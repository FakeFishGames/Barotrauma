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

namespace Barotrauma
{
    partial class GameSession
    {
        public enum InfoFrameTab { Crew, Mission, MyCharacter, Traitor };

        public readonly EventManager EventManager;

        public GameMode? GameMode;

        //two locations used as the start and end in the MP mode
        private Location[]? dummyLocations;
        public CrewManager? CrewManager;

        public double RoundStartTime;

        public double TimeSpentCleaning, TimeSpentPainting;

        private readonly List<Mission> missions = new List<Mission>();
        public IEnumerable<Mission> Missions { get { return missions; } }

        private readonly HashSet<Character> casualties = new HashSet<Character>();
        public IEnumerable<Character> Casualties { get { return casualties; } }


        public CharacterTeamType? WinningTeam;

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
                if (dummyLocations == null) { CreateDummyLocations(); }
                if (dummyLocations == null) { throw new NullReferenceException("dummyLocations is null somehow!"); }
                return dummyLocations[0];
            }
        }

        public Location EndLocation
        {
            get
            {
                if (Map != null) { return Map.SelectedLocation; }
                if (dummyLocations == null) { CreateDummyLocations(); }
                if (dummyLocations == null) { throw new NullReferenceException("dummyLocations is null somehow!"); }
                return dummyLocations[1];
            }
        }

        public SubmarineInfo SubmarineInfo { get; set; }
        
        public List<SubmarineInfo> OwnedSubmarines = new List<SubmarineInfo>();

        public Submarine? Submarine { get; set; }

        public string? SavePath { get; set; }

        partial void InitProjSpecific();

        private GameSession(SubmarineInfo submarineInfo)
        {
            InitProjSpecific();
            SubmarineInfo = submarineInfo;
            GameMain.GameSession = this;
            EventManager = new EventManager();
        }

        /// <summary>
        /// Start a new GameSession. Will be saved to the specified save path (if playing a game mode that can be saved).
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, string savePath, GameModePreset gameModePreset, CampaignSettings settings, string? seed = null, MissionType missionType = MissionType.None)
            : this(submarineInfo)
        {
            this.SavePath = savePath;
            CrewManager = new CrewManager(gameModePreset.IsSinglePlayer);
            GameMode = InstantiateGameMode(gameModePreset, seed, submarineInfo, settings, missionType: missionType);
            InitOwnedSubs(submarineInfo);
        }

        /// <summary>
        /// Start a new GameSession with a specific pre-selected mission.
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, GameModePreset gameModePreset, string? seed = null, IEnumerable<MissionPrefab>? missionPrefabs = null)
            : this(submarineInfo)
        {
            CrewManager = new CrewManager(gameModePreset.IsSinglePlayer);
            GameMode = InstantiateGameMode(gameModePreset, seed, submarineInfo, CampaignSettings.Empty, missionPrefabs: missionPrefabs);
            InitOwnedSubs(submarineInfo);
        }

        /// <summary>
        /// Load a game session from the specified XML document. The session will be saved to the specified path.
        /// </summary>
        public GameSession(SubmarineInfo submarineInfo, List<SubmarineInfo> ownedSubmarines, XDocument doc, string saveFile) : this(submarineInfo)
        {
            this.SavePath = saveFile;
            GameMain.GameSession = this;
            XElement rootElement = doc.Root ?? throw new NullReferenceException("Game session XML element is invalid: document is null.");
            //selectedSub.Name = doc.Root.GetAttributeString("submarine", selectedSub.Name);

            foreach (var subElement in rootElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
#if CLIENT
                    case "gamemode": //legacy support
                    case "singleplayercampaign":
                        CrewManager = new CrewManager(true);
                        var campaign = SinglePlayerCampaign.Load(subElement);
                        campaign.LoadNewLevel();
                        GameMode = campaign;
                        InitOwnedSubs(submarineInfo, ownedSubmarines);
                        break;
#endif
                    case "multiplayercampaign":
                        CrewManager = new CrewManager(false);
                        var mpCampaign = MultiPlayerCampaign.LoadNew(subElement);
                        GameMode = mpCampaign;
                        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                        {
                            mpCampaign.LoadNewLevel();
                            InitOwnedSubs(submarineInfo, ownedSubmarines);
                            //save to ensure the campaign ID in the save file matches the one that got assigned to this campaign instance
                            SaveUtil.SaveGame(saveFile);
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

        private GameMode InstantiateGameMode(GameModePreset gameModePreset, string? seed, SubmarineInfo selectedSub, CampaignSettings settings, IEnumerable<MissionPrefab>? missionPrefabs = null, MissionType missionType = MissionType.None)
        {
            if (gameModePreset.GameModeType == typeof(CoOpMode) || gameModePreset.GameModeType == typeof(PvPMode))
            {
                //don't allow hidden mission types (e.g. GoTo) in single mission modes
                var missionTypes = (MissionType[])Enum.GetValues(typeof(MissionType));
                for (int i = 0; i < missionTypes.Length; i++)
                {
                    if (MissionPrefab.HiddenMissionClasses.Contains(missionTypes[i]))
                    {
                        missionType &= ~missionTypes[i];
                    }
                }
            }
            if (gameModePreset.GameModeType == typeof(CoOpMode))
            {
                return missionPrefabs != null ?
                    new CoOpMode(gameModePreset, missionPrefabs) :
                    new CoOpMode(gameModePreset, missionType, seed ?? ToolBox.RandomSeed(8));
            }
            else if (gameModePreset.GameModeType == typeof(PvPMode))
            {
                return missionPrefabs != null ?
                    new PvPMode(gameModePreset, missionPrefabs) :
                    new PvPMode(gameModePreset, missionType, seed ?? ToolBox.RandomSeed(8));
            }
            else if (gameModePreset.GameModeType == typeof(MultiPlayerCampaign))
            {
                var campaign = MultiPlayerCampaign.StartNew(seed ?? ToolBox.RandomSeed(8), selectedSub, settings);
                if (selectedSub != null)
                {
                    campaign.Bank.TryDeduct(selectedSub.Price);
                    campaign.Bank.Balance = Math.Max(campaign.Bank.Balance, MultiPlayerCampaign.MinimumInitialMoney);
                }
                return campaign;
            }
#if CLIENT
            else if (gameModePreset.GameModeType == typeof(SinglePlayerCampaign))
            {
                var campaign = SinglePlayerCampaign.StartNew(seed ?? ToolBox.RandomSeed(8), selectedSub, settings);
                if (selectedSub != null)
                {
                    campaign.Bank.TryDeduct(selectedSub.Price);
                    campaign.Bank.Balance = Math.Max(campaign.Bank.Balance, MultiPlayerCampaign.MinimumInitialMoney);
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

        private void CreateDummyLocations(LocationType? forceLocationType = null)
        {
            dummyLocations = new Location[2];

            string seed = "";
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null)
            {
                seed = GameMain.GameSession.Level.Seed;
            }
            else if (GameMain.NetLobbyScreen != null)
            {
                seed = GameMain.NetLobbyScreen.LevelSeed;
            }

            MTRandom rand = new MTRandom(ToolBox.StringToInt(seed));
            for (int i = 0; i < 2; i++)
            {
                dummyLocations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f), null, rand, requireOutpost: true, forceLocationType: forceLocationType);
            }
        }

        public void LoadPreviousSave()
        {
            Submarine.Unload();
            SaveUtil.LoadGame(SavePath);
        }

        /// <summary>
        /// Switch to another submarine. The sub is loaded when the next round starts.
        /// </summary>
        public SubmarineInfo SwitchSubmarine(SubmarineInfo newSubmarine, int cost, Client? client = null)
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

            if ((GameMain.NetworkMember is null || GameMain.NetworkMember is { IsServer: true }) && cost > 0)
            {
                Campaign!.GetWallet(client).TryDeduct(cost);
            }
            GameAnalyticsManager.AddMoneySpentEvent(cost, GameAnalyticsManager.MoneySink.SubmarineSwitch, newSubmarine.Name);
            Campaign!.PendingSubmarineSwitch = newSubmarine;
            
            return newSubmarine;
        }

        public void PurchaseSubmarine(SubmarineInfo newSubmarine, Client? client = null)
        {
            if (Campaign is null) { return; }
            if ((GameMain.NetworkMember is null || GameMain.NetworkMember is { IsServer: true }) && !Campaign.GetWallet(client).TryDeduct(newSubmarine.Price)) { return; }
            if (!OwnedSubmarines.Any(s => s.Name == newSubmarine.Name))
            {
                GameAnalyticsManager.AddMoneySpentEvent(newSubmarine.Price, GameAnalyticsManager.MoneySink.SubmarinePurchase, newSubmarine.Name);
                OwnedSubmarines.Add(newSubmarine);
            }
        }

        public bool IsSubmarineOwned(SubmarineInfo query)
        {
            return 
                Submarine.MainSub.Info.Name == query.Name || 
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

        public void StartRound(string levelSeed, float? difficulty = null, LevelGenerationParams? levelGenerationParams = null)
        {
            if (GameMode == null) { return; }
            LevelData? randomLevel = null;
            foreach (Mission mission in Missions.Union(GameMode.Missions))
            {
                MissionPrefab missionPrefab = mission.Prefab;
                if (missionPrefab != null &&
                    missionPrefab.AllowedLocationTypes.Any() &&
                    !missionPrefab.AllowedConnectionTypes.Any())
                {
                    LocationType? locationType = LocationType.Prefabs.FirstOrDefault(lt => missionPrefab.AllowedLocationTypes.Any(m => m == lt.Identifier));
                    CreateDummyLocations(locationType);
                    randomLevel = LevelData.CreateRandom(levelSeed, difficulty, levelGenerationParams, requireOutpost: true);
                    break;
                }
            }
            randomLevel ??= LevelData.CreateRandom(levelSeed, difficulty, levelGenerationParams);
            StartRound(randomLevel);
        }

        public void StartRound(LevelData? levelData, bool mirrorLevel = false, SubmarineInfo? startOutpost = null, SubmarineInfo? endOutpost = null)
        {
            AfflictionPrefab.LoadAllEffects();

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
            Submarine = Submarine.MainSub = new Submarine(SubmarineInfo);
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

            foreach (Mission mission in GameMode!.Missions)
            {
                // setting level for missions that may involve difficulty-related submarine creation
                mission.SetLevel(levelData);
            }

            if (Submarine.MainSubs[1] == null)
            {
                var enemySubmarineInfo = GameMode is PvPMode ? SubmarineInfo : GameMode.Missions.FirstOrDefault(m => m.EnemySubmarineInfo != null)?.EnemySubmarineInfo;
                if (enemySubmarineInfo != null)
                {
                    Submarine.MainSubs[1] = new Submarine(enemySubmarineInfo, true);
                }
            }

            if (GameMain.NetworkMember?.ServerSettings?.LockAllDefaultWires ?? false)
            {
                List<Item> items = new List<Item>();
                items.AddRange(Submarine.MainSubs[0].GetItems(alsoFromConnectedSubs: true));
                if (Submarine.MainSubs[1] != null)
                {
                    items.AddRange(Submarine.MainSubs[1].GetItems(alsoFromConnectedSubs: true));
                }
                foreach (Item item in items)
                {
                    Wire wire = item.GetComponent<Wire>();
                    if (wire != null && !wire.NoAutoLock && wire.Connections.Any(c => c != null)) { wire.Locked = true; }                    
                }
            }

            Level? level = null;
            if (levelData != null)
            {
                level = Level.Generate(levelData, mirrorLevel, startOutpost, endOutpost);
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
            GameAnalyticsManager.AddDesignEvent(eventId + "CrewSize:" + (CrewManager?.CharacterInfos?.Count() ?? 0));
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
            if (GameMode is CampaignMode campaignMode) 
            { 
                if (campaignMode.Map?.Radiation != null && campaignMode.Map.Radiation.Enabled)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + "Radiation:Enabled");
                }
                else
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + "Radiation:Disabled");
                }
                bool firstTimeInBiome = Map != null && !Map.Connections.Any(c => c.Passed && c.Biome == LevelData!.Biome);
                if (firstTimeInBiome)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none") + "Discovered:Playtime", campaignMode.TotalPlayTime);
                    GameAnalyticsManager.AddDesignEvent(eventId + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none") + "Discovered:PassedLevels", campaignMode.TotalPassedLevels);
                }
            }

#if CLIENT
            if (GameMode is CampaignMode && levelData != null) { SteamAchievementManager.OnBiomeDiscovered(levelData.Biome); }

            var existingRoundSummary = GUIMessageBox.MessageBoxes.Find(mb => mb.UserData is RoundSummary)?.UserData as RoundSummary;
            if (existingRoundSummary?.ContinueButton != null)
            {
                existingRoundSummary.ContinueButton.Visible = true;
            }

            RoundSummary = new RoundSummary(GameMode, Missions, StartLocation, EndLocation);

            if (!(GameMode is TutorialMode) && !(GameMode is TestGameMode))
            {
                GUI.AddMessage("", Color.Transparent, 3.0f, playSound: false);
                if (EndLocation != null && levelData != null)
                {
                    GUI.AddMessage(levelData.Biome.DisplayName, Color.Lerp(Color.CadetBlue, Color.DarkRed, levelData.Difficulty / 100.0f), 5.0f, playSound: false);
                    GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Destination"), EndLocation.Name), Color.CadetBlue, playSound: false);
                    if (missions.Count > 1)
                    {
                        string joinedMissionNames = string.Join(", ", missions.Select(m => m.Name));
                        GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Mission"), joinedMissionNames), Color.CadetBlue, playSound: false);
                    }
                    else
                    {
                        var mission = missions.FirstOrDefault();
                        GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Mission"), mission?.Name ?? TextManager.Get("None")), Color.CadetBlue, playSound: false);
                    }
                }
                else
                {
                    GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Location"), StartLocation.Name), Color.CadetBlue, playSound: false);
                }
            }

            ReadyCheck.ReadyCheckCooldown = DateTime.MinValue;

            GUI.PreventPauseMenuToggle = false;

            HintManager.OnRoundStarted();
#endif
        }

        private void InitializeLevel(Level? level)
        {
            //make sure no status effects have been carried on from the next round
            //(they should be stopped in EndRound, this is a safeguard against cases where the round is ended ungracefully)
            StatusEffect.StopAll();

#if CLIENT
#if !DEBUG
            GameMain.LightManager.LosEnabled = GameMain.Client == null || GameMain.Client.CharacterInfo != null;
#endif
            if (GameMain.LightManager.LosEnabled) { GameMain.LightManager.LosAlpha = 1f; }
            if (GameMain.Client == null) { GameMain.LightManager.LosMode = GameSettings.CurrentConfig.Graphics.LosMode; }
#endif
            LevelData = level?.LevelData;
            Level = level;

            PlaceSubAtStart(Level);

            foreach (var sub in Submarine.Loaded)
            {
                if (sub.Info.IsOutpost)
                {
                    sub.DisableObstructedWayPoints();
                }
            }

            Entity.Spawner = new EntitySpawner();

            if (GameMode != null && Submarine != null)
            {
                missions.Clear();
                GameMode.AddExtraMissions(LevelData);
                missions.AddRange(GameMode.Missions);
                GameMode.Start();
                foreach (Mission mission in missions)
                {
                    int prevEntityCount = Entity.GetEntities().Count();
                    mission.Start(Level.Loaded);
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Entity.GetEntities().Count() != prevEntityCount)
                    {
                        DebugConsole.ThrowError(
                            $"Entity count has changed after starting a mission ({mission.Prefab.Identifier}) as a client. " +
                            "The clients should not instantiate entities themselves when starting the mission," +
                            " but instead the server should inform the client of the spawned entities using Mission.ServerWriteInitial.");
                    }
                }

                EventManager?.StartRound(Level.Loaded);
                SteamAchievementManager.OnStartRound();

                GameMode.ShowStartMessage();

                if (GameMain.NetworkMember == null) 
                {
                    //only place items and corpses here in single player
                    //the server does this after loading the respawn shuttle
                    Level?.SpawnNPCs();
                    Level?.SpawnCorpses();
                    Level?.PrepareBeaconStation();
                    AutoItemPlacer.PlaceIfNeeded();
                }
                if (GameMode is MultiPlayerCampaign mpCampaign)
                {
                    mpCampaign.UpgradeManager.ApplyUpgrades();
                    mpCampaign.UpgradeManager.SanityCheckUpgrades(Submarine);
                }
            }

            CreatureMetrics.Instance.RecentlyEncountered.Clear();

            GameMain.GameScreen.Cam.Position = Character.Controlled?.WorldPosition ?? Submarine.MainSub.WorldPosition;
            RoundStartTime = Timing.TotalTime;
            GameMain.ResetFrameTime();
            IsRunning = true;
        }

        public void PlaceSubAtStart(Level? level)
        {
            if (level == null || Submarine == null)
            {
                Submarine?.SetPosition(Vector2.Zero);
                return;
            }

            var originalSubPos = Submarine.WorldPosition;

            if (level.StartOutpost != null)
            {
                //start by placing the sub below the outpost
                Rectangle outpostBorders = Level.Loaded.StartOutpost.GetDockedBorders();
                Rectangle subBorders = Submarine.GetDockedBorders();

                Submarine.SetPosition(
                    Level.Loaded.StartOutpost.WorldPosition -
                    new Vector2(0.0f, outpostBorders.Height / 2 + subBorders.Height / 2));

                //find the port that's the nearest to the outpost and dock if one is found
                float closestDistance = 0.0f;
                DockingPort? myPort = null, outPostPort = null;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (port.IsHorizontal || port.Docked) { continue; }
                    if (port.Item.Submarine == level.StartOutpost)
                    {
                        if (port.DockingTarget == null)
                        {
                            outPostPort = port;
                        }
                        continue;
                    }
                    if (port.Item.Submarine != Submarine) { continue; }

                    //the submarine port has to be at the top of the sub
                    if (port.Item.WorldPosition.Y < Submarine.WorldPosition.Y) { continue; }

                    float dist = Vector2.DistanceSquared(port.Item.WorldPosition, level.StartOutpost.WorldPosition);
                    if ((myPort == null || dist < closestDistance || port.MainDockingPort) && !(myPort?.MainDockingPort ?? false))
                    {
                        myPort = port;
                        closestDistance = dist;
                    }
                }

                if (myPort != null && outPostPort != null)
                {
                    Vector2 portDiff = myPort.Item.WorldPosition - Submarine.WorldPosition;
                    Vector2 spawnPos = (outPostPort.Item.WorldPosition - portDiff) - Vector2.UnitY * outPostPort.DockedDistance;

                    bool startDocked = level.Type == LevelData.LevelType.Outpost;
#if CLIENT
                    startDocked |= GameMode is TutorialMode;
#endif
                    if (startDocked)
                    {
                        Submarine.SetPosition(spawnPos);
                        myPort.Dock(outPostPort);
                        myPort.Lock(isNetworkMessage: true, applyEffects: false);
                    }
                    else
                    {
                        Submarine.SetPosition(spawnPos - Vector2.UnitY * 100.0f);
                        Submarine.NeutralizeBallast(); 
                        Submarine.EnableMaintainPosition();
                    }
                }
                else
                {
                    Submarine.NeutralizeBallast();
                    Submarine.EnableMaintainPosition();
                }
            }
            else
            {
                Submarine.SetPosition(Submarine.FindSpawnPos(level.StartPosition));
                Submarine.NeutralizeBallast();
                Submarine.EnableMaintainPosition();
            }

            // Make sure that linked subs which are NOT docked to the main sub
            // (but still close enough to NOT be considered as 'left behind')
            // are also moved to keep their relative position to the main sub
            var linkedSubs = MapEntity.mapEntityList.FindAll(me => me is LinkedSubmarine);
            foreach (LinkedSubmarine ls in linkedSubs)
            {
                if (ls.Sub == null || ls.Submarine != Submarine) { continue; }
                if (!ls.LoadSub || ls.Sub.DockedTo.Contains(Submarine)) { continue; }
                if (Submarine.Info.LeftBehindDockingPortIDs.Contains(ls.OriginalLinkedToID)) { continue; }
                if (ls.Sub.Info.SubmarineElement.Attribute("location") != null) { continue; }
                ls.Sub.SetPosition(ls.Sub.WorldPosition + (Submarine.WorldPosition - originalSubPos));
            }
        }

        public void Update(float deltaTime)
        {
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
            if (!(GameMain.GameSession.CrewManager is { } crewManager)) { return ImmutableHashSet<Character>.Empty; }

            IEnumerable<Character> players;
            IEnumerable<Character> bots;
            HashSet<Character> characters = new HashSet<Character>();

#if SERVER
            players = GameMain.Server.ConnectedClients.Select(c => c.Character).Where(c => c?.Info != null && !c.IsDead);
            bots = crewManager.GetCharacters().Where(c => !c.IsRemotePlayer);
#elif CLIENT
            players = crewManager.GetCharacters().Where(c => c.IsPlayer);
            bots = crewManager.GetCharacters().Where(c => c.IsBot);
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

        public void EndRound(string endMessage, List<TraitorMissionResult>? traitorResults = null, CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            RoundEnding = true;

            //Clear the grids to allow for garbage collection
            Powered.Grids.Clear();

            try
            {
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
                GUI.PreventPauseMenuToggle = true;

                if (!(GameMode is TestGameMode) && Screen.Selected == GameMain.GameScreen && RoundSummary != null)
                {
                    GUI.ClearMessages();
                    GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData is RoundSummary);
                    GUIFrame summaryFrame = RoundSummary.CreateSummaryFrame(this, endMessage, traitorResults, transitionType);
                    GUIMessageBox.MessageBoxes.Add(summaryFrame);
                    RoundSummary.ContinueButton.OnClicked = (_, __) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; };
                }

                if (GameMain.NetLobbyScreen != null) { GameMain.NetLobbyScreen.OnRoundEnded(); }
                TabMenu.OnRoundEnded();
                GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "ConversationAction" || ReadyCheck.IsReadyCheck(mb));
