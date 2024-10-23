#nullable enable

using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using Barotrauma.PerkBehaviors;

namespace Barotrauma
{
    internal readonly record struct PerkCollection(
        ImmutableArray<DisembarkPerkPrefab> Team1Perks,
        ImmutableArray<DisembarkPerkPrefab> Team2Perks)
    {
        public static readonly PerkCollection Empty = new PerkCollection(ImmutableArray<DisembarkPerkPrefab>.Empty, ImmutableArray<DisembarkPerkPrefab>.Empty);

        public void ApplyAll(IReadOnlyCollection<Character> team1Characters, IReadOnlyCollection<Character> team2Characters)
        {
            // Usually there should only be 1 mission active on pvp and mission modes
            bool anyMissionDoesNotLoadSubs = GameMain.GameSession.Missions.Any(static m => !m.Prefab.LoadSubmarines);

            foreach (var team1Perk in Team1Perks)
            {
                GameAnalyticsManager.AddDesignEvent("DisembarkPerk:" + team1Perk.Identifier);
                foreach (PerkBase behavior in team1Perk.PerkBehaviors)
                {
                    if (anyMissionDoesNotLoadSubs && !behavior.CanApplyWithoutSubmarine()) { continue; }
#if CLIENT
                    if (behavior.Simulation == PerkSimulation.ServerOnly) { continue; }
#endif
                    behavior.ApplyOnRoundStart(team1Characters, Submarine.MainSubs[0]);
                }
            }

            if (Submarine.MainSubs[1] is not null)
            {
                foreach (var team2Perk in Team2Perks)
                {
                    GameAnalyticsManager.AddDesignEvent("DisembarkPerk:" + team2Perk.Identifier);
                    foreach (PerkBase behavior in team2Perk.PerkBehaviors)
                    {
                        if (anyMissionDoesNotLoadSubs && !behavior.CanApplyWithoutSubmarine()) { continue; }
#if CLIENT
                        if (behavior.Simulation == PerkSimulation.ServerOnly) { continue; }
#endif
                        behavior.ApplyOnRoundStart(team2Characters, Submarine.MainSubs[1]);
                    }
                }
            }
        }
    }

    partial class GameSession
    {
#if DEBUG
        public static float MinimumLoadingTime;
#endif

        public enum InfoFrameTab { Crew, Mission, MyCharacter, Traitor };

        public Version LastSaveVersion { get; set; } = GameMain.Version;

        public readonly EventManager EventManager;

        public GameMode? GameMode;

        //two locations used as the start and end in the MP mode
        private Location[]? dummyLocations;
        public CrewManager? CrewManager;

        public float RoundDuration
        {
            get; private set;
        }

        public double TimeSpentCleaning, TimeSpentPainting;

        private readonly List<Mission> missions = new List<Mission>();
        public IEnumerable<Mission> Missions { get { return missions; } }

        private readonly HashSet<Character> casualties = new HashSet<Character>();
        public IEnumerable<Character> Casualties { get { return casualties; } }
        
        /// <summary>
        /// Permadeaths per MP account are stored currently just for an achievement ("getoutalive").
        /// The dictionary stores Option<AccountId> directly just to keep the code using it simpler and leaner, but if
        /// this is ever used for something else too, feel free to refactor it to use actual AccountIds.
        /// </summary>
        private Dictionary<Option<AccountId>, int> permadeathsPerAccount = new Dictionary<Option<AccountId>, int>();
        public void IncrementPermadeath(Option<AccountId> accountId)
        {
            permadeathsPerAccount[accountId] = permadeathsPerAccount.GetValueOrDefault(accountId, 0) + 1;
        }
        public int PermadeathCountForAccount(Option<AccountId> accountId)
        {
            return permadeathsPerAccount.GetValueOrDefault(accountId, 0);
        }

        public CharacterTeamType? WinningTeam;

        /// <summary>
        /// Is a round currently running?
        /// </summary>
        public bool IsRunning { get; private set; }

        public bool RoundEnding { get; private set; }

        public Level? Level { get; private set; }
        public LevelData? LevelData { get; private set; }

        public bool MirrorLevel { get; private set; }

        public Map? Map
        {
            get
            {
                return (GameMode as CampaignMode)?.Map;
            }
        }

        public CampaignMode? Campaign
        {
            get
            {
                return GameMode as CampaignMode;
            }
        }
        

        public Location StartLocation
        {
            get
            {
                if (Map != null) { return Map.CurrentLocation; }
                if (dummyLocations == null) 
                { 
                    dummyLocations = LevelData == null ? CreateDummyLocations(seed: string.Empty) : CreateDummyLocations(LevelData); 
                }
                if (dummyLocations == null) { throw new NullReferenceException("dummyLocations is null somehow!"); }
                return dummyLocations[0];
            }
        }

        public Location EndLocation
        {
            get
            {
                if (Map != null) { return Map.SelectedLocation; }
                if (dummyLocations == null)
                {
                    dummyLocations = LevelData == null ? CreateDummyLocations(seed: string.Empty) : CreateDummyLocations(LevelData);
                }
                if (dummyLocations == null) { throw new NullReferenceException("dummyLocations is null somehow!"); }
                return dummyLocations[1];
            }
        }

        public SubmarineInfo SubmarineInfo { get; set; }
        public SubmarineInfo EnemySubmarineInfo { get; set; }
        
        public SubmarineInfo? ForceOutpostModule;
        
        public List<SubmarineInfo> OwnedSubmarines = new List<SubmarineInfo>();

        public Submarine? Submarine { get; set; }

        public CampaignDataPath DataPath { get; set; }

        public bool TraitorsEnabled =>
            GameMain.NetworkMember?.ServerSettings != null &&
            GameMain.NetworkMember.ServerSettings.TraitorProbability > 0.0f;

        partial void InitProjSpecific();

        private GameSession(SubmarineInfo submarineInfo)
        {
            InitProjSpecific();
            SubmarineInfo = submarineInfo;
            EnemySubmarineInfo = SubmarineInfo;
            GameMain.GameSession = this;
            EventManager = new EventManager();
        }

        private GameSession(SubmarineInfo submarineInfo, SubmarineInfo enemySubmarineInfo)
            : this(submarineInfo)
        {
            EnemySubmarineInfo = enemySubmarineInfo;
        }

        /// <summary>
        /// Start a new GameSession. Will be saved to the specified save path (if playing a game mode that can be saved).
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, Option<SubmarineInfo> enemySub, CampaignDataPath dataPath, GameModePreset gameModePreset, CampaignSettings settings, string? seed = null, IEnumerable<Identifier>? missionTypes = null)
            : this(submarineInfo)
        {
            DataPath = dataPath;
            CrewManager = new CrewManager(gameModePreset.IsSinglePlayer);
            GameMode = InstantiateGameMode(gameModePreset, seed, submarineInfo, settings, missionTypes: missionTypes);
            EnemySubmarineInfo = enemySub.TryUnwrap(out var enemySubmarine) ? enemySubmarine : submarineInfo;
            InitOwnedSubs(submarineInfo);
        }

