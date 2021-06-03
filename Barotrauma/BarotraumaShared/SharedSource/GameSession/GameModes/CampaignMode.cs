using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;

namespace Barotrauma
{
    internal struct CampaignSettings
    {
        public static CampaignSettings Empty = new CampaignSettings();

        // Anything that uses this field I wasn't sure if actually needed the proper campaign settings to be passed down
        public static CampaignSettings Unsure = Empty;
        public bool RadiationEnabled { get; set; }

        public CampaignSettings(IReadMessage inc)
        {
            RadiationEnabled = inc.ReadBoolean();
        }
        
        public CampaignSettings(XElement element)
        {
            RadiationEnabled = element.GetAttributeBool(nameof(RadiationEnabled).ToLower(), true);
        }

        public void Serialize(IWriteMessage msg)
        {
            msg.Write(RadiationEnabled);
        }

        public XElement Save()
        {
            return new XElement(nameof(CampaignSettings), new XAttribute(nameof(RadiationEnabled).ToLower(), RadiationEnabled));
        }
    }

    abstract partial class CampaignMode : GameMode
    {
        const int MaxMoney = int.MaxValue / 2; //about 1 billion
        public const int InitialMoney = 8500;

        //duration of the cinematic + credits at the end of the campaign
        protected const float EndCinematicDuration = 240.0f;
        //duration of the camera transition at the end of a round
        protected const float EndTransitionDuration = 5.0f;
        //there can be no events before this time has passed during the 1st campaign round
        const float FirstRoundEventDelay = 0.0f;

        public enum InteractionType { None, Talk, Examine, Map, Crew, Store, Repair, Upgrade, PurchaseSub }

        public readonly CargoManager CargoManager;
        public UpgradeManager UpgradeManager;

        public List<Faction> Factions;

        public CampaignMetadata CampaignMetadata;

        protected XElement petsElement;

        public CampaignSettings Settings;

        private List<Mission> extraMissions = new List<Mission>();

        public enum TransitionType
        {
            None,
            //leaving a location level
            LeaveLocation,
            //progressing to next location level
            ProgressToNextLocation,
            //returning to previous location level
            ReturnToPreviousLocation,
            //returning to previous location (one with no level/outpost, the player is taken to the map screen and must choose their next destination)
            ReturnToPreviousEmptyLocation,
            //progressing to an empty location (one with no level/outpost, the player is taken to the map screen and must choose their next destination)
            ProgressToNextEmptyLocation,
            //end of campaign (reached end location)
            End
        }

        public bool IsFirstRound { get; protected set; } = true;

        public bool DisableEvents
        {
            get { return IsFirstRound && Timing.TotalTime < GameMain.GameSession.RoundStartTime + FirstRoundEventDelay; }
        }

        public bool CheatsEnabled;

        public const int HullRepairCost = 500, ItemRepairCost = 500, ShuttleReplaceCost = 1000;

        protected bool wasDocked;

        //key = dialog flag, double = Timing.TotalTime when the line was last said
        private readonly Dictionary<string, double> dialogLastSpoken = new Dictionary<string, double>();

        public bool PurchasedHullRepairs, PurchasedLostShuttles, PurchasedItemRepairs;

        public SubmarineInfo PendingSubmarineSwitch;

        protected Map map;
        public Map Map
        {
            get { return map; }
        }

        public override IEnumerable<Mission> Missions
        {
            get
            {
                if (Map.CurrentLocation != null)
                {
                    foreach (Mission mission in map.CurrentLocation.SelectedMissions)
                    {
                        if (mission.Locations[0] == mission.Locations[1] ||
                            mission.Locations.Contains(Map.SelectedLocation))
                        {
                            yield return mission;
                        }
                    }
                }
                foreach (Mission mission in extraMissions)
                {
                    yield return mission;
                }
            }
        }

        private int money;
        public int Money
        {
            get { return money; }
            set { money = MathHelper.Clamp(value, 0, MaxMoney); }
        }

        public LevelData NextLevel
        {
            get;
            protected set;
        }

