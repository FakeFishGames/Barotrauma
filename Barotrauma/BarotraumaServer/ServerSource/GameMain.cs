using Barotrauma.Networking;
using Barotrauma.Steam;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    class GameMain
    {
        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;


        private static World world;
        public static World World
        {
            get
            {
                if (world == null) { world = new World(new Vector2(0, -9.82f)); }
                return world;
            }
            set { world = value; }
        }
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

        public static Thread MainThread { get; private set; }

        //only screens the server implements
        public static GameScreen GameScreen;
        public static NetLobbyScreen NetLobbyScreen;

        //null screens because they are not implemented by the server,
        //but they're checked for all over the place
        //TODO: maybe clean up instead of having these constants
        public static readonly Screen SubEditorScreen = UnimplementedScreen.Instance;

        public static DecalManager DecalManager;

        public static bool ShouldRun = true;

        private static Stopwatch stopwatch;

        private static ContentPackage vanillaContent;
        public static ContentPackage VanillaContent
        {
            get
            {
                if (vanillaContent == null)
                {
                    // TODO: Dynamic method for defining and finding the vanilla content package.
                    vanillaContent = ContentPackage.CorePackages.SingleOrDefault(cp => Path.GetFileName(cp.Path).Equals("vanilla 0.9.xml", StringComparison.OrdinalIgnoreCase));
                }
                return vanillaContent;
            }
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

            Console.WriteLine("Loading game settings");
            Config = new GameSettings();

            Console.WriteLine("Loading MD5 hash cache");
            Md5Hash.LoadCache();

            Console.WriteLine("Initializing SteamManager");
            SteamManager.Initialize();
            //TODO: figure out how consent is supposed to work for servers
            //Console.WriteLine("Initializing GameAnalytics");
            //GameAnalyticsManager.InitIfConsented();

            Console.WriteLine("Initializing GameScreen");
            GameScreen = new GameScreen();

            MainThread = Thread.CurrentThread;
        }

        public void Init()
        {
            NPCSet.LoadSets();
            FactionPrefab.LoadFactions();
            CharacterPrefab.LoadAll();
            MissionPrefab.Init();
            TraitorMissionPrefab.Init();
            MapEntityPrefab.Init();
            MapGenerationParams.Init();
            LevelGenerationParams.LoadPresets();
            CaveGenerationParams.LoadPresets();
            OutpostGenerationParams.LoadPresets();
            EventSet.LoadPrefabs();
            Order.Init();
            EventManagerSettings.Init();
            ItemPrefab.LoadAll(GetFilesOfType(ContentType.Item));
            AfflictionPrefab.LoadAll(GetFilesOfType(ContentType.Afflictions));
            SkillSettings.Load(GetFilesOfType(ContentType.SkillSettings));
            StructurePrefab.LoadAll(GetFilesOfType(ContentType.Structure));
            UpgradePrefab.LoadAll(GetFilesOfType(ContentType.UpgradeModules));
            JobPrefab.LoadAll(GetFilesOfType(ContentType.Jobs));
            CorpsePrefab.LoadAll(GetFilesOfType(ContentType.Corpses));
            NPCConversation.LoadAll(GetFilesOfType(ContentType.NPCConversations));
            ItemAssemblyPrefab.LoadAll();
            LevelObjectPrefab.LoadAll();
            BallastFloraPrefab.LoadAll(GetFilesOfType(ContentType.MapCreature));
            TalentPrefab.LoadAll(GetFilesOfType(ContentType.Talents));
            TalentTree.LoadAll(GetFilesOfType(ContentType.TalentTrees));

            GameModePreset.Init();
            DecalManager = new DecalManager();
            LocationType.Init();

            SubmarineInfo.RefreshSavedSubs();

            Screen.SelectNull();

            NetLobbyScreen = new NetLobbyScreen();

            CheckContentPackage();
        }


        private void CheckContentPackage()
        {
            //TODO: reimplement using only core package?
            /*foreach (ContentPackage contentPackage in Config.AllEnabledPackages)
            {
                var exePaths = contentPackage.GetFilesOfType(ContentType.ServerExecutable);
                if (exePaths.Count() > 0 && AppDomain.CurrentDomain.FriendlyName != exePaths.First())
                {
                    DebugConsole.NewMessage(AppDomain.CurrentDomain.FriendlyName);
                    DebugConsole.ShowQuestionPrompt(TextManager.GetWithVariables("IncorrectExe", new string[2] { "[selectedpackage]", "[exename]" }, new string[2] { contentPackage.Name, exePaths.First() }),
                        (option) =>
                        {
                            if (option.ToLower() == "y" || option.ToLower() == "yes")
                            {
                                string fullPath = Path.GetFullPath(exePaths.First());
                                ToolBox.OpenFileWithShell(fullPath);
                                ShouldRun = false;
                            }
                        });
                    break;
                }
            }*/
        }

        /// <summary>
        /// Returns the file paths of all files of the given type in the content packages.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="searchAllContentPackages">If true, also returns files in content packages that are installed but not currently selected.</param>
        public IEnumerable<ContentFile> GetFilesOfType(ContentType type, bool searchAllContentPackages = false)
        {
            if (searchAllContentPackages)
            {
                return ContentPackage.GetFilesOfType(ContentPackage.AllPackages, type);
            }
            else
            {
                return ContentPackage.GetFilesOfType(Config.AllEnabledPackages, type);
            }
        }

        public bool TryStartChildServerRelay()
        {            
            for (int i = 0; i < CommandLineArgs.Length; i++)
            {
                switch (CommandLineArgs[i].Trim())
                {
                    case "-pipes":
                        ChildServerRelay.Start(CommandLineArgs[i + 2], CommandLineArgs[i + 1]);
                        return true;
                }
            }
            return false;
        }

        public void StartServer()
        {
            string name = "Server";
            int port = NetConfig.DefaultPort;
            int queryPort = NetConfig.DefaultQueryPort;
            bool publiclyVisible = false;
            string password = "";
            bool enableUpnp = false;

            int maxPlayers = 10;
            int? ownerKey = null;
            UInt64 steamId = 0;

            XDocument doc = XMLExtensions.TryLoadXml(ServerSettings.SettingsFile);
            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("File \"" + ServerSettings.SettingsFile + "\" not found. Starting the server with default settings.");
            }
            else
            {
                name = doc.Root.GetAttributeString("name", "Server");
                port = doc.Root.GetAttributeInt("port", NetConfig.DefaultPort);
                queryPort = doc.Root.GetAttributeInt("queryport", NetConfig.DefaultQueryPort);
                publiclyVisible = doc.Root.GetAttributeBool("public", false);
                password = doc.Root.GetAttributeString("password", "");
                enableUpnp = doc.Root.GetAttributeBool("enableupnp", false);
                maxPlayers = doc.Root.GetAttributeInt("maxplayers", 10);
                ownerKey = null;
            }
            
#if DEBUG
            foreach (string s in CommandLineArgs)
            {
                Console.WriteLine(s);
            }
#endif

            for (int i = 0; i < CommandLineArgs.Length; i++)
            {
                switch (CommandLineArgs[i].Trim())
                {
                    case "-name":
                        name = CommandLineArgs[i + 1];
                        i++;
                        break;
                    case "-port":
                        int.TryParse(CommandLineArgs[i + 1], out port);
                        i++;
                        break;
                    case "-queryport":
                        int.TryParse(CommandLineArgs[i + 1], out queryPort);
                        i++;
                        break;
                    case "-public":
                        bool.TryParse(CommandLineArgs[i + 1], out publiclyVisible);
                        i++;
                        break;
                    case "-password":
                        password = CommandLineArgs[i + 1];
                        i++;
                        break;
                    case "-nopassword":
                        password = "";
                        break;
                    case "-upnp":
                    case "-enableupnp":
                        bool.TryParse(CommandLineArgs[i + 1], out enableUpnp);
                        i++;
                        break;
                    case "-maxplayers":
                        int.TryParse(CommandLineArgs[i + 1], out maxPlayers);
                        i++;
                        break;
                    case "-ownerkey":
                        if (int.TryParse(CommandLineArgs[i + 1], out int key))
                        {
                            ownerKey = key;
                        }
                        i++;
                        break;
                    case "-steamid":
                        UInt64.TryParse(CommandLineArgs[i + 1], out steamId);
                        i++;
                        break;
                    case "-pipes":
                        //handled in TryStartChildServerRelay
                        i += 2;
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
                ownerKey,
                steamId);

            for (int i = 0; i < CommandLineArgs.Length; i++)
            {
                switch (CommandLineArgs[i].Trim())
                {
                    case "-playstyle":
                        Enum.TryParse(CommandLineArgs[i + 1], out PlayStyle playStyle);
                        Server.ServerSettings.PlayStyle = playStyle;
                        i++;
                        break;
                    case "-banafterwrongpassword":
                        bool.TryParse(CommandLineArgs[i + 1], out bool banAfterWrongPassword);
                        Server.ServerSettings.BanAfterWrongPassword = banAfterWrongPassword;
                        break;
                    case "-karma":
                    case "-karmaenabled":
                        bool.TryParse(CommandLineArgs[i + 1], out bool karmaEnabled);
                        Server.ServerSettings.KarmaEnabled = karmaEnabled;
                        i++;
                        break;
                    case "-karmapreset":
                        string karmaPresetName = CommandLineArgs[i + 1];
                        Server.ServerSettings.KarmaPreset = karmaPresetName;
                        i++;
                        break;
                }
            }
        }

        public void CloseServer()
        {
            Server?.Disconnect();
            ShouldRun = false;
            Server = null;
        }

        public void Run()
        {
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Character));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Item));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Items.Components.ItemComponent));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Hull));

            TryStartChildServerRelay();
            Init();
            StartServer();

            ResetFrameTime();

            double frequency = (double)Stopwatch.Frequency;
            if (frequency <= 1500)
            {
                DebugConsole.NewMessage("WARNING: Stopwatch frequency under 1500 ticks per second. Expect significant syncing accuracy issues.", Color.Yellow);
            }

            stopwatch = Stopwatch.StartNew();
            long prevTicks = stopwatch.ElapsedTicks;
            while (ShouldRun)
            {
                long currTicks = stopwatch.ElapsedTicks;
                double elapsedTime = Math.Max(currTicks - prevTicks, 0) / frequency;
                Timing.Accumulator += elapsedTime;
                if (Timing.Accumulator > 1.0)
                {
                    //prevent spiral of death
                    Timing.Accumulator = Timing.Step;
                }
                prevTicks = currTicks;
                while (Timing.Accumulator >= Timing.Step)
                {
                    Timing.TotalTime += Timing.Step;
                    DebugConsole.Update();
                    if (GameSession?.GameMode == null || !GameSession.GameMode.Paused)
                    {
                        Screen.Selected?.Update((float)Timing.Step);
                    }
                    Server.Update((float)Timing.Step);
                    if (Server == null) { break; }
                    SteamManager.Update((float)Timing.Step);
                    TaskPool.Update();
                    CoroutineManager.Update((float)Timing.Step, (float)Timing.Step);

                    Timing.Accumulator -= Timing.Step;
                }

#if !DEBUG
                if (Server?.OwnerConnection == null)
                {
                    DebugConsole.UpdateCommandLine((int)(Timing.Accumulator * 800));
                }
                else
                {
                    DebugConsole.Clear();
                }
#else
                DebugConsole.UpdateCommandLine((int)(Timing.Accumulator * 800));
#endif

                int frameTime = (int)(((double)(stopwatch.ElapsedTicks - prevTicks) / frequency) * 1000.0);
                frameTime = Math.Max(0, frameTime);
                
                Thread.Sleep(Math.Max(((int)(Timing.Step * 1000.0) - frameTime) / 2, 0));
            }
            stopwatch.Stop();

            CloseServer();

            SteamManager.ShutDown();

            SaveUtil.CleanUnnecessarySaveFiles();

            if (GameSettings.SaveDebugConsoleLogs || GameSettings.VerboseLogging) { DebugConsole.SaveLogs(); }
            if (GameAnalyticsManager.SendUserStatistics) { GameAnalyticsManager.ShutDown(); }

            MainThread = null;
        }

        public static void ResetFrameTime()
        {
            Timing.Accumulator = 0.0f;
            stopwatch?.Reset();
            stopwatch?.Start();
        }
        
        public CoroutineHandle ShowLoading(IEnumerable<CoroutineStatus> loader, bool waitKeyHit = true)
        {
            return CoroutineManager.StartCoroutine(loader);
        }

        public void Exit()
        {
            ShouldRun = false;
        }
    }
}