        /// <summary>
        /// Start a new GameSession with a specific pre-selected mission.
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, Option<SubmarineInfo> enemySub, GameModePreset gameModePreset, string? seed = null, IEnumerable<MissionPrefab>? missionPrefabs = null)
            : this(submarineInfo)
        {
            CrewManager = new CrewManager(gameModePreset.IsSinglePlayer);
            GameMode = InstantiateGameMode(gameModePreset, seed, submarineInfo, CampaignSettings.Empty, missionPrefabs: missionPrefabs);
            EnemySubmarineInfo = enemySub.TryUnwrap(out var enemySubmarine) ? enemySubmarine : submarineInfo;
            InitOwnedSubs(submarineInfo);
        }

        /// <summary>
        /// Load a game session from the specified XML document. The session will be saved to the specified path.
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, List<SubmarineInfo> ownedSubmarines, XDocument doc, CampaignDataPath campaignData) : this(submarineInfo)
        {
            DataPath = campaignData;
            GameMain.GameSession = this;
            XElement rootElement = doc.Root ?? throw new NullReferenceException("Game session XML element is invalid: document is null.");

            LastSaveVersion = doc.Root.GetAttributeVersion("version", GameMain.Version);

            foreach (var subElement in rootElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "gamemode": //legacy support
                    case "singleplayercampaign":
#if CLIENT
                        CrewManager = new CrewManager(true);
                        var campaign = SinglePlayerCampaign.Load(subElement);
                        campaign.LoadNewLevel();
                        GameMode = campaign;
                        InitOwnedSubs(submarineInfo, ownedSubmarines);
#else
                        throw new Exception("The server cannot load a single player campaign.");
#endif
                        break;            
                    case "multiplayercampaign":
                        CrewManager = new CrewManager(false);
                        var mpCampaign = MultiPlayerCampaign.LoadNew(subElement);
                        GameMode = mpCampaign;
                        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                        {
                            mpCampaign.LoadNewLevel();
                            InitOwnedSubs(submarineInfo, ownedSubmarines);
                            //save to ensure the campaign ID in the save file matches the one that got assigned to this campaign instance
                            SaveUtil.SaveGame(campaignData, isSavingOnLoading: true);
                        }
                        break;
                    case "permadeaths":
                        permadeathsPerAccount = new Dictionary<Option<AccountId>, int>();
                        foreach (XElement accountElement in subElement.Elements("account"))
                        {
                            if (accountElement.Attribute("id") is XAttribute accountIdAttr &&
                                accountElement.Attribute("permadeathcount") is XAttribute permadeathCountAttr)
                            {
                                try
                                {
                                    permadeathsPerAccount[AccountId.Parse(accountIdAttr.Value)] = int.Parse(permadeathCountAttr.Value);
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.AddWarning($"Exception while trying to load permadeath counts!\n{e}\n id: {accountIdAttr}\n permadeathcount: {permadeathCountAttr}");
                                }
                            }
                        }
                        break;
                }
            }
        }

        private void InitOwnedSubs(SubmarineInfo submarineInfo, List<SubmarineInfo>? ownedSubmarines = null)
        {
            OwnedSubmarines = ownedSubmarines ?? new List<SubmarineInfo>();
            if (submarineInfo != null && !OwnedSubmarines.Any(s => s.Name == submarineInfo.Name))
            {
                OwnedSubmarines.Add(submarineInfo);
            }
        }

        private GameMode InstantiateGameMode(GameModePreset gameModePreset, string? seed, SubmarineInfo selectedSub, CampaignSettings settings, IEnumerable<MissionPrefab>? missionPrefabs = null, IEnumerable<Identifier>? missionTypes = null)
        {
            if (gameModePreset.GameModeType == typeof(CoOpMode))
            {
                return missionPrefabs != null ?
                    new CoOpMode(gameModePreset, missionPrefabs) :
                    new CoOpMode(gameModePreset, missionTypes, seed ?? ToolBox.RandomSeed(8));
            }
            else if (gameModePreset.GameModeType == typeof(PvPMode))
            {
                return missionPrefabs != null ?
                    new PvPMode(gameModePreset, missionPrefabs) :
                    new PvPMode(gameModePreset, missionTypes, seed ?? ToolBox.RandomSeed(8));
            }
            else if (gameModePreset.GameModeType == typeof(MultiPlayerCampaign))
            {
                var campaign = MultiPlayerCampaign.StartNew(seed ?? ToolBox.RandomSeed(8), settings);
                if (selectedSub != null)
                {
                    campaign.Bank.Deduct(selectedSub.Price);
                    campaign.Bank.Balance = Math.Max(campaign.Bank.Balance, 0);
#if SERVER
                    if (GameMain.Server?.ServerSettings?.NewCampaignDefaultSalary is { } salary)
                    {
                        campaign.Bank.SetRewardDistribution((int)Math.Round(salary, digits: 0));
                    }
#endif
                }
                return campaign;
            }
#if CLIENT
            else if (gameModePreset.GameModeType == typeof(SinglePlayerCampaign))
            {
                var campaign = SinglePlayerCampaign.StartNew(seed ?? ToolBox.RandomSeed(8), settings);
                if (selectedSub != null)
                {
                    campaign.Bank.TryDeduct(selectedSub.Price);
                    campaign.Bank.Balance = Math.Max(campaign.Bank.Balance, 0);
                }
                return campaign;
            }
            else if (gameModePreset.GameModeType == typeof(TutorialMode))
            {
                return new TutorialMode(gameModePreset);
            }
            else if (gameModePreset.GameModeType == typeof(TestGameMode))
            {
                return new TestGameMode(gameModePreset);
            }
#endif
            else if (gameModePreset.GameModeType == typeof(GameMode))
            {
                return new GameMode(gameModePreset);
            }
            else
            {
                throw new Exception($"Could not find a game mode of the type \"{gameModePreset.GameModeType}\"");
            }
        }

        public static Location[] CreateDummyLocations(LevelData levelData, LocationType? forceLocationType = null)
        {
            MTRandom rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            var forceParams = levelData?.ForceOutpostGenerationParams;
            if (forceLocationType == null &&
                forceParams != null && forceParams.AllowedLocationTypes.Any() && !forceParams.AllowedLocationTypes.Contains("Any".ToIdentifier()))
            {
                forceLocationType =
                    LocationType.Prefabs.Where(lt => forceParams.AllowedLocationTypes.Contains(lt.Identifier)).GetRandom(rand);
            }
            var dummyLocations = CreateDummyLocations(rand, forceLocationType);
            List<Faction> factions = new List<Faction>();
            foreach (var factionPrefab in FactionPrefab.Prefabs)
            {
                factions.Add(new Faction(new CampaignMetadata(), factionPrefab));
            }
            foreach (var location in dummyLocations)
            {
                if (location.Type.HasOutpost)
                {
                    location.Faction = CampaignMode.GetRandomFaction(factions, rand, secondary: false);
                    location.SecondaryFaction = CampaignMode.GetRandomFaction(factions, rand, secondary: true);
                }
            }
            return dummyLocations;
        }

        public static Location[] CreateDummyLocations(string seed, LocationType? forceLocationType = null)
        {
            return CreateDummyLocations(new MTRandom(ToolBox.StringToInt(seed)), forceLocationType);
        }

        private static Location[] CreateDummyLocations(Random rand, LocationType? forceLocationType = null)
        {
            var dummyLocations = new Location[2];
            for (int i = 0; i < 2; i++)
            {
                dummyLocations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f), null, rand, requireOutpost: true, forceLocationType);
            }
            return dummyLocations;
        }

        public static bool ShouldApplyDisembarkPoints(GameModePreset? preset)
        {
            if (preset is null) { return true; } // sure I guess?

            return preset == GameModePreset.Sandbox ||
                   preset == GameModePreset.Mission ||
                   preset == GameModePreset.PvP;
        }

        public void LoadPreviousSave()
        {
            Submarine.Unload();
            SaveUtil.LoadGame(DataPath);
        }

        /// <summary>
        /// Switch to another submarine. The sub is loaded when the next round starts.
        /// </summary>
        public void SwitchSubmarine(SubmarineInfo newSubmarine, bool transferItems, Client? client = null)
        {
            if (!OwnedSubmarines.Any(s => s.Name == newSubmarine.Name))
            {
                OwnedSubmarines.Add(newSubmarine);
            }
            else
            {
                // Fetch owned submarine data as the newSubmarine is just the base submarine
                for (int i = 0; i < OwnedSubmarines.Count; i++)
                {
                    if (OwnedSubmarines[i].Name == newSubmarine.Name)
                    {
                        newSubmarine = OwnedSubmarines[i];
                        break;
                    }
                }
            }
            Campaign!.PendingSubmarineSwitch = newSubmarine;
            Campaign!.TransferItemsOnSubSwitch = transferItems;
        }

        public bool TryPurchaseSubmarine(SubmarineInfo newSubmarine, Client? client = null)
        {
            if (Campaign is null) { return false; }
            int price = newSubmarine.GetPrice();
            if ((GameMain.NetworkMember is null || GameMain.NetworkMember is { IsServer: true }) && !Campaign.TryPurchase(client, price)) { return false; }
            if (!OwnedSubmarines.Any(s => s.Name == newSubmarine.Name))
            {
                GameAnalyticsManager.AddMoneySpentEvent(price, GameAnalyticsManager.MoneySink.SubmarinePurchase, newSubmarine.Name);
                OwnedSubmarines.Add(newSubmarine);
#if SERVER
                (Campaign as MultiPlayerCampaign)?.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.SubList);
#endif
            }
            return true;
        }

        public bool IsSubmarineOwned(SubmarineInfo query)
        {
            return 
                Submarine.MainSub?.Info.Name == query.Name || 
                (OwnedSubmarines != null && OwnedSubmarines.Any(os => os.Name == query.Name));
        }

        public bool IsCurrentLocationRadiated()
        {
            if (Map?.CurrentLocation == null || Campaign == null) { return false; }

            bool isRadiated = Map.CurrentLocation.IsRadiated();

            if (Level.Loaded?.EndLocation is { } endLocation)
            {
                isRadiated |= endLocation.IsRadiated();
            }

            return isRadiated;
        }
        
        public void StartRound(string levelSeed, float? difficulty = null, LevelGenerationParams? levelGenerationParams = null, Identifier forceBiome = default)
        {
            if (GameMode == null) { return; }
            
            LevelData? randomLevel = null;
            bool pvpOnly = GameMode is PvPMode;
            foreach (Mission mission in Missions.Union(GameMode.Missions))
            {
                MissionPrefab missionPrefab = mission.Prefab;
                if (missionPrefab != null &&
                    missionPrefab.AllowedLocationTypes.Any() &&
                    !missionPrefab.AllowedConnectionTypes.Any())
                {
                    Random rand = new MTRandom(ToolBox.StringToInt(levelSeed));
                    LocationType locationType = LocationType.Prefabs
                        .Where(lt => missionPrefab.AllowedLocationTypes.Any(m => m == lt.Identifier))
                        .GetRandom(rand)!;
                    dummyLocations = CreateDummyLocations(levelSeed, locationType);

                    if (!tryCreateFaction(mission.Prefab.RequiredLocationFaction, dummyLocations, static (loc, fac) => loc.Faction = fac))
                    {
                        tryCreateFaction(locationType.Faction, dummyLocations, static (loc, fac) => loc.Faction = fac);
                        tryCreateFaction(locationType.SecondaryFaction, dummyLocations, static (loc, fac) => loc.SecondaryFaction = fac);
                    }
                    static bool tryCreateFaction(Identifier factionIdentifier, Location[] locations, Action<Location, Faction> setter)
                    {
                        if (factionIdentifier.IsEmpty) { return false; }
                        if (!FactionPrefab.Prefabs.TryGet(factionIdentifier, out var prefab)) { return false; }
                        if (locations.Length == 0) { return false; }

                        var newFaction = new Faction(metadata: null, prefab);
                        for (int i = 0; i < locations.Length; i++)
                        {
                            setter(locations[i], newFaction);
                        }

                        return true;
                    }

                    randomLevel = LevelData.CreateRandom(levelSeed, difficulty, levelGenerationParams, requireOutpost: true, biomeId: forceBiome, pvpOnly: pvpOnly);
                    break;
                }
            }
            randomLevel ??= LevelData.CreateRandom(levelSeed, difficulty, levelGenerationParams, biomeId: forceBiome, pvpOnly: pvpOnly);
            StartRound(randomLevel);
        }

        private bool TryGenerateStationAroundModule(SubmarineInfo? moduleInfo, out Submarine? outpostSub)
        {
            outpostSub = null;
            if (moduleInfo == null) { return false; }

            var allSuitableOutpostParams = OutpostGenerationParams.OutpostParams
                .Where(outpostParam => IsOutpostParamsSuitable(outpostParam));
            
            // allow for fallback when there are no options with allowed location types defined
            var suitableOutpostParams = 
                allSuitableOutpostParams.Where(p => p.AllowedLocationTypes.Any()).GetRandomUnsynced() ?? 
                allSuitableOutpostParams.GetRandomUnsynced();

            bool IsOutpostParamsSuitable(OutpostGenerationParams outpostParams)
            {
                bool moduleWorksWithOutpostParams = outpostParams.ModuleCounts.Any(moduleCount => moduleInfo.OutpostModuleInfo.ModuleFlags.Contains(moduleCount.Identifier));
                if (!moduleWorksWithOutpostParams) { return false; }
                
                // is there a location that these outpostParams are suitable for, and which this module is suitable for
                return LocationType.Prefabs.Any(locationType => IsSuitableLocationType(moduleInfo.OutpostModuleInfo.AllowedLocationTypes, locationType.Identifier) 
                                                                && IsSuitableLocationType(outpostParams.AllowedLocationTypes, locationType.Identifier)); 

                bool IsSuitableLocationType(IEnumerable<Identifier> allowedLocationTypes, Identifier locationType)
                {
                    return allowedLocationTypes.None() ||  allowedLocationTypes.Contains("Any".ToIdentifier()) || allowedLocationTypes.Contains(locationType);
                }
            }
            
            if (suitableOutpostParams == null)
            {
                DebugConsole.AddWarning("No suitable generation parameters found for ForceOutpostModule, skipping outpost generation!");
                return false;
            }
            
            var suitableLocationType = LocationType.Prefabs.Where(locationType => 
                suitableOutpostParams.AllowedLocationTypes.Contains(locationType.Identifier)).GetRandomUnsynced();

            if (suitableLocationType == null)
            {
                DebugConsole.AddWarning("No suitable location type found for ForceOutpostModule, skipping outpost generation!");
                return false;
            }
            
            // try to find a required faction id matching our module
            var requiredFactionModuleCount = suitableOutpostParams.ModuleCounts.FirstOrDefault(mc => !mc.RequiredFaction.IsEmpty && moduleInfo.OutpostModuleInfo.ModuleFlags.Contains(mc.Identifier));
            Identifier requiredFactionId = requiredFactionModuleCount?.RequiredFaction ?? Identifier.Empty;
            
            if (requiredFactionId.IsEmpty)
            {
                // no matching faction requirements, generate normally from location type
                outpostSub = OutpostGenerator.Generate(suitableOutpostParams, suitableLocationType);
                return outpostSub != null;
            }
            
            // if there is a faction requirement for the module, create a dummy location and augment its factions to match
            var dummyLocations = CreateDummyLocations("1337", suitableLocationType);
            var dummyLocation = dummyLocations[0];
                
            if (FactionPrefab.Prefabs.TryGet(requiredFactionId, out FactionPrefab? factionPrefab))
            {
                if (factionPrefab.ControlledOutpostPercentage > factionPrefab.SecondaryControlledOutpostPercentage)
                {
                    dummyLocation.Faction = new Faction(null, factionPrefab);
                }
                else
                {
                    dummyLocation.SecondaryFaction = new Faction(null, factionPrefab);
                }
                
                outpostSub = OutpostGenerator.Generate(suitableOutpostParams, dummyLocation);
                return outpostSub != null;
            }

             return false;
        }

        public void StartRound(LevelData? levelData, bool mirrorLevel = false, SubmarineInfo? startOutpost = null, SubmarineInfo? endOutpost = null)
        {
#if DEBUG
            DateTime startTime = DateTime.Now;
#endif
            RoundDuration = 0.0f;
            AfflictionPrefab.LoadAllEffectsAndTreatmentSuitabilities();

            MirrorLevel = mirrorLevel;
            if (SubmarineInfo == null)
            {
                DebugConsole.ThrowError("Couldn't start game session, submarine not selected.");
                return;
            }
            if (SubmarineInfo.IsFileCorrupted)
            {
                DebugConsole.ThrowError("Couldn't start game session, submarine file corrupted.");
                return;
            }
            if (SubmarineInfo.SubmarineElement.Elements().Count() == 0)
            {
                DebugConsole.ThrowError("Couldn't start game session, saved submarine is empty. The submarine file may be corrupted.");
                return;
            }

            Submarine.LockX = Submarine.LockY = false;

            LevelData = levelData;

            Submarine.Unload();

            bool loadSubmarine = GameMode!.Missions.None(m => !m.Prefab.LoadSubmarines);
            
            // attempt to generate an outpost for the main sub, with the forced module inside it
            if (loadSubmarine)
            {
                if (TryGenerateStationAroundModule(ForceOutpostModule, out Submarine? outpostSub))
                {
                    Submarine = Submarine.MainSub = outpostSub ?? new Submarine(SubmarineInfo);
                }
                else
                {
                    Submarine = Submarine.MainSub = new Submarine(SubmarineInfo);
                }
                foreach (Submarine sub in Submarine.GetConnectedSubs())
                {
                    sub.TeamID = CharacterTeamType.Team1;
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.Submarine != sub) { continue; }
                        foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                        {
                            wifiComponent.TeamID = sub.TeamID;
                        }
                    }
                }
            }
            else
            {
                Submarine = Submarine.MainSub = null;
            }            

            GameMode!.AddExtraMissions(LevelData);
            foreach (Mission mission in GameMode!.Missions)
            {
                // setting level for missions that may involve difficulty-related submarine creation
                mission.SetLevel(levelData);
            }

            if (Submarine.MainSubs[1] == null && loadSubmarine)
            {
                var enemySubmarineInfo = GameMode is PvPMode ? EnemySubmarineInfo : GameMode.Missions.FirstOrDefault(m => m.EnemySubmarineInfo != null)?.EnemySubmarineInfo;
                if (enemySubmarineInfo != null)
                {
                    Submarine.MainSubs[1] = new Submarine(enemySubmarineInfo);
                }
            }

            if (GameMain.NetworkMember?.ServerSettings is { LockAllDefaultWires: true } &&
                Submarine.MainSubs[0] != null)
            {
                List<Item> items = new List<Item>();
                items.AddRange(Submarine.MainSubs[0].GetItems(alsoFromConnectedSubs: true));
                if (Submarine.MainSubs[1] != null)
                {
                    items.AddRange(Submarine.MainSubs[1].GetItems(alsoFromConnectedSubs: true));
                }
                foreach (Item item in items)
                {
                    if (item.GetComponent<CircuitBox>() is { } cb)
                    {
                        cb.TemporarilyLocked = true;
                    }

                    Wire wire = item.GetComponent<Wire>();
                    if (wire != null && !wire.NoAutoLock && wire.Connections.Any(c => c != null)) { wire.Locked = true; }                    
                }
            }

            Level? level = null;
            if (levelData != null)
            {
                level = Level.Generate(levelData, mirrorLevel, StartLocation, EndLocation, startOutpost, endOutpost);
            }

            InitializeLevel(level);

            //Clear out the cached grids and force update
            Powered.Grids.Clear();

            casualties.Clear();

            GameAnalyticsManager.AddProgressionEvent(
                GameAnalyticsManager.ProgressionStatus.Start,
                GameMode?.Preset?.Identifier.Value ?? "none");

            string eventId = "StartRound:" + (GameMode?.Preset?.Identifier.Value ?? "none") + ":";
            GameAnalyticsManager.AddDesignEvent(eventId + "Submarine:" + (Submarine.MainSub?.Info?.Name ?? "none"));
            GameAnalyticsManager.AddDesignEvent(eventId + "GameMode:" + (GameMode?.Preset?.Identifier.Value ?? "none"));
            GameAnalyticsManager.AddDesignEvent(eventId + "CrewSize:" + (CrewManager?.GetCharacterInfos()?.Count() ?? 0));
            foreach (Mission mission in missions)
            {
                GameAnalyticsManager.AddDesignEvent(eventId + "MissionType:" + (mission.Prefab.Type.ToString() ?? "none") + ":" + mission.Prefab.Identifier);
            }
            if (Level.Loaded != null)
            {
                Identifier levelId = (Level.Loaded.Type == LevelData.LevelType.Outpost ?
                    Level.Loaded.StartOutpost?.Info?.OutpostGenerationParams?.Identifier :
                    Level.Loaded.GenerationParams?.Identifier) ?? "null".ToIdentifier();
                GameAnalyticsManager.AddDesignEvent(eventId + "LevelType:" + Level.Loaded.Type.ToString() + ":" + levelId);
            }
            GameAnalyticsManager.AddDesignEvent(eventId + "Biome:" + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none"));
#if CLIENT
            if (GameMode is TutorialMode tutorialMode)
            {
                GameAnalyticsManager.AddDesignEvent(eventId + tutorialMode.Tutorial.Identifier);
                if (GameMain.IsFirstLaunch)
                {
                    GameAnalyticsManager.AddDesignEvent("FirstLaunch:" + eventId + tutorialMode.Tutorial.Identifier);
                }
            }
            GameAnalyticsManager.AddDesignEvent($"{eventId}HintManager:{(HintManager.Enabled ? "Enabled" : "Disabled")}");
#endif
            var campaignMode = GameMode as CampaignMode;
            if (campaignMode != null)
            {
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:RadiationEnabled:" + campaignMode.Settings.RadiationEnabled);
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:WorldHostility:" + campaignMode.Settings.WorldHostility);
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:ShowHuskWarning:" + campaignMode.Settings.ShowHuskWarning);
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:StartItemSet:" + campaignMode.Settings.StartItemSet);
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:MaxMissionCount:" + campaignMode.Settings.MaxMissionCount);
                //log the multipliers as integers to reduce the number of distinct values
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:RepairFailMultiplier:" + (int)(campaignMode.Settings.RepairFailMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:FuelMultiplier:" + (int)(campaignMode.Settings.FuelMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:MissionRewardMultiplier:" + (int)(campaignMode.Settings.MissionRewardMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:CrewVitalityMultiplier:" + (int)(campaignMode.Settings.CrewVitalityMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:NonCrewVitalityMultiplier:" + (int)(campaignMode.Settings.NonCrewVitalityMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:OxygenMultiplier:" + (int)(campaignMode.Settings.OxygenMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:RepairFailMultiplier:" + (int)(campaignMode.Settings.RepairFailMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:ShipyardPriceMultiplier:" + (int)(campaignMode.Settings.ShipyardPriceMultiplier * 100));
                GameAnalyticsManager.AddDesignEvent("CampaignSettings:ShopPriceMultiplier:" + (int)(campaignMode.Settings.ShopPriceMultiplier * 100));

                bool firstTimeInBiome = Map != null && !Map.Connections.Any(c => c.Passed && c.Biome == LevelData!.Biome);
                if (firstTimeInBiome)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none") + "Discovered:Playtime", campaignMode.TotalPlayTime);
                    GameAnalyticsManager.AddDesignEvent(eventId + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none") + "Discovered:PassedLevels", campaignMode.TotalPassedLevels);
                }
                if (GameMain.NetworkMember?.ServerSettings is { } serverSettings)
                {
                    GameAnalyticsManager.AddDesignEvent("ServerSettings:RespawnMode:" + serverSettings.RespawnMode);
                    GameAnalyticsManager.AddDesignEvent("ServerSettings:IronmanMode:" + serverSettings.IronmanModeActive);
                    GameAnalyticsManager.AddDesignEvent("ServerSettings:AllowBotTakeoverOnPermadeath:" + serverSettings.AllowBotTakeoverOnPermadeath);
                }
            }

#if DEBUG
            double startDuration = (DateTime.Now - startTime).TotalSeconds;
            if (startDuration < MinimumLoadingTime)
            {
                int sleepTime = (int)((MinimumLoadingTime - startDuration) * 1000);
                DebugConsole.NewMessage($"Stalling round start by {sleepTime / 1000.0f} s (minimum loading time set to {MinimumLoadingTime})...", Color.Magenta);
                System.Threading.Thread.Sleep(sleepTime);
            }
#endif
#if CLIENT
            var existingRoundSummary = GUIMessageBox.MessageBoxes.Find(mb => mb.UserData is RoundSummary)?.UserData as RoundSummary;
            if (existingRoundSummary?.ContinueButton != null)
            {
                existingRoundSummary.ContinueButton.Visible = true;
            }

            CharacterHUD.ClearBossProgressBars();

            RoundSummary = new RoundSummary(GameMode, Missions, StartLocation, EndLocation);

            if (GameMode is not TutorialMode && GameMode is not TestGameMode)
            {
                GUI.AddMessage("", Color.Transparent, 3.0f, playSound: false);
                if (EndLocation != null && levelData != null)
                {
                    GUI.AddMessage(levelData.Biome.DisplayName, Color.Lerp(Color.CadetBlue, Color.DarkRed, levelData.Difficulty / 100.0f), 5.0f, playSound: false);
                    GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Destination"), EndLocation.DisplayName), Color.CadetBlue, playSound: false);
                    var missionsToShow = missions.Where(m => m.Prefab.ShowStartMessage);
                    if (missionsToShow.Count() > 1)
                    {
                        string joinedMissionNames = string.Join(", ", missions.Select(m => m.Name));
                        GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Mission"), joinedMissionNames), Color.CadetBlue, playSound: false);
                    }
                    else
                    {
                        var mission = missionsToShow.FirstOrDefault();
                        GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Mission"), mission?.Name ?? TextManager.Get("None")), Color.CadetBlue, playSound: false);
                    }
                }
                else
                {
                    GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Location"), StartLocation.DisplayName), Color.CadetBlue, playSound: false);
                }
            }

            ReadyCheck.ReadyCheckCooldown = DateTime.MinValue;
            GUI.PreventPauseMenuToggle = false;
            HintManager.OnRoundStarted();
            EnableEventLogNotificationIcon(enabled: false);
#endif
            if (campaignMode is { ItemsRelocatedToMainSub: true })
            {
#if SERVER
                GameMain.Server.SendChatMessage(TextManager.Get("itemrelocated").Value, ChatMessageType.ServerMessageBoxInGame);
#else
                if (campaignMode.IsSinglePlayer)
                {
                    new GUIMessageBox(string.Empty, TextManager.Get("itemrelocated"));
                }
#endif
                campaignMode.ItemsRelocatedToMainSub = false;
            }

            EventManager?.EventLog?.Clear();
            if (campaignMode is { DivingSuitWarningShown: false } &&
                Level.Loaded != null && Level.Loaded.GetRealWorldDepth(0) > 4000)
            {
#if CLIENT
                CoroutineManager.Invoke(() => new GUIMessageBox(TextManager.Get("warning"), TextManager.Get("hint.upgradedivingsuits")), delay: 5.0f);
#endif
                campaignMode.DivingSuitWarningShown = true;
            }
        }

        private void InitializeLevel(Level? level)
        {
            //make sure no status effects have been carried on from the next round
            //(they should be stopped in EndRound, this is a safeguard against cases where the round is ended ungracefully)
            StatusEffect.StopAll();

            bool forceDocking = false;
#if CLIENT
            GameMain.LightManager.LosEnabled = (GameMain.Client == null || GameMain.Client.CharacterInfo != null) && !GameMain.DevMode;
            if (GameMain.LightManager.LosEnabled) { GameMain.LightManager.LosAlpha = 1f; }
            if (GameMain.Client == null) { GameMain.LightManager.LosMode = GameSettings.CurrentConfig.Graphics.LosMode; }
            forceDocking = GameMode is TutorialMode;
#endif
            LevelData = level?.LevelData;
            Level = level;

            PlaceSubAtInitialPosition(Submarine, Level, placeAtStart: true, forceDocking: forceDocking);

            foreach (var sub in Submarine.Loaded)
            {
                // TODO: Currently there's no need to check these on ruins, but that might change -> Could maybe just check if the body is static?
                if (sub.Info.IsOutpost || sub.Info.IsBeacon || sub.Info.IsWreck)
                {
                    sub.DisableObstructedWayPoints();
                }
            }

            Entity.Spawner = new EntitySpawner();

            if (GameMode != null)
            {
                missions.Clear();
                missions.AddRange(GameMode.Missions);
                GameMode.Start();
                foreach (Mission mission in missions)
                {
                    int prevEntityCount = Entity.GetEntities().Count;
                    mission.Start(Level.Loaded);
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Entity.GetEntities().Count != prevEntityCount)
                    {
                        DebugConsole.ThrowError(
                            $"Entity count has changed after starting a mission ({mission.Prefab.Identifier}) as a client. " +
                            "The clients should not instantiate entities themselves when starting the mission," +
                            " but instead the server should inform the client of the spawned entities using Mission.ServerWriteInitial.");
                    }
                }

#if CLIENT
                ObjectiveManager.ResetObjectives();
#endif
                EventManager?.StartRound(Level.Loaded);
                AchievementManager.OnStartRound(Level?.LevelData.Biome);

                GameMode.ShowStartMessage();

                if (GameMain.NetworkMember == null) 
                {
                    //only place items and corpses here in single player
                    //the server does this after loading the respawn shuttle
                    if (Level != null)
                    {
                        if (GameMain.GameSession.Missions.None(m => !m.Prefab.AllowOutpostNPCs))
                        {
                            Level.SpawnNPCs();
                        }
                        Level.SpawnCorpses();
                        Level.PrepareBeaconStation();
                    }
                    else
                    {
                        // Spawn npcs in the sub editor test mode.
                        foreach (Submarine sub in Submarine.Loaded)
                        {
                            if (sub?.Info?.OutpostGenerationParams != null)
                            {
                                OutpostGenerator.SpawnNPCs(StartLocation, sub);
                            }
                        }
                    }
                    AutoItemPlacer.SpawnItems(Campaign?.Settings.StartItemSet);
                }
                if (GameMode is MultiPlayerCampaign mpCampaign)
                {
                    mpCampaign.UpgradeManager.ApplyUpgrades();
                    mpCampaign.UpgradeManager.SanityCheckUpgrades();
                }
            }

            CreatureMetrics.RecentlyEncountered.Clear();

            GameMain.GameScreen.Cam.Position = Character.Controlled?.WorldPosition ?? Submarine.MainSub?.WorldPosition ?? Submarine.Loaded.First().WorldPosition;
            RoundDuration = 0.0f;
            GameMain.ResetFrameTime();
            IsRunning = true;
        }

        public static void PlaceSubAtInitialPosition(Submarine? sub, Level? level, bool placeAtStart = true, bool forceDocking = false)
        {
            if (level == null || sub == null)
            {
                sub?.SetPosition(Vector2.Zero);
                return;
            }

            Submarine outpost = placeAtStart ? level.StartOutpost : level.EndOutpost;

            var originalSubPos = sub.WorldPosition;
            var spawnPoint = WayPoint.WayPointList.Find(wp => wp.SpawnType.HasFlag(SpawnType.Submarine) && wp.Submarine == outpost);
            if (spawnPoint != null)
            {
                //pre-determine spawnpoint, just use it directly
                sub.SetPosition(spawnPoint.WorldPosition);
                sub.NeutralizeBallast();
                sub.EnableMaintainPosition();
            }
            else if (outpost != null)
            {
                //start by placing the sub below the outpost
                Rectangle outpostBorders = outpost.GetDockedBorders();
                Rectangle subBorders = sub.GetDockedBorders();

                sub.SetPosition(
                    outpost.WorldPosition -
                    new Vector2(0.0f, outpostBorders.Height / 2 + subBorders.Height / 2));

                //find the port that's the nearest to the outpost and dock if one is found
                float closestDistance = 0.0f;
                DockingPort? myPort = null, outPostPort = null;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (port.IsHorizontal || port.Docked) { continue; }
                    if (port.Item.Submarine == outpost)
                    {
                        if (port.DockingTarget == null || (outPostPort != null && !outPostPort.MainDockingPort && port.MainDockingPort))
                        {
                            outPostPort = port;
                        }
                        continue;
                    }
                    if (port.Item.Submarine != sub) { continue; }

                    //the submarine port has to be at the top of the sub
                    if (port.Item.WorldPosition.Y < sub.WorldPosition.Y) { continue; }

                    float dist = Vector2.DistanceSquared(port.Item.WorldPosition, outpost.WorldPosition);
                    if ((myPort == null || dist < closestDistance || port.MainDockingPort) && !(myPort?.MainDockingPort ?? false))
                    {
                        myPort = port;
                        closestDistance = dist;
                    }
                }

                if (myPort != null && outPostPort != null)
                {
                    Vector2 portDiff = myPort.Item.WorldPosition - sub.WorldPosition;
                    Vector2 spawnPos = (outPostPort.Item.WorldPosition - portDiff) - Vector2.UnitY * outPostPort.DockedDistance;

                    bool startDocked = level.Type == LevelData.LevelType.Outpost || forceDocking;
                    if (startDocked)
                    {
                        sub.SetPosition(spawnPos);
                        myPort.Dock(outPostPort);
                        myPort.Lock(isNetworkMessage: true, applyEffects: false);
                    }
                    else
                    {
                        sub.SetPosition(spawnPos - Vector2.UnitY * 100.0f);
                        sub.NeutralizeBallast();
                        sub.EnableMaintainPosition();
                    }
                }
                else
                {
                    sub.NeutralizeBallast();
                    sub.EnableMaintainPosition();
                }

            }
            else
            {
                sub.SetPosition(sub.FindSpawnPos(placeAtStart ? level.StartPosition : level.EndPosition));
                sub.NeutralizeBallast();
                sub.EnableMaintainPosition();
            }

            // Make sure that linked subs which are NOT docked to the main sub
            // (but still close enough to NOT be considered as 'left behind')
            // are also moved to keep their relative position to the main sub
            var linkedSubs = MapEntity.MapEntityList.FindAll(me => me is LinkedSubmarine);
            foreach (LinkedSubmarine ls in linkedSubs)
            {
                if (ls.Sub == null || ls.Submarine != sub) { continue; }
                if (!ls.LoadSub || ls.Sub.DockedTo.Contains(sub)) { continue; }
                if (sub.Info.LeftBehindDockingPortIDs.Contains(ls.OriginalLinkedToID)) { continue; }
                if (ls.Sub.Info.SubmarineElement.Attribute("location") != null) { continue; }
                ls.SetPositionRelativeToMainSub();
            }
        }

        public void Update(float deltaTime)
        {
            RoundDuration += deltaTime;
            EventManager?.Update(deltaTime);
            GameMode?.Update(deltaTime);
            //backwards for loop because the missions may get completed and removed from the list in Update()
            for (int i = missions.Count - 1; i >= 0; i--)
            {
                missions[i].Update(deltaTime);
            }
            UpdateProjSpecific(deltaTime);
        }

        public Mission? GetMission(int index)
        {
            if (index < 0 || index >= missions.Count) { return null; }
            return missions[index];
        }

        public int GetMissionIndex(Mission mission)
        {
            return missions.IndexOf(mission);
        }

        public void EnforceMissionOrder(List<Identifier> missionIdentifiers)
        {
            List<Mission> sortedMissions = new List<Mission>();
            foreach (Identifier missionId in missionIdentifiers)
            {
                var matchingMission = missions.Find(m => m.Prefab.Identifier == missionId);
                if (matchingMission == null) { continue; }
                sortedMissions.Add(matchingMission);
                missions.Remove(matchingMission);
            }
            missions.AddRange(sortedMissions);
        }

        partial void UpdateProjSpecific(float deltaTime);

        /// <summary>
        /// Returns a list of crew characters currently in the game with a given filter.
        /// </summary>
        /// <param name="type">Character type filter</param>
        /// <returns></returns>
        /// <remarks>
        /// In singleplayer mode the CharacterType.Player returns the currently controlled player.
        /// </remarks>
        public static ImmutableHashSet<Character> GetSessionCrewCharacters(CharacterType type)
        {
            if (GameMain.GameSession?.CrewManager is not { } crewManager) { return ImmutableHashSet<Character>.Empty; }

            IEnumerable<Character> players;
            IEnumerable<Character> bots;
            HashSet<Character> characters = new HashSet<Character>();

#if SERVER
            players = GameMain.Server.ConnectedClients.Select(c => c.Character).Where(c => c?.Info != null && !c.IsDead);
            bots = crewManager.GetCharacters().Where(c => !c.IsRemotePlayer);
#elif CLIENT
            players = crewManager.GetCharacters().Where(static c => c.IsPlayer);
            bots = crewManager.GetCharacters().Where(static c => c.IsBot);
#endif
            if (type.HasFlag(CharacterType.Bot))
            {
                foreach (Character bot in bots) { characters.Add(bot); }
            }

            if (type.HasFlag(CharacterType.Player))
            {
                foreach (Character player in players) { characters.Add(player); }
            }

            return characters.ToImmutableHashSet();
        }

