using Barotrauma.Networking;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    class GameMain
    {
        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static World World;
        public static GameSettings Config;

        public static GameServer Server;
        public const GameClient Client = null;
        public static NetworkMember NetworkMember
        {
            get { return Server as NetworkMember; }
        }

        public static GameSession GameSession;

        public static GameMain Instance
        {
            get;
            private set;
        }

        //NilMod Class
        public static NilMod NilMod;

        //only screens the server implements
        public static GameScreen GameScreen;
        public static NetLobbyScreen NetLobbyScreen;

        //null screens because they are not implemented by the server,
        //but they're checked for all over the place
        //TODO: maybe clean up instead of having these constants
        public static readonly Screen MainMenuScreen = UnimplementedScreen.Instance;
        public static readonly Screen LobbyScreen = UnimplementedScreen.Instance;

        public static readonly Screen ServerListScreen = UnimplementedScreen.Instance;

        public static readonly Screen SubEditorScreen = UnimplementedScreen.Instance;
        public static readonly Screen CharacterEditorScreen = UnimplementedScreen.Instance;
        
        public static bool ShouldRun = true;

        public static ContentPackage SelectedPackage
        {
            get { return Config.SelectedContentPackage; }
        }

        public GameMain()
        {
            Instance = this;

            World = new World(new Vector2(0, -9.82f));
            FarseerPhysics.Settings.AllowSleep = true;
            FarseerPhysics.Settings.ContinuousPhysics = false;
            FarseerPhysics.Settings.VelocityIterations = 1;
            FarseerPhysics.Settings.PositionIterations = 1;

            Config = new GameSettings("config.xml");
            if (Config.WasGameUpdated)
            {
                UpdaterUtil.CleanOldFiles();
                Config.WasGameUpdated = false;
                Config.Save("config.xml");
            }

            NilMod = new NilMod();
            NilMod.Load(false);

            GameScreen = new GameScreen();
        }

        public void Init()
        {
            Mission.Init();
            MapEntityPrefab.Init();
            LevelGenerationParams.LoadPresets();

            JobPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Jobs));
            StructurePrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Structure));

            ItemPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Item));

            GameModePreset.Init();

            LocationType.Init();

            Submarine.RefreshSavedSubs();

            Screen.SelectNull();

            NetLobbyScreen = new NetLobbyScreen();
        }

        public void StartServer()
        {
            if (GameMain.NilMod.OverrideServerSettings)
            {
                NetLobbyScreen.ServerName = GameMain.NilMod.ServerName;
                Server = new GameServer(GameMain.NilMod.ServerName,
                GameMain.NilMod.ServerPort,
                GameMain.NilMod.PublicServer,
                GameMain.NilMod.UseServerPassword ? "" : GameMain.NilMod.ServerPassword,
                GameMain.NilMod.UPNPForwarding,
                GameMain.NilMod.MaxPlayers);
            }
            else
            {
                XDocument doc = XMLExtensions.TryLoadXml(GameServer.SettingsFile);
                if (doc == null)
                {
                    DebugConsole.ThrowError("File \"" + GameServer.SettingsFile + "\" not found. Starting the server with default settings.");
                    Server = new GameServer("Server", 14242, false, "", false, 10);
                    return;
                }

            Server = new GameServer(
                doc.Root.GetAttributeString("name", "Server"),
                doc.Root.GetAttributeInt("port", 14242),
                doc.Root.GetAttributeBool("public", false),
                doc.Root.GetAttributeString("password", ""),
                doc.Root.GetAttributeBool("enableupnp", false),
                doc.Root.GetAttributeInt("maxplayers", 10));
            }
            NilMod.FetchExternalIP();
        }

        public void CloseServer()
        {
            Server.Disconnect();
            Server = null;
        }

        public void Run()
        {
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Character));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Item));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Items.Components.ItemComponent));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Hull));

            Init();
            StartServer();

            DefaultServerStartup();

            Timing.Accumulator = 0.0;

            double frequency = (double)Stopwatch.Frequency;
            if (frequency <= 1500)
            {
                DebugConsole.NewMessage("WARNING: Stopwatch frequency under 1500 ticks per second. Expect significant syncing accuracy issues.", Color.Yellow);
            }
            
            Stopwatch stopwatch = Stopwatch.StartNew();
            long prevTicks = stopwatch.ElapsedTicks;
            while (ShouldRun)
            {
                long currTicks = stopwatch.ElapsedTicks;
                //Necessary for some timing
                Timing.TotalTime = stopwatch.ElapsedMilliseconds / 1000;
                Timing.Accumulator += (double)(currTicks - prevTicks) / frequency;
                prevTicks = currTicks;
                while (Timing.Accumulator>=Timing.Step)
                {
                    DebugConsole.Update();
                    if (Screen.Selected != null) Screen.Selected.Update((float)Timing.Step);
                    Server.Update((float)Timing.Step);
                    CoroutineManager.Update((float)Timing.Step, (float)Timing.Step);
            
                    Timing.Accumulator -= Timing.Step;
                }
                int frameTime = (int)(((double)(stopwatch.ElapsedTicks - prevTicks) / frequency)*1000.0);
                Thread.Sleep(Math.Max(((int)(Timing.Step * 1000.0) - frameTime)/2,0));
            }
            stopwatch.Stop();

            CloseServer();

        }
        
        public void ProcessInput()
        {
            while (true)
            {
                string input = Console.ReadLine();
                lock (DebugConsole.QueuedCommands)
                {
                    DebugConsole.QueuedCommands.Add(input);
                }
            }
        }

        public CoroutineHandle ShowLoading(IEnumerable<object> loader, bool waitKeyHit = true)
        {
            return CoroutineManager.StartCoroutine(loader);
        }

        public void DefaultServerStartup()
        {
            Boolean startcampaign = false;

            //Default Mission Parameters
            if (GameMain.NilMod.DefaultGamemode.ToLowerInvariant() == "mission")
            {
                GameMain.NetLobbyScreen.SelectedModeIndex = 1;
                //GameMain.NilMod.DefaultMissionType = "Cargo";
                //Only select this default if we actually default to mission mode
                switch (GameMain.NilMod.DefaultMissionType.ToLowerInvariant())
                {
                    case "random":
                        GameMain.NetLobbyScreen.MissionTypeIndex = 0;
                        break;
                    case "salvage":
                        GameMain.NetLobbyScreen.MissionTypeIndex = 1;
                        break;
                    case "monster":
                        GameMain.NetLobbyScreen.MissionTypeIndex = 2;
                        break;
                    case "cargo":
                        GameMain.NetLobbyScreen.MissionTypeIndex = 3;
                        break;
                    case "combat":
                        GameMain.NetLobbyScreen.MissionTypeIndex = 4;
                        break;
                    //Random if no valid mission type
                    default:
                        GameMain.NetLobbyScreen.MissionTypeIndex = 5;
                        break;
                }
            }
            else if (GameMain.NilMod.DefaultGamemode.ToLowerInvariant() == "campaign")
            {
                startcampaign = true;
                GameMain.NetLobbyScreen.SelectedModeIndex = 1;
            }
            else
            {
                GameMain.NetLobbyScreen.SelectedModeIndex = 0;
            }

            DebugConsole.NewMessage(
                "Save Server Logs: " + (GameMain.Server.SaveServerLogs ? "YES" : "NO") +
                ", Allow File Transfers: " + (GameMain.Server.AllowFileTransfers ? "YES" : "NO"), Color.Cyan);

            DebugConsole.NewMessage(
                "Allow Spectating: " + (GameMain.Server.AllowSpectating ? "YES" : "NO"), Color.Cyan);

            //LevelSeed = ToolBox.RandomSeed(8);



            DebugConsole.NewMessage(" ", Color.Cyan);

            DebugConsole.NewMessage(
                "Auto Restart: " + (GameMain.Server.AutoRestart ? "YES" : "NO") +
                ", Auto Restart Interval: " + ToolBox.SecondsToReadableTime(GameMain.Server.AutoRestartInterval), Color.Cyan);

            DebugConsole.NewMessage(
                "End Round At Level End: " + (GameMain.Server.EndRoundAtLevelEnd ? "YES" : "NO") +
                ", End Vote Required Ratio: " + (GameMain.Server.EndVoteRequiredRatio * 100) + "%", Color.Cyan);
            
            DebugConsole.NewMessage(
                "Allow Vote Kick: " + (GameMain.Server.AllowVoteKick ? "YES" : "NO") +
                ", Kick Vote Required Ratio: " + (GameMain.Server.KickVoteRequiredRatio * 100) + "%", Color.Cyan);

            DebugConsole.NewMessage(" ", Color.Cyan);

            DebugConsole.NewMessage(
                "Allow Respawns: " + (GameMain.Server.AllowRespawn ? "YES" : "NO") +
                ", Min Respawn Ratio:" + GameMain.Server.MinRespawnRatio, Color.Cyan);

            DebugConsole.NewMessage(
                "Respawn Interval: " + ToolBox.SecondsToReadableTime(GameMain.Server.RespawnInterval) +
                ", Max Transport Time:" + ToolBox.SecondsToReadableTime(GameMain.Server.MaxTransportTime), Color.Cyan);

            DebugConsole.NewMessage(" ", Color.Cyan);

            DebugConsole.NewMessage(
                "Gamemode Selection: " + GameMain.Server.ModeSelectionMode.ToString() +
                ", Submarine Selection: " + GameMain.Server.SubSelectionMode.ToString(), Color.Cyan);

            DebugConsole.NewMessage(
                "Default Gamemode: " + GameMain.NetLobbyScreen.SelectedModeName +
                ", Default Mission Type: " + GameMain.NetLobbyScreen.MissionTypeName, Color.Cyan);

            DebugConsole.NewMessage("TraitorsEnabled: " + GameMain.Server.TraitorsEnabled.ToString(), Color.Cyan);

            DebugConsole.NewMessage(" ", Color.Cyan);

            if (!startcampaign)
            {
                DebugConsole.NewMessage("Starting with Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                DebugConsole.NewMessage("On submarine: " + GameMain.NetLobbyScreen.SelectedSub.Name, Color.Cyan);
                DebugConsole.NewMessage("Using respawn shuttle: " + GameMain.NetLobbyScreen.SelectedShuttle.Name, Color.Cyan);
            }
            else
            {
                if (GameMain.NilMod.CampaignSaveName != "")
                {
                    MultiplayerCampaign.StartCampaignSetup(true);
                }
                else
                {
                    DebugConsole.NewMessage("Nilmod default campaign savefile not specified. Please setup the campaign or specify a filename in nilmodsettings.xml", Color.Cyan);
                    MultiplayerCampaign.StartCampaignSetup(false);
                }
                DebugConsole.NewMessage(" ", Color.Cyan);
            }
        }
    }
}