        protected CampaignMode(GameModePreset preset)
            : base(preset)
        {
            Money = InitialMoney;
            CargoManager = new CargoManager(this);
        }

        /// <summary>
        /// The location that's displayed as the "current one" in the map screen. Normally the current outpost or the location at the start of the level,
        /// but when selecting the next destination at the end of the level at an uninhabited location we use the location at the end
        /// </summary>
        public Location GetCurrentDisplayLocation()
        {
            if (Level.Loaded?.EndLocation != null && !Level.Loaded.Generating &&
                Level.Loaded.Type == LevelData.LevelType.LocationConnection &&
                GetAvailableTransition(out _, out _) == TransitionType.ProgressToNextEmptyLocation)
            {
                return Level.Loaded.EndLocation;
            }
            return Level.Loaded?.StartLocation ?? Map.CurrentLocation;            
        }

        public List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(sub =>
                sub != leavingSub &&
                !leavingSub.DockedTo.Contains(sub) &&
                sub.Info.Type == SubmarineType.Player && sub.TeamID == CharacterTeamType.Team1 && // pirate subs are currently tagged as player subs as well
                sub != GameMain.NetworkMember?.RespawnManager?.RespawnShuttle &&
                (sub.AtEndExit != leavingSub.AtEndExit || sub.AtStartExit != leavingSub.AtStartExit));
        }