#if SERVER
        private double LastEndRoundErrorMessageTime;
#endif

        public void EndRound(string endMessage, CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None, TraitorManager.TraitorResults? traitorResults = null)
        {
            RoundEnding = true;

            //Clear the grids to allow for garbage collection
            Powered.Grids.Clear();
            Powered.ChangedConnections.Clear();

            try
            {
                EventManager?.TriggerOnEndRoundActions();

                ImmutableHashSet<Character> crewCharacters = GetSessionCrewCharacters(CharacterType.Both);
                int prevMoney = GetAmountOfMoney(crewCharacters);
                foreach (Mission mission in missions)
                {
                    mission.End();
                }

                foreach (Character character in crewCharacters)
                {
                    character.CheckTalents(AbilityEffectType.OnRoundEnd);
                }

                if (missions.Any())
                {
                    if (missions.Any(m => m.Completed))
                    {
                        foreach (Character character in crewCharacters)
                        {
                            character.CheckTalents(AbilityEffectType.OnAnyMissionCompleted);
                        }
                    }

                    if (missions.All(m => m.Completed))
                    {
                        foreach (Character character in crewCharacters)
                        {
                            character.CheckTalents(AbilityEffectType.OnAllMissionsCompleted);
                        }
                    }
                }

#if CLIENT
                if (GUI.PauseMenuOpen)
                {
                    GUI.TogglePauseMenu();
                }
                if (IsTabMenuOpen)
                {
                    ToggleTabMenu();
                }
                DeathPrompt?.Close();
                DeathPrompt.CloseBotPanel();

                GUI.PreventPauseMenuToggle = true;

                if (GameMode is not TestGameMode && Screen.Selected == GameMain.GameScreen && RoundSummary != null && transitionType != CampaignMode.TransitionType.End)
                {
                    GUI.ClearMessages();
                    GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData is RoundSummary);
                    GUIFrame summaryFrame = RoundSummary.CreateSummaryFrame(this, endMessage, transitionType, traitorResults);
                    GUIMessageBox.MessageBoxes.Add(summaryFrame);
                    RoundSummary.ContinueButton.OnClicked = (_, __) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; };
                }

                if (GameMain.NetLobbyScreen != null) { GameMain.NetLobbyScreen.OnRoundEnded(); }
                TabMenu.OnRoundEnded();
                GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "ConversationAction" || ReadyCheck.IsReadyCheck(mb));
                ObjectiveManager.ResetUI();
                CharacterHUD.ClearBossProgressBars();
