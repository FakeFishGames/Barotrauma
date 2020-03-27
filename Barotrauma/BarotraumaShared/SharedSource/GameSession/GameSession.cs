using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class GameSession
    {
        public enum InfoFrameTab { Crew, Mission, MyCharacter, ManagePlayers };

        public readonly EventManager EventManager;

        public GameMode GameMode;

        //two locations used as the start and end in the MP mode
        private Location[] dummyLocations;
        public CrewManager CrewManager;

        public double RoundStartTime;

        public Mission Mission { get; private set; }

        public Character.TeamType? WinningTeam;

        public Level Level { get; private set; }

        public Map Map
        {
            get
            {
                return (GameMode as CampaignMode)?.Map;
            }
        }

        public Location StartLocation
        {
            get
            {
                if (Map != null) return Map.CurrentLocation;

                if (dummyLocations == null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[0];
            }
        }

        public Location EndLocation
        {
            get
            {
                if (Map != null) return Map.SelectedLocation;

                if (dummyLocations == null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[1];
            }
        }

        public SubmarineInfo SubmarineInfo { get; set; }

        public Submarine Submarine { get; set; }

        public string SavePath { get; set; }

        partial void InitProjSpecific();

        public GameSession(SubmarineInfo submarineInfo, string savePath, GameModePreset gameModePreset, MissionType missionType = MissionType.None)
            : this(submarineInfo, savePath)
        {
            CrewManager = new CrewManager(gameModePreset != null && gameModePreset.IsSinglePlayer);
            GameMode = gameModePreset.Instantiate(missionType);
        }

        public GameSession(SubmarineInfo submarineInfo, string savePath, GameModePreset gameModePreset, MissionPrefab missionPrefab)
            : this(submarineInfo, savePath)
        {
            CrewManager = new CrewManager(gameModePreset != null && gameModePreset.IsSinglePlayer);
            GameMode = gameModePreset.Instantiate(missionPrefab);

#if CLIENT
            if (GameMode is SubTestMode) { EventManager = null; }
#endif
        }

        private GameSession(SubmarineInfo submarineInfo, string savePath)
        {
            InitProjSpecific();
            SubmarineInfo = submarineInfo;
            /*Submarine = new Submarine(submarineInfo);
            Submarine.MainSub = Submarine;*/
            GameMain.GameSession = this;
            EventManager = new EventManager();
            this.SavePath = savePath;
        }


        public GameSession(SubmarineInfo selectedSubInfo, string saveFile, XDocument doc)
            : this(selectedSubInfo, saveFile)
        {
            Submarine.MainSub = Submarine;

            GameMain.GameSession = this;
            //selectedSub.Name = doc.Root.GetAttributeString("submarine", selectedSub.Name);

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
#if CLIENT
                    case "gamemode": //legacy support
                    case "singleplayercampaign":
                        CrewManager = new CrewManager(true);
                        GameMode = SinglePlayerCampaign.Load(subElement);
                        break;
#endif
                    case "multiplayercampaign":
                        CrewManager = new CrewManager(false);
                        GameMode = MultiPlayerCampaign.LoadNew(subElement);
                        break;
                }
            }
        }

        private void CreateDummyLocations()
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
                dummyLocations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f), null, rand);
            }
        }

        public void LoadPrevious()
        {
            Submarine.Unload();
            SaveUtil.LoadGame(SavePath);
        }

        public void StartRound(string levelSeed, float? difficulty = null)
        {
            Level randomLevel = Level.CreateRandom(levelSeed, difficulty);

            StartRound(randomLevel);
        }

        public void StartRound(Level level, bool mirrorLevel = false)
        {
            //make sure no status effects have been carried on from the next round
            //(they should be stopped in EndRound, this is a safeguard against cases where the round is ended ungracefully)
            StatusEffect.StopAll();

#if CLIENT
            GameMain.LightManager.LosEnabled = GameMain.Client == null || GameMain.Client.CharacterInfo != null;
            if (GameMain.Client == null) GameMain.LightManager.LosMode = GameMain.Config.LosMode;
#endif
            this.Level = level;

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

            Submarine.Unload();
            Submarine = Submarine.MainSub = new Submarine(SubmarineInfo);
            Submarine.MainSub = Submarine;
            if (GameMode.Mission != null && GameMode.Mission.TeamCount > 1 && Submarine.MainSubs[1] == null)
            {
                Submarine.MainSubs[1] = new Submarine(SubmarineInfo, true);
            }

            if (level != null)
            {
                level.Generate(mirrorLevel);
                if (level.StartOutpost != null)
                {
                    //start by placing the sub below the outpost
                    Rectangle outpostBorders = Level.Loaded.StartOutpost.GetDockedBorders();
                    Rectangle subBorders = Submarine.GetDockedBorders();

                    Vector2 startOutpostSize = Vector2.Zero;
                    if (Level.Loaded.StartOutpost != null)
                    {
                        startOutpostSize = Level.Loaded.StartOutpost.Borders.Size.ToVector2();
                    }
                    Submarine.SetPosition(
                        Level.Loaded.StartOutpost.WorldPosition -
                        new Vector2(0.0f, outpostBorders.Height / 2 + subBorders.Height / 2));

                    //find the port that's the nearest to the outpost and dock if one is found
                    float closestDistance = 0.0f;
                    DockingPort myPort = null, outPostPort = null;
                    foreach (DockingPort port in DockingPort.List)
                    {
                        if (port.IsHorizontal || port.Docked) { continue; }
                        if (port.Item.Submarine == level.StartOutpost)
                        {
                            outPostPort = port;
                            continue;
                        }
                        if (port.Item.Submarine != Submarine) { continue; }

                        //the submarine port has to be at the top of the sub
                        if (port.Item.WorldPosition.Y < Submarine.WorldPosition.Y) { continue; }

                        float dist = Vector2.DistanceSquared(port.Item.WorldPosition, level.StartOutpost.WorldPosition);
                        if (myPort == null || dist < closestDistance || (port.MainDockingPort && !myPort.MainDockingPort))
                        {
                            myPort = port;
                            closestDistance = dist;
                        }
                    }

                    if (myPort != null && outPostPort != null)
                    {
                        Vector2 portDiff = myPort.Item.WorldPosition - Submarine.WorldPosition;
                        Submarine.SetPosition((outPostPort.Item.WorldPosition - portDiff) - Vector2.UnitY * outPostPort.DockedDistance);
                        myPort.Dock(outPostPort);
                        myPort.Lock(true);
                    }
                }
                else
                {
                    Submarine.SetPosition(Submarine.FindSpawnPos(level.StartPosition));
                }
            }

            foreach (var sub in Submarine.Loaded)
            {
                if (sub.Info.IsOutpost)
                {
                    sub.DisableObstructedWayPoints();
                }
            }

            Entity.Spawner = new EntitySpawner();
            
            if (GameMode.Mission != null) { Mission = GameMode.Mission; }
            if (GameMode != null) { GameMode.Start(); }
            if (GameMode.Mission != null)
            {
                int prevEntityCount = Entity.GetEntityList().Count;
                Mission.Start(Level.Loaded);
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && Entity.GetEntityList().Count != prevEntityCount)
                {
                    DebugConsole.ThrowError(
                        "Entity count has changed after starting a mission as a client. " +
                        "The clients should not instantiate entities themselves when starting the mission," +
                        " but instead the server should inform the client of the spawned entities using Mission.ServerWriteInitial.");
                }
            }

            EventManager?.StartRound(level);
            SteamAchievementManager.OnStartRound();

            if (GameMode != null)
            {
                GameMode.ShowStartMessage();

                if (GameMain.NetworkMember == null) 
                {
                    //only place items and corpses here in single player
                    //the server does this after loading the respawn shuttle
                    Level?.SpawnCorpses();
                    AutoItemPlacer.PlaceIfNeeded(GameMode);
                }
                if (GameMode is MultiPlayerCampaign mpCampaign && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    mpCampaign.CargoManager.CreateItems();
                }
            }

            GameAnalyticsManager.AddDesignEvent("Submarine:" + Submarine.Info.Name);
            GameAnalyticsManager.AddDesignEvent("Level", ToolBox.StringToInt(level?.Seed ?? "[NO_LEVEL]"));
            GameAnalyticsManager.AddProgressionEvent(GameAnalyticsSDK.Net.EGAProgressionStatus.Start,
                    GameMode.Preset.Identifier, (Mission == null ? "None" : Mission.GetType().ToString()));

