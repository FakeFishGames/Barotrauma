using Barotrauma.Networking;
using Barotrauma.Steam;
using FarseerPhysics.Dynamics;
using GameAnalyticsSDK.Net;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        //only screens the server implements
        public static GameScreen GameScreen;
        public static NetLobbyScreen NetLobbyScreen;

        //null screens because they are not implemented by the server,
        //but they're checked for all over the place
        //TODO: maybe clean up instead of having these constants
        public static readonly Screen SubEditorScreen = UnimplementedScreen.Instance;
        
        public static bool ShouldRun = true;

        public static HashSet<ContentPackage> SelectedPackages
        {
            get { return Config.SelectedContentPackages; }
        }

        public readonly string[] CommandLineArgs;

        public GameMain(string[] args)
        {
            Instance = this;

            CommandLineArgs = args;

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
                Config.Save();
            }

            TextManager.LoadTextPacks(Path.Combine("Content", "Texts"));

            SteamManager.Initialize();
            if (GameSettings.SendUserStatistics) GameAnalyticsManager.Init();            
            
            GameScreen = new GameScreen();
        }

        public void Init()
        {
            MissionPrefab.Init();
            MapEntityPrefab.Init();
            MapGenerationParams.Init();
            LevelGenerationParams.LoadPresets();
            ScriptedEventSet.LoadPrefabs();

            StructurePrefab.LoadAll(GetFilesOfType(ContentType.Structure));
            ItemPrefab.LoadAll(GetFilesOfType(ContentType.Item));
            JobPrefab.LoadAll(GetFilesOfType(ContentType.Jobs));
            NPCConversation.LoadAll(GetFilesOfType(ContentType.NPCConversations));
            LevelObjectPrefab.LoadAll();
            AfflictionPrefab.LoadAll(GetFilesOfType(ContentType.Afflictions));

            GameModePreset.Init();
            LocationType.Init();

            Submarine.RefreshSavedSubs();

            Screen.SelectNull();

            NetLobbyScreen = new NetLobbyScreen();

            CheckContentPackage();
        }


        private void CheckContentPackage()
        {

            foreach (ContentPackage contentPackage in Config.SelectedContentPackages)
            {
                var exePaths = contentPackage.GetFilesOfType(ContentType.Executable);
                if (exePaths.Count() > 0 && AppDomain.CurrentDomain.FriendlyName != exePaths.First())
                {
                    DebugConsole.ShowQuestionPrompt(TextManager.Get("IncorrectExe")
                            .Replace("[selectedpackage]", contentPackage.Name)
                            .Replace("[exename]", exePaths.First()),
                        (option) =>
                        {
                            if (option.ToLower() == "y" || option.ToLower() == "yes")
                            {
                                string fullPath = Path.GetFullPath(exePaths.First());
                                Process.Start(fullPath);
                                ShouldRun = false;
                            }
                        });
                    break;
                }
            }
        }

        /// <summary>
        /// Returns the file paths of all files of the given type in the currently selected content packages.
        /// </summary>
        public IEnumerable<string> GetFilesOfType(ContentType type)
        {
            return ContentPackage.GetFilesOfType(SelectedPackages, type);
        }

        public void StartServer()
        {
            XDocument doc = XMLExtensions.TryLoadXml(ServerSettings.SettingsFile);
            if (doc == null)
            {
                DebugConsole.ThrowError("File \"" + ServerSettings.SettingsFile + "\" not found. Starting the server with default settings.");
                Server = new GameServer("Server", NetConfig.DefaultPort, NetConfig.DefaultQueryPort, false, "", false, 10);
                return;
            }

            string name = doc.Root.GetAttributeString("name", "Server");
            int port = doc.Root.GetAttributeInt("port", NetConfig.DefaultPort);
            int queryPort = doc.Root.GetAttributeInt("queryport", NetConfig.DefaultQueryPort);
            bool publiclyVisible = doc.Root.GetAttributeBool("public", false);
            string password = doc.Root.GetAttributeString("password", "");
            bool enableUpnp = doc.Root.GetAttributeBool("enableupnp", false);
            int maxPlayers = doc.Root.GetAttributeInt("maxplayers", 10);
            int ownerKey = 0;
            
            int tempInt;
            bool tempBool;

            for (int i=0;i<CommandLineArgs.Length;i++)
            {
                switch (CommandLineArgs[i].Trim())
                {
                    case "-name":
                        name = CommandLineArgs[i + 1];
                        i++;
                        break;
                    case "-port":
                        if (int.TryParse(CommandLineArgs[i + 1], out tempInt))
                        {
                            port = tempInt;
                        }
                        i++;
                        break;
                    case "-queryport":
                        if (int.TryParse(CommandLineArgs[i + 1], out tempInt))
                        {
                            queryPort = tempInt;
                        }
                        i++;
                        break;
                    case "-public":
                        if (bool.TryParse(CommandLineArgs[i + 1], out tempBool))
                        {
                            publiclyVisible = tempBool;
                        }
                        i++;
                        break;
                    case "-password":
                        password = CommandLineArgs[i + 1];
                        i++;
                        break;
                    case "-upnp":
                    case "-enableupnp":
                        if (bool.TryParse(CommandLineArgs[i + 1], out tempBool))
                        {
                            enableUpnp = tempBool;
                        }
                        i++;
                        break;
                    case "-maxplayers":
                        if (int.TryParse(CommandLineArgs[i + 1], out tempInt))
                        {
                            maxPlayers = tempInt;
                        }
                        i++;
                        break;
                    case "-ownerkey":
                        if (int.TryParse(CommandLineArgs[i + 1], out tempInt))
                        {
                            ownerKey = tempInt;
                        }
                        i++;
                        break;
                }
            }

            Server = new GameServer(
                name,
                port,
                queryPort,
                publiclyVisible,
                password,
                enableUpnp,
                maxPlayers,
                ownerKey);
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
                double elapsedTime = (currTicks - prevTicks) / frequency;
                Timing.Accumulator += elapsedTime;
                Timing.TotalTime += elapsedTime;
                prevTicks = currTicks;
                while (Timing.Accumulator >= Timing.Step)
                {
                    DebugConsole.Update();
                    if (Screen.Selected != null) Screen.Selected.Update((float)Timing.Step);
                    Server.Update((float)Timing.Step);
                    CoroutineManager.Update((float)Timing.Step, (float)Timing.Step);

                    Timing.Accumulator -= Timing.Step;
                }
                int frameTime = (int)(((double)(stopwatch.ElapsedTicks - prevTicks) / frequency) * 1000.0);
                Thread.Sleep(Math.Max(((int)(Timing.Step * 1000.0) - frameTime) / 2, 0));
            }
            stopwatch.Stop();

            CloseServer();
        }
        
        public CoroutineHandle ShowLoading(IEnumerable<object> loader, bool waitKeyHit = true)
        {
            return CoroutineManager.StartCoroutine(loader);
        }

        public void Exit()
        {
            ShouldRun = false;
        }
    }
}