#endif
                AchievementManager.OnRoundEnded(this);

#if SERVER
                GameMain.Server?.TraitorManager?.EndRound();
#endif
                GameMode?.End(transitionType);
                EventManager?.EndRound();
                StatusEffect.StopAll();
                AfflictionPrefab.ClearAllEffects();
                IsRunning = false;

#if CLIENT
                bool success = CrewManager!.GetCharacters().Any(c => !c.IsDead);
#else
                bool success =
                    GameMain.Server != null &&
                    GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
#endif
                GameAnalyticsManager.AddProgressionEvent(
                    success ? GameAnalyticsManager.ProgressionStatus.Complete : GameAnalyticsManager.ProgressionStatus.Fail,
                    GameMode?.Preset.Identifier.Value ?? "none",
                    RoundDuration);
                string eventId = "EndRound:" + (GameMode?.Preset?.Identifier.Value ?? "none") + ":";
                LogEndRoundStats(eventId, traitorResults);
                if (GameMode is CampaignMode campaignMode)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + "MoneyEarned", GetAmountOfMoney(crewCharacters) - prevMoney);
                    campaignMode.TotalPlayTime += RoundDuration;
                }
#if CLIENT
                HintManager.OnRoundEnded();
#endif
                missions.Clear();
            }
            catch (Exception e)
            {
                string errorMsg = "Unknown error while ending the round.";
                DebugConsole.ThrowError(errorMsg, e);
                GameAnalyticsManager.AddErrorEventOnce("GameSession.EndRound:UnknownError", GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + e.StackTrace);
#if SERVER
                if (Timing.TotalTime > LastEndRoundErrorMessageTime + 1.0)
                {
                    GameMain.Server?.SendChatMessage(errorMsg + "\n" + e.StackTrace, Networking.ChatMessageType.Error);
                    LastEndRoundErrorMessageTime = Timing.TotalTime;
                }
#endif
            }
            finally
            {
                RoundEnding = false;
            }

            int GetAmountOfMoney(IEnumerable<Character> crew)
            {
                if (GameMode is not CampaignMode campaign) { return 0; }

                return GameMain.NetworkMember switch
                {
                    null => campaign.Bank.Balance,
                    _    => crew.Sum(c => c.Wallet.Balance) + campaign.Bank.Balance
                };
            }
        }

        public static PerkCollection GetPerks()
        {
            if (GameMain.NetworkMember?.ServerSettings is not { } serverSettings)
            {
                return PerkCollection.Empty;
            }

            var team1Builder = ImmutableArray.CreateBuilder<DisembarkPerkPrefab>();
            var team2Builder = ImmutableArray.CreateBuilder<DisembarkPerkPrefab>();

            foreach (Identifier coalitionPerk in serverSettings.SelectedCoalitionPerks)
            {
                if (!DisembarkPerkPrefab.Prefabs.TryGet(coalitionPerk, out DisembarkPerkPrefab? disembarkPerk)) { continue; }
                team1Builder.Add(disembarkPerk);
            }

            foreach (Identifier separatistsPerk in serverSettings.SelectedSeparatistsPerks)
            {
                if (!DisembarkPerkPrefab.Prefabs.TryGet(separatistsPerk, out DisembarkPerkPrefab? disembarkPerk)) { continue; }
                team2Builder.Add(disembarkPerk);
            }

            return new PerkCollection(team1Builder.ToImmutable(), team2Builder.ToImmutable());
        }

        public static bool ValidatedDisembarkPoints(GameModePreset preset, IEnumerable<Identifier> missionTypes)
        {
            if (GameMain.NetworkMember?.ServerSettings is not { } settings) { return false; }

            bool checkBothTeams = preset == GameModePreset.PvP;

            PerkCollection perks = GetPerks();

            int team1TotalCost = GetTotalCost(perks.Team1Perks);
            if (team1TotalCost > settings.DisembarkPointAllowance)
            {
                return false;
            }

            if (checkBothTeams)
            {
                int team2TotalCost = GetTotalCost(perks.Team2Perks);
                if (team2TotalCost > settings.DisembarkPointAllowance)
                {
                    return false;
                }
            }

            return true;

            int GetTotalCost(ImmutableArray<DisembarkPerkPrefab> perksToCheck)
            {
                if (preset == GameModePreset.Mission || preset == GameModePreset.PvP)
                {
                    if (ShouldIgnorePerksThatCanNotApplyWithoutSubmarine(preset, missionTypes))
                    {
                        perksToCheck = perksToCheck.Where(static p => p.PerkBehaviors.All(static b => b.CanApplyWithoutSubmarine())).ToImmutableArray();
                    }
                }
                return perksToCheck.Sum(static p => p.Cost);
            }
        }

        public static bool ShouldIgnorePerksThatCanNotApplyWithoutSubmarine(GameModePreset preset, IEnumerable<Identifier> missionTypes)
        {
            if (preset == GameModePreset.Mission || preset == GameModePreset.PvP)
            {
                var missionTypesToCheck = MissionMode.ValidateMissionTypes(missionTypes, preset == GameModePreset.PvP ? MissionPrefab.PvPMissionClasses : MissionPrefab.CoOpMissionClasses);
                foreach (var missionType in missionTypesToCheck)
                {
                    foreach (var missionPrefab in MissionPrefab.Prefabs.Where(mp => mp.Type == missionType))
                    {
                        if (missionPrefab.LoadSubmarines)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public void LogEndRoundStats(string eventId, TraitorManager.TraitorResults? traitorResults = null)
        {
            if (Submarine.MainSub?.Info?.IsVanillaSubmarine() ?? false)
            {
                //don't log modded subs, that's a ton of extra data to collect
                GameAnalyticsManager.AddDesignEvent(eventId + "Submarine:" + (Submarine.MainSub?.Info?.Name ?? "none"), RoundDuration);
            }
            GameAnalyticsManager.AddDesignEvent(eventId + "GameMode:" + (GameMode?.Name.Value ?? "none"), RoundDuration);
            GameAnalyticsManager.AddDesignEvent(eventId + "CrewSize:" + (CrewManager?.GetCharacterInfos()?.Count() ?? 0), RoundDuration);
            foreach (Mission mission in missions)
            {
                GameAnalyticsManager.AddDesignEvent(eventId + "MissionType:" + (mission.Prefab.Type.ToString() ?? "none") + ":" + mission.Prefab.Identifier + ":" + (mission.Completed ? "Completed" : "Failed"), RoundDuration);
            }
            if (!ContentPackageManager.ModsEnabled)
            {
                if (Level.Loaded != null)
                {
                    Identifier levelId = (Level.Loaded.Type == LevelData.LevelType.Outpost ?
                        Level.Loaded.StartOutpost?.Info?.OutpostGenerationParams?.Identifier :
                        Level.Loaded.GenerationParams?.Identifier) ?? "null".ToIdentifier();
                    GameAnalyticsManager.AddDesignEvent(eventId + "LevelType:" + (Level.Loaded?.Type.ToString() ?? "none" + ":" + levelId), RoundDuration);
                    GameAnalyticsManager.AddDesignEvent(eventId + "Biome:" + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none"), RoundDuration);
                }

                //disabled for now, we're collecting too many events and this is information we don't need atm
                /*if (Submarine.MainSub != null)
                {
                    Dictionary<ItemPrefab, int> submarineInventory = new Dictionary<ItemPrefab, int>();
                    foreach (Item item in Item.ItemList)
                    {
                        var rootContainer = item.RootContainer ?? item;
                        if (rootContainer.Submarine?.Info == null || rootContainer.Submarine.Info.Type != SubmarineType.Player) { continue; }
                        if (rootContainer.Submarine != Submarine.MainSub && !Submarine.MainSub.DockedTo.Contains(rootContainer.Submarine)) { continue; }

                        var holdable = item.GetComponent<Holdable>();
                        if (holdable == null || holdable.Attached) { continue; }
                        var wire = item.GetComponent<Wire>();
                        if (wire != null && wire.Connections.Any(c => c != null)) { continue; }

                        if (!submarineInventory.ContainsKey(item.Prefab))
                        {
                            submarineInventory.Add(item.Prefab, 0);
                        }
                        submarineInventory[item.Prefab]++;
                    }
                    foreach (var subItem in submarineInventory)
                    {
                        GameAnalyticsManager.AddDesignEvent(eventId + "SubmarineInventory:" + subItem.Key.Identifier, subItem.Value);
                    }
                }*/
            }

            if (traitorResults.HasValue)
            {
                GameAnalyticsManager.AddDesignEvent($"TraitorEvent:{traitorResults.Value.TraitorEventIdentifier}:{traitorResults.Value.ObjectiveSuccessful}");
                GameAnalyticsManager.AddDesignEvent($"TraitorEvent:{traitorResults.Value.TraitorEventIdentifier}:{(traitorResults.Value.VotedCorrectTraitor ? "TraitorIdentifier" : "TraitorUnidentified")}");
            }

            foreach (Character c in GetSessionCrewCharacters(CharacterType.Both))
            {
                foreach (var itemSelectedDuration in c.ItemSelectedDurations)
                {
                    string characterType = "Unknown";
                    if (c.IsBot)
                    {
                        characterType = "Bot";
                    }
                    else if (c.IsPlayer)
                    {
                        characterType = "Player";
                    }
                    GameAnalyticsManager.AddDesignEvent("TimeSpentOnDevices:" + (GameMode?.Preset?.Identifier.Value ?? "none") + ":" + characterType + ":" + (c.Info?.Job?.Prefab.Identifier.Value ?? "NoJob") + ":" + itemSelectedDuration.Key.Identifier, itemSelectedDuration.Value);
                }
            }
#if CLIENT
            if (GameMode is TutorialMode tutorialMode)
            {
                GameAnalyticsManager.AddDesignEvent(eventId + tutorialMode.Tutorial.Identifier);
                if (GameMain.IsFirstLaunch)
                {
                    GameAnalyticsManager.AddDesignEvent("FirstLaunch:" + eventId + tutorialMode.Tutorial.Identifier);
                }
            }
            GameAnalyticsManager.AddDesignEvent(eventId + "TimeSpentCleaning", TimeSpentCleaning);
            GameAnalyticsManager.AddDesignEvent(eventId + "TimeSpentPainting", TimeSpentPainting);
            TimeSpentCleaning = TimeSpentPainting = 0.0;
#endif
        }

        public void KillCharacter(Character character)
        {
            if (CrewManager != null && CrewManager.GetCharacters().Contains(character))
            {
                casualties.Add(character);
            }
#if CLIENT
            CrewManager?.KillCharacter(character);
#endif
        }

        public void ReviveCharacter(Character character)
        {
            casualties.Remove(character);
#if CLIENT
            CrewManager?.ReviveCharacter(character);
#endif
        }

        public static bool IsCompatibleWithEnabledContentPackages(IList<string> contentPackageNames, out LocalizedString errorMsg)
        {
            errorMsg = "";
            //no known content packages, must be an older save file
            if (!contentPackageNames.Any()) { return true; }

            List<string> missingPackages = new List<string>();
            foreach (string packageName in contentPackageNames)
            {
                if (!ContentPackageManager.EnabledPackages.All.Any(cp => cp.NameMatches(packageName)))
                {
                    missingPackages.Add(packageName);
                }
            }
            List<string> excessPackages = new List<string>();
            foreach (ContentPackage cp in ContentPackageManager.EnabledPackages.All)
            {
                if (!cp.HasMultiplayerSyncedContent) { continue; }
                if (!contentPackageNames.Any(p => cp.NameMatches(p)))
                {
                    excessPackages.Add(cp.Name);
                }
            }

            bool orderMismatch = false;
            if (missingPackages.Count == 0 && missingPackages.Count == 0)
            {
                var enabledPackages = ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerSyncedContent).ToImmutableArray();
                for (int i = 0; i < contentPackageNames.Count && i < enabledPackages.Length; i++)
                {
                    if (!enabledPackages[i].NameMatches(contentPackageNames[i]))
                    {
                        orderMismatch = true;
                        break;
                    }
                }
            }

            if (!orderMismatch && missingPackages.Count == 0 && excessPackages.Count == 0) { return true; }

            if (missingPackages.Count == 1)
            {
                errorMsg = TextManager.GetWithVariable("campaignmode.missingcontentpackage", "[missingcontentpackage]", missingPackages[0]);
            }
            else if (missingPackages.Count > 1)
            {
                errorMsg = TextManager.GetWithVariable("campaignmode.missingcontentpackages", "[missingcontentpackages]", string.Join(", ", missingPackages));
            }
            if (excessPackages.Count == 1)
            {
                if (!errorMsg.IsNullOrEmpty()) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.incompatiblecontentpackage", "[incompatiblecontentpackage]", excessPackages[0]);
            }
            else if (excessPackages.Count > 1)
            {
                if (!errorMsg.IsNullOrEmpty()) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.incompatiblecontentpackages", "[incompatiblecontentpackages]", string.Join(", ", excessPackages));
            }
            if (orderMismatch)
            {
                if (!errorMsg.IsNullOrEmpty()) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.contentpackageordermismatch", "[loadorder]", string.Join(", ", contentPackageNames));
            }

            return false;
        }

        public void Save(string filePath, bool isSavingOnLoading)
        {
            if (GameMode is not CampaignMode campaign)
            {
                throw new NotSupportedException("GameSessions can only be saved when playing in a campaign mode.");
            }

            XDocument doc = new XDocument(new XElement("Gamesession"));
            XElement rootElement = doc.Root ?? throw new NullReferenceException("Game session XML element is invalid: document is null.");

            rootElement.Add(new XAttribute("savetime", SerializableDateTime.UtcNow.ToUnixTime()));
            #warning TODO: after this gets on main, replace savetime with the commented line
            //rootElement.Add(new XAttribute("savetime", SerializableDateTime.LocalNow));

            rootElement.Add(new XAttribute("currentlocation", Map?.CurrentLocation?.NameIdentifier.Value ?? string.Empty));
            rootElement.Add(new XAttribute("currentlocationnameformatindex", Map?.CurrentLocation?.NameFormatIndex ?? -1));
            rootElement.Add(new XAttribute("locationtype", Map?.CurrentLocation?.Type?.Identifier ?? Identifier.Empty));

            rootElement.Add(new XAttribute("nextleveltype", campaign.NextLevel?.Type ?? LevelData?.Type ?? LevelData.LevelType.Outpost));

            LastSaveVersion = GameMain.Version;
            rootElement.Add(new XAttribute("version", GameMain.Version));
            if (Submarine?.Info != null && !Submarine.Removed && Campaign != null)
            {
                bool hasNewPendingSub = Campaign.PendingSubmarineSwitch != null &&
                    Campaign.PendingSubmarineSwitch.MD5Hash.StringRepresentation != Submarine.Info.MD5Hash.StringRepresentation;
                if (hasNewPendingSub)
                {
                    Campaign.SwitchSubs();
                }
            }
            rootElement.Add(new XAttribute("submarine", SubmarineInfo == null ? "" : SubmarineInfo.Name));
            if (OwnedSubmarines != null)
            {
                List<string> ownedSubmarineNames = new List<string>();
                var ownedSubsElement = new XElement("ownedsubmarines");
                rootElement.Add(ownedSubsElement);
                foreach (var ownedSub in OwnedSubmarines)
                {
                    ownedSubsElement.Add(new XElement("sub", new XAttribute("name", ownedSub.Name)));
                }
            }
            if (Map != null) { rootElement.Add(new XAttribute("mapseed", Map.Seed)); }
            rootElement.Add(new XAttribute("selectedcontentpackagenames",
                string.Join("|", ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerSyncedContent).Select(cp => cp.Name.Replace("|", @"\|")))));
            
            XElement permadeathsElement = new XElement("permadeaths");
            foreach (var kvp in permadeathsPerAccount)
            {
                if (kvp.Key.TryUnwrap(out AccountId? accountId))
                {
                    permadeathsElement.Add(
                        new XElement("account"),
                            new XAttribute("id", accountId.StringRepresentation),
                            new XAttribute("permadeathcount", kvp.Value));
                }
            }
            rootElement.Add(permadeathsElement);
            
            ((CampaignMode)GameMode).Save(doc.Root, isSavingOnLoading);

            doc.SaveSafe(filePath, throwExceptions: true);
        }

        /*public void Load(XElement saveElement)
        {
            foreach (var subElement in saveElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
#if CLIENT
                    case "gamemode": //legacy support
                    case "singleplayercampaign":
                        GameMode = SinglePlayerCampaign.Load(subElement);
                        break;
#endif
                    case "multiplayercampaign":
                        if (!(GameMode is MultiPlayerCampaign mpCampaign))
                        {
                            DebugConsole.ThrowError("Error while loading a save file: the save file is for a multiplayer campaign but the current gamemode is " + GameMode.GetType().ToString());
                            break;
                        }

                        mpCampaign.Load(subElement);
                        break;
                }
            }
        }*/

    }
}