#if CLIENT
            if (GameMode is SinglePlayerCampaign) { SteamAchievementManager.OnBiomeDiscovered(level.Biome); }
            if (!(GameMode is SubTestMode)) { RoundSummary = new RoundSummary(this); }

            GameMain.GameScreen.ColorFade(Color.Black, Color.TransparentBlack, 5.0f);

            if (!(GameMode is TutorialMode) && !(GameMode is SubTestMode))
            {
                GUI.AddMessage("", Color.Transparent, 3.0f, playSound: false);
                GUI.AddMessage(level.Biome.DisplayName, Color.Lerp(Color.CadetBlue, Color.DarkRed, level.Difficulty / 100.0f), 5.0f, playSound: false);
                GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Destination"), EndLocation.Name), Color.CadetBlue, playSound: false);
                GUI.AddMessage(TextManager.AddPunctuation(':', TextManager.Get("Mission"), (Mission == null ? TextManager.Get("None") : Mission.Name)), Color.CadetBlue, playSound: false);
            }
#endif

            RoundStartTime = Timing.TotalTime;
            GameMain.ResetFrameTime();
        }

        public void Update(float deltaTime)
        {
            EventManager?.Update(deltaTime);
            GameMode?.Update(deltaTime);
            Mission?.Update(deltaTime);

            UpdateProjSpecific(deltaTime);
        }

        partial void UpdateProjSpecific(float deltaTime);

        public void EndRound(string endMessage)
        {
            if (Mission != null) Mission.End();
            GameAnalyticsManager.AddProgressionEvent(
                (Mission == null || Mission.Completed)  ? GameAnalyticsSDK.Net.EGAProgressionStatus.Complete : GameAnalyticsSDK.Net.EGAProgressionStatus.Fail,
                GameMode.Preset.Identifier,
                (Mission == null ? "None" : Mission.GetType().ToString()));

#if CLIENT
            if (RoundSummary != null)
            {
                GUIFrame summaryFrame = RoundSummary.CreateSummaryFrame(endMessage);
                GUIMessageBox.MessageBoxes.Add(summaryFrame);
                var okButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), summaryFrame.Children.First().Children.First().FindChild("buttonarea").RectTransform),
                    TextManager.Get("OK"))
                {
                    OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; }
                };
            }