        public override void Start()
        {
            base.Start();
            dialogLastSpoken.Clear();
            characterOutOfBoundsTimer.Clear();
#if CLIENT
            prevCampaignUIAutoOpenType = TransitionType.None;
#endif
            if (PurchasedHullRepairs)
            {
                foreach (Structure wall in Structure.WallList)
                {
                    if (wall.Submarine == null || wall.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (wall.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(wall.Submarine))
                    {
                        for (int i = 0; i < wall.SectionCount; i++)
                        {
                            wall.SetDamage(i, 0, createNetworkEvent: false);
                        }
                    }
                }
                PurchasedHullRepairs = false;
            }
            if (PurchasedItemRepairs)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (item.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(item.Submarine))
                    {
                        if (item.GetComponent<Items.Components.Repairable>() != null)
                        {
                            item.Condition = item.MaxCondition;
                        }
                    }
                }
                PurchasedItemRepairs = false;
            }
            PurchasedLostShuttles = false;
            var connectedSubs = Submarine.MainSub.GetConnectedSubs();
            wasDocked = Level.Loaded.StartOutpost != null && connectedSubs.Contains(Level.Loaded.StartOutpost);
        }

        public void InitCampaignData()
        {
            Factions = new List<Faction>();
            foreach (FactionPrefab factionPrefab in FactionPrefab.Prefabs)
            {
                Factions.Add(new Faction(CampaignMetadata, factionPrefab));
            }
        }

        /// <summary>
        /// Automatically cleared after triggering -> no need to unregister
        /// </summary>
        public event Action BeforeLevelLoading;


        public override void AddExtraMissions(LevelData levelData)
        {
            extraMissions.Clear();

            var currentLocation = Map.CurrentLocation;
            if (levelData.Type == LevelData.LevelType.Outpost)
            {
                //if there's an available mission that takes place in the outpost, select it
                foreach (var availableMission in currentLocation.AvailableMissions)
                {
                    if (availableMission.Locations[0] == currentLocation && availableMission.Locations[1] == currentLocation)
                    {
                        currentLocation.SelectMission(availableMission);
                    }
                }
            }
            else
            {
                foreach (Mission mission in currentLocation.SelectedMissions.ToList())
                {
                    //if we had selected a mission that takes place in the outpost, deselect it when leaving the outpost
                    if (mission.Locations[0] == currentLocation &&
                        mission.Locations[1] == currentLocation)
                    {
                        currentLocation.DeselectMission(mission);
                    }
                }

                if (levelData.HasBeaconStation && !levelData.IsBeaconActive)
                {
                    var beaconMissionPrefabs = MissionPrefab.List.FindAll(m => m.Tags.Any(t => t.Equals("beaconnoreward", StringComparison.OrdinalIgnoreCase)));
                    if (beaconMissionPrefabs.Any())
                    {
                        Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
                        var beaconMissionPrefab = ToolBox.SelectWeightedRandom(beaconMissionPrefabs, beaconMissionPrefabs.Select(p => (float)p.Commonness).ToList(), rand);
                        if (!Missions.Any(m => m.Prefab.Type == beaconMissionPrefab.Type))
                        {
                            extraMissions.Add(beaconMissionPrefab.Instantiate(Map.SelectedConnection.Locations, Submarine.MainSub));
                        }
                    }
                }
                if (levelData.HasHuntingGrounds)
                {
                    var huntingGroundsMissionPrefabs = MissionPrefab.List.FindAll(m => m.Tags.Any(t => t.Equals("huntinggroundsnoreward", StringComparison.OrdinalIgnoreCase)));
                    if (!huntingGroundsMissionPrefabs.Any())
                    {
                        DebugConsole.AddWarning("Could not find a hunting grounds mission for the level. No mission with the tag \"huntinggroundsnoreward\" found.");
                    }
                    else
                    {
                        Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
                        var huntingGroundsMissionPrefab = ToolBox.SelectWeightedRandom(huntingGroundsMissionPrefabs, huntingGroundsMissionPrefabs.Select(p => (float)p.Commonness).ToList(), rand);
                        if (!Missions.Any(m => m.Prefab.Tags.Any(t => t.Equals("huntinggrounds", StringComparison.OrdinalIgnoreCase))))
                        {
                            extraMissions.Add(huntingGroundsMissionPrefab.Instantiate(Map.SelectedConnection.Locations, Submarine.MainSub));
                        }
                    }
                }
            }
        }

        public void LoadNewLevel()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) 
            {
                return;
            }

            if (CoroutineManager.IsCoroutineRunning("LevelTransition"))
            {
                DebugConsole.ThrowError("Level transition already running.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            BeforeLevelLoading?.Invoke();
            BeforeLevelLoading = null;

            if (Level.Loaded == null || Submarine.MainSub == null)
            {
                LoadInitialLevel();
                return;
            }

            var availableTransition = GetAvailableTransition(out LevelData nextLevel, out Submarine leavingSub);

            if (availableTransition == TransitionType.None)
            {
                DebugConsole.ThrowError("Failed to load a new campaign level. No available level transitions " +
                    "(current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                    "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                    "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                    "at start: " + (leavingSub?.AtStartExit.ToString() ?? "null") + ", " +
                    "at end: " + (leavingSub?.AtEndExit.ToString() ?? "null") + ")\n" +
                    Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (nextLevel == null)
            {
                DebugConsole.ThrowError("Failed to load a new campaign level. No available level transitions " +
                    "(transition type: " + availableTransition + ", " +
                    "current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                    "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                    "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                    "at start: " + (leavingSub?.AtStartExit.ToString() ?? "null") + ", " +
                    "at end: " + (leavingSub?.AtEndExit.ToString() ?? "null") + ")\n" +
                    Environment.StackTrace.CleanupStackTrace());
                return;
            }
#if CLIENT
            ShowCampaignUI = ForceMapUI = false;
#endif
            DebugConsole.NewMessage("Transitioning to " + (nextLevel?.Seed ?? "null") +
                " (current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                "at start: " + (leavingSub?.AtStartExit.ToString() ?? "null") + ", " +
                "at end: " + (leavingSub?.AtEndExit.ToString() ?? "null") + ", " +
                "transition type: " + availableTransition + ")");

            IsFirstRound = false;
            bool mirror = map.SelectedConnection != null && map.CurrentLocation != map.SelectedConnection.Locations[0];
            CoroutineManager.StartCoroutine(DoLevelTransition(availableTransition, nextLevel, leavingSub, mirror), "LevelTransition");
        }

        /// <summary>
        /// Load the first level and start the round after loading a save file
        /// </summary>
        protected abstract void LoadInitialLevel();

        protected abstract IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null);

        /// <summary>
        /// Which type of transition between levels is currently possible (if any)
        /// </summary>
        public TransitionType GetAvailableTransition(out LevelData nextLevel, out Submarine leavingSub)
        {
            if (Level.Loaded == null || Submarine.MainSub == null)
            {
                nextLevel = null;
                leavingSub = null;
                return TransitionType.None;
            }

            leavingSub = GetLeavingSub();
            if (leavingSub == null)
            {
                nextLevel = null;
                return TransitionType.None;
            }

            //currently travelling from location to another
            if (Level.Loaded.Type == LevelData.LevelType.LocationConnection)
            {
                if (leavingSub.AtEndExit)
                {
                    if (Map.EndLocation != null && 
                        map.SelectedLocation == Map.EndLocation && 
                        Map.EndLocation.Connections.Any(c => c.LevelData == Level.Loaded.LevelData))
                    {
                        nextLevel = map.StartLocation.LevelData;
                        return TransitionType.End;
                    }
                    if (Level.Loaded.EndLocation != null && Level.Loaded.EndLocation.Type.HasOutpost && Level.Loaded.EndOutpost != null)
                    {
                        nextLevel = Level.Loaded.EndLocation.LevelData;
                        return TransitionType.ProgressToNextLocation;
                    }
                    else if (map.SelectedConnection != null)
                    {
                        nextLevel = Level.Loaded.LevelData != map.SelectedConnection?.LevelData || (map.SelectedConnection.Locations[0] == Level.Loaded.EndLocation == Level.Loaded.Mirrored) ? 
                            map.SelectedConnection.LevelData : null;
                        return TransitionType.ProgressToNextEmptyLocation;
                    }
                    else
                    {
                        nextLevel = null;
                        return TransitionType.ProgressToNextEmptyLocation;
                    }
                }
                else if (leavingSub.AtStartExit)
                {
                    if (map.CurrentLocation.Type.HasOutpost && Level.Loaded.StartOutpost != null)
                    {
                        nextLevel = map.CurrentLocation.LevelData;
                        return TransitionType.ReturnToPreviousLocation;
                    }
                    else if (map.SelectedLocation != null && map.SelectedLocation != map.CurrentLocation && !map.CurrentLocation.Type.HasOutpost &&
                            map.SelectedConnection != null && Level.Loaded.LevelData != map.SelectedConnection.LevelData)
                    {
                        nextLevel = map.SelectedConnection.LevelData;
                        return TransitionType.LeaveLocation;
                    }
                    else
                    {
                        nextLevel = map.SelectedConnection?.LevelData;
                        return TransitionType.ReturnToPreviousEmptyLocation;
                    }
                }
                else
                {
                    nextLevel = null;
                    return TransitionType.None;
                }
            }
            else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                nextLevel = map.SelectedLocation == null ? null : map.SelectedConnection?.LevelData;
                return nextLevel == null ? TransitionType.None : TransitionType.LeaveLocation;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Which submarine is at a position where it can leave the level and enter another one (if any).
        /// </summary>
        private Submarine GetLeavingSub()
        {
            //in single player, only the sub the controlled character is inside can transition between levels
            //in multiplayer, if there's subs at both ends of the level, only the one with more players inside can transition
            //TODO: ignore players who don't have the permission to trigger a transition between levels?
            var leavingPlayers = Character.CharacterList.Where(c => !c.IsDead && (c == Character.Controlled || c.IsRemotePlayer));

            //allow leaving if inside an outpost, and the submarine is either docked to it or close enough
            Submarine leavingSubAtStart = GetLeavingSubAtStart(leavingPlayers);
            Submarine leavingSubAtEnd = GetLeavingSubAtEnd(leavingPlayers);

            if (Level.IsLoadedOutpost)
            {
                leavingSubAtStart ??= Submarine.MainSub;
                leavingSubAtEnd ??= Submarine.MainSub;            
            }
            int playersInSubAtStart = leavingSubAtStart == null || !leavingSubAtStart.AtStartExit ? 0 :
                leavingPlayers.Count(c => c.Submarine == leavingSubAtStart || leavingSubAtStart.DockedTo.Contains(c.Submarine) || (Level.Loaded.StartOutpost != null && c.Submarine == Level.Loaded.StartOutpost));
            int playersInSubAtEnd = leavingSubAtEnd == null || !leavingSubAtEnd.AtEndExit ? 0 :
                leavingPlayers.Count(c => c.Submarine == leavingSubAtEnd || leavingSubAtEnd.DockedTo.Contains(c.Submarine) || (Level.Loaded.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost));

            if (playersInSubAtStart == 0 && playersInSubAtEnd == 0) 
            {
                return null; 
            }

            return playersInSubAtStart > playersInSubAtEnd ? leavingSubAtStart : leavingSubAtEnd;

            static Submarine GetLeavingSubAtStart(IEnumerable<Character> leavingPlayers)
            {
                if (Level.Loaded.StartOutpost == null)
                {
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.StartExitPosition, ignoreOutposts: true, ignoreRespawnShuttle: true, teamType: leavingPlayers.FirstOrDefault()?.TeamID);
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
                else
                {
                    //if there's a sub docked to the outpost, we can leave the level
                    if (Level.Loaded.StartOutpost.DockedTo.Any())
                    {
                        var dockedSub = Level.Loaded.StartOutpost.DockedTo.FirstOrDefault();
                        if (dockedSub == GameMain.NetworkMember?.RespawnManager?.RespawnShuttle || dockedSub.TeamID != leavingPlayers.FirstOrDefault()?.TeamID) { return null; }
                        return dockedSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : dockedSub;
                    }

                    //nothing docked, check if there's a sub close enough to the outpost and someone inside the outpost
                    if (Level.Loaded.Type == LevelData.LevelType.LocationConnection && !leavingPlayers.Any(s => s.Submarine == Level.Loaded.StartOutpost)) { return null; }
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.StartOutpost.WorldPosition, ignoreOutposts: true, ignoreRespawnShuttle: true, teamType: leavingPlayers.FirstOrDefault()?.TeamID);
                    if (closestSub == null || !closestSub.AtStartExit) { return null; }
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
            }

            static Submarine GetLeavingSubAtEnd(IEnumerable<Character> leavingPlayers)
            {
                //no "end" in outpost levels
                if (Level.Loaded.Type == LevelData.LevelType.Outpost) { return null; }

                if (Level.Loaded.EndOutpost == null)
                {
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.EndExitPosition, ignoreOutposts: true, ignoreRespawnShuttle: true, teamType: leavingPlayers.FirstOrDefault()?.TeamID);
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
                else
                {
                    //if there's a sub docked to the outpost, we can leave the level
                    if (Level.Loaded.EndOutpost.DockedTo.Any())
                    {
                        var dockedSub = Level.Loaded.EndOutpost.DockedTo.FirstOrDefault();
                        if (dockedSub == GameMain.NetworkMember?.RespawnManager?.RespawnShuttle || dockedSub.TeamID != leavingPlayers.FirstOrDefault()?.TeamID) { return null; }
                        return dockedSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : dockedSub;
                    }

                    //nothing docked, check if there's a sub close enough to the outpost and someone inside the outpost
                    if (Level.Loaded.Type == LevelData.LevelType.LocationConnection && !leavingPlayers.Any(s => s.Submarine == Level.Loaded.EndOutpost)) { return null; }
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.EndOutpost.WorldPosition, ignoreOutposts: true, ignoreRespawnShuttle: true, teamType: leavingPlayers.FirstOrDefault()?.TeamID);
                    if (closestSub == null || !closestSub.AtEndExit) { return null; }
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
            }
        }

        public override void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            List<Item> takenItems = new List<Item>();
            foreach (Item item in Item.ItemList)
            {
                if (!item.SpawnedInOutpost || item.OriginalModuleIndex < 0) { continue; }
                var owner = item.GetRootInventoryOwner();
                if ((!(owner?.Submarine?.Info?.IsOutpost ?? false)) || (owner is Character character && character.TeamID == CharacterTeamType.Team1) || item.Submarine == null || !item.Submarine.Info.IsOutpost)
                {
                    takenItems.Add(item);
                }
            }
            if (map != null && CargoManager != null)
            {
                map.CurrentLocation.RegisterTakenItems(takenItems);
                map.CurrentLocation.AddToStock(CargoManager.SoldItems);
                CargoManager.ClearSoldItemsProjSpecific();
                map.CurrentLocation.RemoveFromStock(CargoManager.PurchasedItems);
            }
            if (GameMain.NetworkMember == null)
            {
                CargoManager.ClearItemsInBuyCrate();
                CargoManager.ClearItemsInSellCrate();
                CargoManager.ClearItemsInSellFromSubCrate();
            }
            else
            {
                if (GameMain.NetworkMember.IsServer)
                {
                    CargoManager?.ClearItemsInBuyCrate();
                    // TODO: CargoManager?.ClearItemsInSellFromSubCrate();
                }
                else if (GameMain.NetworkMember.IsClient)
                {
                    CargoManager?.ClearItemsInSellCrate();
                }
            }

            if (Level.Loaded?.StartOutpost != null)
            {
                List<Character> killedCharacters = new List<Character>();
                foreach (Character c in Level.Loaded.StartOutpost.Info.OutpostNPCs.SelectMany(kpv => kpv.Value))
                {
                    if (!c.IsDead && !c.Removed) { continue; }
                    killedCharacters.Add(c);
                }
                map.CurrentLocation.RegisterKilledCharacters(killedCharacters);
                Level.Loaded.StartOutpost.Info.OutpostNPCs.Clear();
            }

            List<Character> deadCharacters = Character.CharacterList.FindAll(c => c.IsDead);
            foreach (Character c in deadCharacters)
            {
                if (c.IsDead) 
                {
                    CrewManager.RemoveCharacterInfo(c.Info);
                    c.DespawnNow();
                }
            }

            foreach (CharacterInfo ci in CrewManager.CharacterInfos.ToList())
            {
                if (ci.CauseOfDeath != null)
                {
                    CrewManager.RemoveCharacterInfo(ci);
                }
            }

            foreach (DockingPort port in DockingPort.List)
            {
                if (port.Door != null &
                    port.Item.Submarine.Info.Type == SubmarineType.Player && 
                    port.DockingTarget?.Item?.Submarine != null && 
                    port.DockingTarget.Item.Submarine.Info.IsOutpost)
                {
                    port.Door.IsOpen = false;
                }
            }
        }


        public void EndCampaign()
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.IsOnPlayerTeam)
                {
                    c.CharacterHealth.RemoveAllAfflictions();
                }
            }
            foreach (LocationConnection connection in Map.Connections)
            {
                connection.Difficulty = MathHelper.Lerp(connection.Difficulty, 100.0f, 0.25f);
                connection.LevelData.Difficulty = connection.Difficulty;
                connection.LevelData.IsBeaconActive = false;
                connection.LevelData.HasHuntingGrounds = connection.LevelData.OriginallyHadHuntingGrounds;
            }
            foreach (Location location in Map.Locations)
            {
                if (location.Type != location.OriginalType)
                {
                    location.ChangeType(location.OriginalType);
                    location.PendingLocationTypeChange = null;
                }
                location.CreateStore(force: true);
                location.ClearMissions();
                location.Discovered = false;
                location.LevelData?.EventHistory?.Clear();
            }
            Map.SetLocation(Map.Locations.IndexOf(Map.StartLocation));
            Map.SelectLocation(-1);
            if (Map.Radiation != null)
            {
                Map.Radiation.Amount = Map.Radiation.Params.StartingRadiation;
            }
            foreach (Location location in Map.Locations)
            {
                location.TurnsInRadiation = 0;
            }
            EndCampaignProjSpecific();

            if (CampaignMetadata != null)
            {
                int loops = CampaignMetadata.GetInt("campaign.endings", 0);
                CampaignMetadata.SetValue("campaign.endings",  loops + 1);
            }
        }

        protected virtual void EndCampaignProjSpecific() { }

        public bool TryHireCharacter(Location location, CharacterInfo characterInfo)
        {
            if (characterInfo == null) { return false; }
            if (Money < characterInfo.Salary) { return false; }
            characterInfo.IsNewHire = true;
            location.RemoveHireableCharacter(characterInfo);
            CrewManager.AddCharacterInfo(characterInfo);
            Money -= characterInfo.Salary;
            return true;
        }

        private void NPCInteract(Character npc, Character interactor)
        {
            if (!npc.AllowCustomInteract) { return; }
            NPCInteractProjSpecific(npc, interactor);
            string coroutineName = "DoCharacterWait." + (npc?.ID ?? Entity.NullEntityID);
            if (!CoroutineManager.IsCoroutineRunning(coroutineName))
            {
                CoroutineManager.StartCoroutine(DoCharacterWait(npc, interactor), coroutineName);
            }
        }

        private IEnumerable<object> DoCharacterWait(Character npc, Character interactor)
        {
            if (npc == null || interactor == null) { yield return CoroutineStatus.Failure; }

            HumanAIController humanAI = npc.AIController as HumanAIController;
            if (humanAI == null) { yield return CoroutineStatus.Failure; }

            var waitOrder = Order.PrefabList.Find(o => o.Identifier.Equals("wait", StringComparison.OrdinalIgnoreCase));
            humanAI.SetForcedOrder(waitOrder, string.Empty, null);
            var waitObjective = humanAI.ObjectiveManager.ForcedOrder;
            humanAI.FaceTarget(interactor);
            
            while (!npc.Removed && !interactor.Removed &&
                Vector2.DistanceSquared(npc.WorldPosition, interactor.WorldPosition) < 300.0f * 300.0f &&
                humanAI.ObjectiveManager.ForcedOrder == waitObjective &&
                humanAI.AllowCampaignInteraction() &&
                !interactor.IsIncapacitated)
            {
                yield return CoroutineStatus.Running;
            }

#if CLIENT
            ShowCampaignUI = false;
#endif

            humanAI.ClearForcedOrder();
            yield return CoroutineStatus.Success;
        }

        partial void NPCInteractProjSpecific(Character npc, Character interactor);

        public void AssignNPCMenuInteraction(Character character, InteractionType interactionType)
        {
            character.CampaignInteractionType = interactionType;
            if (interactionType == InteractionType.None) 
            {
                character.SetCustomInteract(null, null);
                return; 
            }
            character.CharacterHealth.UseHealthWindow = false;
            //character.CanInventoryBeAccessed = false;
            character.SetCustomInteract(
                NPCInteract,
#if CLIENT
                hudText: TextManager.GetWithVariable("CampaignInteraction." + interactionType, "[key]", GameMain.Config.KeyBindText(InputType.Use)));
#else
                hudText: TextManager.Get("CampaignInteraction." + interactionType));