#endif
                SteamAchievementManager.OnRoundEnded(this);

                GameMode?.End(transitionType);
                EventManager?.EndRound();
                StatusEffect.StopAll();
                AfflictionPrefab.ClearAllEffects();
                IsRunning = false;

#if CLIENT
                bool success = CrewManager!.GetCharacters().Any(c => !c.IsDead);
#else
                bool success = GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
#endif
                double roundDuration = Timing.TotalTime - RoundStartTime;
                GameAnalyticsManager.AddProgressionEvent(
                    success ? GameAnalyticsManager.ProgressionStatus.Complete : GameAnalyticsManager.ProgressionStatus.Fail,
                    GameMode?.Name?.Value ?? "none",
                    roundDuration);
                string eventId = "EndRound:" + (GameMode?.Preset?.Identifier.Value ?? "none") + ":";
                LogEndRoundStats(eventId);
                if (GameMode is CampaignMode campaignMode)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + "MoneyEarned", GetAmountOfMoney(crewCharacters) - prevMoney);
                    campaignMode.TotalPlayTime += roundDuration;
                }
#if CLIENT
                HintManager.OnRoundEnded();
#endif
                missions.Clear();
            }
            finally
            {
                RoundEnding = false;
            }

            int GetAmountOfMoney(IEnumerable<Character> crew)
            {
                if (!(GameMode is CampaignMode campaign)) { return 0; }

                return GameMain.NetworkMember switch
                {
                    null => campaign.Bank.Balance,
                    _ => crew.Sum(c => c.Wallet.Balance) + campaign.Bank.Balance
                };
            }
        }

        public void LogEndRoundStats(string eventId)
        {
            double roundDuration = Timing.TotalTime - RoundStartTime;
            GameAnalyticsManager.AddDesignEvent(eventId + "Submarine:" + (Submarine.MainSub?.Info?.Name ?? "none"), roundDuration);
            GameAnalyticsManager.AddDesignEvent(eventId + "GameMode:" + (GameMode?.Name.Value ?? "none"), roundDuration);
            GameAnalyticsManager.AddDesignEvent(eventId + "CrewSize:" + (CrewManager?.CharacterInfos?.Count() ?? 0), roundDuration);
            foreach (Mission mission in missions)
            {
                GameAnalyticsManager.AddDesignEvent(eventId + "MissionType:" + (mission.Prefab.Type.ToString() ?? "none") + ":" + mission.Prefab.Identifier + ":" + (mission.Completed ? "Completed" : "Failed"), roundDuration);
            }
            if (Level.Loaded != null)
            {
                Identifier levelId = (Level.Loaded.Type == LevelData.LevelType.Outpost ?
                    Level.Loaded.StartOutpost?.Info?.OutpostGenerationParams?.Identifier :
                    Level.Loaded.GenerationParams?.Identifier) ?? "null".ToIdentifier();
                GameAnalyticsManager.AddDesignEvent(eventId + "LevelType:" + (Level.Loaded?.Type.ToString() ?? "none" + ":" + levelId), roundDuration);
                GameAnalyticsManager.AddDesignEvent(eventId + "Biome:" + (Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none"), roundDuration);
            }

            if (Submarine.MainSub != null)
            {
                Dictionary<ItemPrefab, int> submarineInventory = new Dictionary<ItemPrefab, int>();
                foreach (Item item in Item.ItemList)
                {
                    var rootContainer = item.GetRootContainer() ?? item;
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

        public void Save(string filePath)
        {
            if (!(GameMode is CampaignMode))
            {
                throw new NotSupportedException("GameSessions can only be saved when playing in a campaign mode.");
            }

            XDocument doc = new XDocument(new XElement("Gamesession"));
            XElement rootElement = doc.Root ?? throw new NullReferenceException("Game session XML element is invalid: document is null.");

            rootElement.Add(new XAttribute("savetime", ToolBox.Epoch.NowLocal));
            rootElement.Add(new XAttribute("version", GameMain.Version));
            var submarineInfo = Campaign?.PendingSubmarineSwitch ?? SubmarineInfo;
            rootElement.Add(new XAttribute("submarine", submarineInfo == null ? "" : submarineInfo.Name));
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

            ((CampaignMode)GameMode).Save(doc.Root);

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