#endif

            EventManager?.EndRound();
            SteamAchievementManager.OnRoundEnded(this);

            Mission = null;

            StatusEffect.StopAll();
        }

        public void KillCharacter(Character character)
        {
#if CLIENT
            CrewManager.KillCharacter(character);
#endif
        }

        public void ReviveCharacter(Character character)
        {
#if CLIENT
            CrewManager.ReviveCharacter(character);
#endif
        }

        public static bool IsCompatibleWithSelectedContentPackages(IList<string> contentPackagePaths, out string errorMsg)
        {
            errorMsg = "";
            //no known content packages, must be an older save file
            if (!contentPackagePaths.Any()) { return true; }

            List<string> missingPackages = new List<string>();
            foreach (string packagePath in contentPackagePaths)
            {
                if (!GameMain.Config.SelectedContentPackages.Any(cp => cp.Path == packagePath))
                {
                    missingPackages.Add(packagePath);
                }
            }
            List<string> excessPackages = new List<string>();
            foreach (ContentPackage cp in GameMain.Config.SelectedContentPackages)
            {
                if (!cp.HasMultiplayerIncompatibleContent) { continue; }
                if (!contentPackagePaths.Any(p => p == cp.Path))
                {
                    excessPackages.Add(cp.Name);
                }
            }

            bool orderMismatch = false;
            if (missingPackages.Count == 0 && missingPackages.Count == 0)
            {
                var selectedPackages = GameMain.Config.SelectedContentPackages.Where(cp => cp.HasMultiplayerIncompatibleContent).ToList();
                for (int i = 0; i < contentPackagePaths.Count && i < selectedPackages.Count; i++)
                {
                    if (contentPackagePaths[i] != selectedPackages[i].Path)
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
                if (!string.IsNullOrEmpty(errorMsg)) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.incompatiblecontentpackage", "[incompatiblecontentpackage]", excessPackages[0]);
            }
            else if (excessPackages.Count > 1)
            {
                if (!string.IsNullOrEmpty(errorMsg)) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.incompatiblecontentpackages", "[incompatiblecontentpackages]", string.Join(", ", excessPackages));
            }
            if (orderMismatch)
            {
                if (!string.IsNullOrEmpty(errorMsg)) { errorMsg += "\n"; }
                errorMsg += TextManager.GetWithVariable("campaignmode.contentpackageordermismatch", "[loadorder]", string.Join(", ", contentPackagePaths));
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

            doc.Root.Add(new XAttribute("savetime", ToolBox.Epoch.NowLocal));
            doc.Root.Add(new XAttribute("submarine", SubmarineInfo == null ? "" : SubmarineInfo.Name));
            doc.Root.Add(new XAttribute("mapseed", Map.Seed));
            doc.Root.Add(new XAttribute("selectedcontentpackages",
                string.Join("|", GameMain.Config.SelectedContentPackages.Where(cp => cp.HasMultiplayerIncompatibleContent).Select(cp => cp.Path))));

            ((CampaignMode)GameMode).Save(doc.Root);

            try
            {
                doc.Save(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving gamesession to \"" + filePath + "\" failed!", e);
            }
        }

        public void Load(XElement saveElement)
        {
            foreach (XElement subElement in saveElement.Elements())
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
        }

    }
}