#endif
        }

        private readonly Dictionary<Character, float> characterOutOfBoundsTimer = new Dictionary<Character, float>();

        protected void KeepCharactersCloseToOutpost(float deltaTime)
        {
            const float MaxDist = 3000.0f;
            const float MinDist = 2500.0f;

            if (!Level.IsLoadedOutpost) { return; }

            Rectangle worldBorders = Submarine.MainSub.GetDockedBorders();
            worldBorders.Location += Submarine.MainSub.WorldPosition.ToPoint();

            foreach (Character c in Character.CharacterList)
            {
                if ((c != Character.Controlled && !c.IsRemotePlayer) || 
                    c.Removed || c.IsDead || c.IsIncapacitated || c.Submarine != null)
                {
                    if (characterOutOfBoundsTimer.ContainsKey(c)) 
                    {
                        c.OverrideMovement = null;
                        characterOutOfBoundsTimer.Remove(c); 
                    }
                    continue;
                }

                if (c.WorldPosition.Y < worldBorders.Y - worldBorders.Height - MaxDist) 
                { 
                    if (!characterOutOfBoundsTimer.ContainsKey(c)) 
                    { 
                        characterOutOfBoundsTimer.Add(c, 0.0f); 
                    }
                    else
                    {
                        characterOutOfBoundsTimer[c] += deltaTime;
                    }
                }
                else if (c.WorldPosition.Y > worldBorders.Y - worldBorders.Height - MinDist)
                {
                    if (characterOutOfBoundsTimer.ContainsKey(c))
                    {
                        c.OverrideMovement = null; 
                        characterOutOfBoundsTimer.Remove(c); 
                    }
                }
            }

            foreach (KeyValuePair<Character, float> character in characterOutOfBoundsTimer)
            {
                if (character.Value <= 0.0f)
                {
                    if (IsSinglePlayer)
                    {
#if CLIENT
                        GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(
                            TextManager.Get("RadioAnnouncerName"), 
                            TextManager.Get("TooFarFromOutpostWarning"), 
                            Networking.ChatMessageType.Default, 
                            sender: null);
#endif
                    }
                    else
                    {
#if SERVER
                        foreach (Networking.Client c in GameMain.Server.ConnectedClients)
                        {
                        
                            GameMain.Server.SendDirectChatMessage(Networking.ChatMessage.Create(
                                TextManager.Get("RadioAnnouncerName"), 
                                TextManager.Get("TooFarFromOutpostWarning"),  Networking.ChatMessageType.Default, null), c);
                        }
#endif
                    }
                }
                character.Key.OverrideMovement = Vector2.UnitY * 10.0f;
#if CLIENT
                Character.DisableControls = true;
#endif
                //if the character doesn't get back up in 10 seconds (something blocking the way?), teleport it closer
                if (character.Value > 10.0f)
                {
                    Vector2 teleportPos = character.Key.WorldPosition;
                    teleportPos += Vector2.Normalize(Submarine.MainSub.WorldPosition - character.Key.WorldPosition) * 100.0f;
                    character.Key.AnimController.SetPosition(ConvertUnits.ToSimUnits(teleportPos));
                }
            }
        }

        public void OutpostNPCAttacked(Character npc, Character attacker, AttackResult attackResult)
        {
            if (npc == null || attacker == null || npc.IsDead || npc.IsInstigator) { return; }
            if (npc.TeamID != CharacterTeamType.FriendlyNPC) { return; }
            if (!attacker.IsRemotePlayer && attacker != Character.Controlled) { return; }
            Location location = Map?.CurrentLocation;
            if (location != null)
            {
                location.Reputation.Value -= attackResult.Damage * Reputation.ReputationLossPerNPCDamage;
            }
        }

        public abstract void Save(XElement element);
        
        public void LogState()
        {
            DebugConsole.NewMessage("********* CAMPAIGN STATUS *********", Color.White);
            DebugConsole.NewMessage("   Money: " + Money, Color.White);
            DebugConsole.NewMessage("   Current location: " + map.CurrentLocation.Name, Color.White);

            DebugConsole.NewMessage("   Available destinations: ", Color.White);
            for (int i = 0; i < map.CurrentLocation.Connections.Count; i++)
            {
                Location destination = map.CurrentLocation.Connections[i].OtherLocation(map.CurrentLocation);
                if (destination == map.SelectedLocation)
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name + " [SELECTED]", Color.White);
                }
                else
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name, Color.White);
                }
            }

            if (map.CurrentLocation != null)
            {
                foreach (Mission mission in map.CurrentLocation.SelectedMissions)
                {
                    DebugConsole.NewMessage("   Selected mission: " + mission.Name, Color.White);
                    DebugConsole.NewMessage("\n" + mission.Description, Color.White);
                }
            }
        }

        public override void Remove()
        {
            base.Remove();
            map?.Remove();
            map = null;
        }
    }
}
