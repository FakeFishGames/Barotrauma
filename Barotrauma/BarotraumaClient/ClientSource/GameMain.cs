using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Steam;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using GameAnalyticsSDK.Net;
using Barotrauma.IO;
using System.Threading;
using Barotrauma.Tutorials;
using Barotrauma.Media;
using Barotrauma.Extensions;
using System.Threading.Tasks;

namespace Barotrauma
{
    class GameMain : Game
    {
        public static bool ShowFPS = false;
        public static bool ShowPerf = false;
        public static bool DebugDraw;
        public static bool IsSingleplayer => NetworkMember == null;
        public static bool IsMultiplayer => NetworkMember != null;

        public static PerformanceCounter PerformanceCounter;

        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static string[] ConsoleArguments;

        public static GameScreen GameScreen;
        public static MainMenuScreen MainMenuScreen;

        public static NetLobbyScreen NetLobbyScreen;
        public static ServerListScreen ServerListScreen;
        public static SteamWorkshopScreen SteamWorkshopScreen;

        public static SubEditorScreen SubEditorScreen;
        public static TestScreen TestScreen;
        public static ParticleEditorScreen ParticleEditorScreen;
        public static LevelEditorScreen LevelEditorScreen;
        public static SpriteEditorScreen SpriteEditorScreen;
        public static EventEditorScreen EventEditorScreen;
        public static CharacterEditor.CharacterEditorScreen CharacterEditorScreen;

        public static CampaignEndScreen CampaignEndScreen;

        public static Lights.LightManager LightManager;

        public static Sounds.SoundManager SoundManager;

        public static Thread MainThread { get; private set; }

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

        private static GameSession gameSession;
        public static GameSession GameSession
        {
            get { return gameSession; }
            set
            {
                if (gameSession == value) { return; }
                if (gameSession?.GameMode != null && gameSession.GameMode != value?.GameMode)
                {
                    gameSession.GameMode.Remove();
                }
                gameSession = value;
            }
        }

        public static ParticleManager ParticleManager;
        public static DecalManager DecalManager;

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

        public static LoadingScreen TitleScreen;
        private bool loadingScreenOpen;

        public static GameSettings Config;

        private CoroutineHandle loadingCoroutine;
        private bool hasLoaded;

        private readonly GameTime fixedTime;

        public string ConnectName;
        public string ConnectEndpoint;
        public UInt64 ConnectLobby;

        private static SpriteBatch spriteBatch;

        private Viewport defaultViewport;

        public event Action ResolutionChanged;

        private bool exiting;

        public static GameMain Instance
        {
            get;
            private set;
        }

        public static GraphicsDeviceManager GraphicsDeviceManager
        {
            get;
            private set;
        }

        public static WindowMode WindowMode
        {
            get;
            private set;
        }

        public static int GraphicsWidth
        {
            get;
            private set;
        }

        public static int GraphicsHeight
        {
            get;
            private set;
        }

        public static bool WindowActive
        {
            get
            {
                try
                {
                    return Instance != null && !Instance.exiting && Instance.IsActive;
                }
                catch (NullReferenceException)
                {
                    return false;
                }
            }
        }

        public static GameClient Client;
        public static NetworkMember NetworkMember
        {
            get { return Client; }
        }

        public static RasterizerState ScissorTestEnable
        {
            get;
            private set;
        }

        public bool LoadingScreenOpen
        {
            get { return loadingScreenOpen; }
        }

        public bool Paused
        {
            get; private set;
        }

        private const GraphicsProfile GfxProfile = GraphicsProfile.Reach;

#if DEBUG
        public static bool FirstLoad = true;

        public static bool CancelQuickStart;
#endif

        public GameMain(string[] args)
        {
            Content.RootDirectory = "Content";
#if DEBUG && WINDOWS
            GraphicsAdapter.UseDebugLayers = true;
#endif
            GraphicsDeviceManager = new GraphicsDeviceManager(this)
            {
                IsFullScreen = false,
                GraphicsProfile = GfxProfile
            };
            GraphicsDeviceManager.ApplyChanges();

            Window.Title = "Barotrauma";

            Instance = this;

            if (!Directory.Exists(Content.RootDirectory))
            {
                throw new Exception("Content folder not found. If you are trying to compile the game from the source code and own a legal copy of the game, you can copy the Content folder from the game's files to BarotraumaShared/Content.");
            }

            Config = new GameSettings();

            Md5Hash.LoadCache();

            ConsoleArguments = args;

            ConnectName = null;
            ConnectEndpoint = null;
            ConnectLobby = 0;

            try
            {
                ToolBox.ParseConnectCommand(ConsoleArguments, out ConnectName, out ConnectEndpoint, out ConnectLobby);
            }
            catch (IndexOutOfRangeException e)
            {
                DebugConsole.ThrowError($"Failed to parse console arguments ({string.Join(' ', ConsoleArguments)})", e);
                ConnectName = null;
                ConnectEndpoint = null;
                ConnectLobby = 0;
            }

            GUI.KeyboardDispatcher = new EventInput.KeyboardDispatcher(Window);

            PerformanceCounter = new PerformanceCounter();

            IsFixedTimeStep = false;

            GameMain.ResetFrameTime();
            fixedTime = new GameTime();

            FarseerPhysics.Settings.AllowSleep = true;
            FarseerPhysics.Settings.ContinuousPhysics = false;
            FarseerPhysics.Settings.VelocityIterations = 1;
            FarseerPhysics.Settings.PositionIterations = 1;

            MainThread = Thread.CurrentThread;

            Window.FileDropped += OnFileDropped;
        }

        public static void OnFileDropped(object sender, FileDropEventArgs args)
        {
            if (!(Screen.Selected is { } screen)) { return; }

            string filePath = args.FilePath;
            if (string.IsNullOrWhiteSpace(filePath)) { return; }

            string extension = Path.GetExtension(filePath).ToLower();

            System.IO.FileInfo info = new System.IO.FileInfo(args.FilePath);
            if (!info.Exists) { return; }

            screen.OnFileDropped(filePath, extension);
        }

        public void ApplyGraphicsSettings()
        {
            GraphicsWidth = Config.GraphicsWidth;
            GraphicsHeight = Config.GraphicsHeight;
            switch (Config.WindowMode)
            {
                case WindowMode.BorderlessWindowed:
                    GraphicsWidth = GraphicsDevice.DisplayMode.Width;
                    GraphicsHeight = GraphicsDevice.DisplayMode.Height;
                    break;
                case WindowMode.Windowed:
                    GraphicsWidth = Math.Min(GraphicsDevice.DisplayMode.Width, GraphicsWidth);
                    GraphicsHeight = Math.Min(GraphicsDevice.DisplayMode.Height, GraphicsHeight);
                    break;
            }
            GraphicsDeviceManager.GraphicsProfile = GfxProfile;
            GraphicsDeviceManager.PreferredBackBufferFormat = SurfaceFormat.Color;
            GraphicsDeviceManager.PreferMultiSampling = false;
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = Config.VSyncEnabled;
            SetWindowMode(Config.WindowMode);

            defaultViewport = GraphicsDevice.Viewport;

            ResolutionChanged?.Invoke();
        }

        public void SetWindowMode(WindowMode windowMode)
        {
            WindowMode = windowMode;
            GraphicsDeviceManager.HardwareModeSwitch = Config.WindowMode != WindowMode.BorderlessWindowed;
            GraphicsDeviceManager.IsFullScreen = Config.WindowMode == WindowMode.Fullscreen || Config.WindowMode == WindowMode.BorderlessWindowed;
            Window.IsBorderless = !GraphicsDeviceManager.HardwareModeSwitch;

            GraphicsDeviceManager.PreferredBackBufferWidth = GraphicsWidth;
            GraphicsDeviceManager.PreferredBackBufferHeight = GraphicsHeight;

            GraphicsDeviceManager.ApplyChanges();

            if (windowMode == WindowMode.BorderlessWindowed)
            {
                GraphicsWidth = GraphicsDevice.PresentationParameters.Bounds.Width;
                GraphicsHeight = GraphicsDevice.PresentationParameters.Bounds.Height;
                GraphicsDevice.Viewport = new Viewport(0,0,GraphicsWidth,GraphicsHeight);
                GraphicsDevice.ScissorRectangle = new Rectangle(0,0,GraphicsWidth,GraphicsHeight);
                GraphicsDeviceManager.PreferredBackBufferWidth = GraphicsWidth;
                GraphicsDeviceManager.PreferredBackBufferHeight = GraphicsHeight;

                GraphicsDeviceManager.ApplyChanges();
            }
        }

        public void ResetViewPort()
        {
            GraphicsDevice.Viewport = defaultViewport;
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            ApplyGraphicsSettings();

            ScissorTestEnable = new RasterizerState() { ScissorTestEnable = true };

            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Character));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Item));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Items.Components.ItemComponent));
            Hyper.ComponentModel.HyperTypeDescriptionProvider.Add(typeof(Hull));
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            GraphicsWidth = GraphicsDevice.Viewport.Width;
            GraphicsHeight = GraphicsDevice.Viewport.Height;

            ApplyGraphicsSettings();

            ConvertUnits.SetDisplayUnitToSimUnitRatio(Physics.DisplayToSimRation);

            spriteBatch = new SpriteBatch(GraphicsDevice);
            TextureLoader.Init(GraphicsDevice);

            //do this here because we need it for the loading screen
            WaterRenderer.Instance = new WaterRenderer(base.GraphicsDevice, Content);

            Quad.Init(GraphicsDevice);

            loadingScreenOpen = true;
            TitleScreen = new LoadingScreen(GraphicsDevice)
            {
                WaitForLanguageSelection = Config.ShowLanguageSelectionPrompt
            };

            bool canLoadInSeparateThread = true;

            loadingCoroutine = CoroutineManager.StartCoroutine(Load(canLoadInSeparateThread), "Load", canLoadInSeparateThread);
        }

        private void InitUserStats()
        {
            return;

            if (GameSettings.ShowUserStatisticsPrompt)
            {
                if (TextManager.ContainsTag("statisticspromptheader") && TextManager.ContainsTag("statisticsprompttext"))
                {
                    var userStatsPrompt = new GUIMessageBox(
                        TextManager.Get("statisticspromptheader"),
                        TextManager.Get("statisticsprompttext"),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    userStatsPrompt.Buttons[0].OnClicked += (btn, userdata) =>
                    {
                        GameSettings.ShowUserStatisticsPrompt = false;
                        GameSettings.SendUserStatistics = true;
                        GameAnalyticsManager.Init();
                        Config.SaveNewPlayerConfig();
                        return true;
                    };
                    userStatsPrompt.Buttons[0].OnClicked += userStatsPrompt.Close;
                    userStatsPrompt.Buttons[1].OnClicked += (btn, userdata) =>
                    {
                        GameSettings.ShowUserStatisticsPrompt = false;
                        GameSettings.SendUserStatistics = false;
                        Config.SaveNewPlayerConfig();
                        return true;
                    };
                    userStatsPrompt.Buttons[1].OnClicked += userStatsPrompt.Close;
                }
                else
                {
                    //user statistics enabled by default if the prompt cannot be shown in the user's language
                    GameSettings.ShowUserStatisticsPrompt = false;
                    GameSettings.SendUserStatistics = true;
                    GameAnalyticsManager.Init();
                    Config.SaveNewPlayerConfig();
                }
            }
            else if (GameSettings.SendUserStatistics)
            {
                GameAnalyticsManager.Init();
            }
        }

        public class LoadingException : Exception
        {
            public LoadingException(Exception e) : base("Loading was interrupted due to an error.", innerException: e)
            {
            }
        }

        private IEnumerable<object> Load(bool isSeparateThread)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("LOADING COROUTINE", Color.Lime);
            }

            while (TitleScreen.WaitForLanguageSelection)
            {
                yield return CoroutineStatus.Running;
            }

            SoundManager = new Sounds.SoundManager();
            SoundManager.SetCategoryGainMultiplier("default", Config.SoundVolume, 0);
            SoundManager.SetCategoryGainMultiplier("ui", Config.SoundVolume, 0);
            SoundManager.SetCategoryGainMultiplier("waterambience", Config.SoundVolume, 0);
            SoundManager.SetCategoryGainMultiplier("music", Config.MusicVolume, 0);
            SoundManager.SetCategoryGainMultiplier("voip", Math.Min(Config.VoiceChatVolume, 1.0f), 0);

            if (Config.EnableSplashScreen && !ConsoleArguments.Contains("-skipintro"))
            {
                var pendingSplashScreens = TitleScreen.PendingSplashScreens;
                float baseVolume = MathHelper.Clamp(Config.SoundVolume * 2.0f, 0.0f, 1.0f);
                pendingSplashScreens?.Enqueue(new LoadingScreen.PendingSplashScreen("Content/SplashScreens/Splash_UTG.webm", baseVolume * 0.5f));
                pendingSplashScreens?.Enqueue(new LoadingScreen.PendingSplashScreen("Content/SplashScreens/Splash_FF.webm", baseVolume));
                pendingSplashScreens?.Enqueue(new LoadingScreen.PendingSplashScreen("Content/SplashScreens/Splash_Daedalic.webm", baseVolume * 0.1f));
            }

            //if not loading in a separate thread, wait for the splash screens to finish before continuing the loading
            //otherwise the videos will look extremely choppy
            if (!isSeparateThread)
            {
                while (TitleScreen.PlayingSplashScreen || TitleScreen.PendingSplashScreens.Count > 0)
                {
                    yield return CoroutineStatus.Running;
                }
            }

            GUI.Init(Window, Config.AllEnabledPackages, GraphicsDevice);
            DebugConsole.Init();

            if (Config.AutoUpdateWorkshopItems)
            {
                Config.WaitingForAutoUpdate = true;
                TaskPool.Add("AutoUpdateWorkshopItemsAsync",
                    SteamManager.AutoUpdateWorkshopItemsAsync(), (task) =>
                {
                    bool result = ((Task<bool>)task).Result;

                    Config.WaitingForAutoUpdate = false;
                });

                while (Config.WaitingForAutoUpdate) { yield return CoroutineStatus.Running; }
            }

#if DEBUG
            if (Config.ModBreakerMode)
            {
                Config.SelectCorePackage(ContentPackage.CorePackages.GetRandom());
                foreach (var regularPackage in ContentPackage.RegularPackages)
                {
                    if (Rand.Range(0.0, 1.0) <= 0.5)
                    {
                        Config.EnableRegularPackage(regularPackage);
                    }
                    else
                    {
                        Config.DisableRegularPackage(regularPackage);
                    }
                }
                ContentPackage.SortContentPackages(p =>
                {
                    return Rand.Int(int.MaxValue);
                });
            }
#endif

            if (Config.AllEnabledPackages.None())
            {
                DebugConsole.Log("No content packages selected");
            }
            else
            {
                DebugConsole.Log("Selected content packages: " + string.Join(", ", Config.AllEnabledPackages.Select(cp => cp.Name)));
            }

#if DEBUG
            GameSettings.ShowUserStatisticsPrompt = false;
            GameSettings.SendUserStatistics = false;
#endif

            InitUserStats();

        yield return CoroutineStatus.Running;

            Debug.WriteLine("sounds");

            int i = 0;
            foreach (object crObj in SoundPlayer.Init())
            {
                CoroutineStatus status = (CoroutineStatus)crObj;
                if (status == CoroutineStatus.Success) break;

                i++;
                TitleScreen.LoadState = SoundPlayer.SoundCount == 0 ?
                    1.0f :
                    Math.Min(40.0f * i / Math.Max(SoundPlayer.SoundCount, 1), 40.0f);

                yield return CoroutineStatus.Running;
            }

            TitleScreen.LoadState = 40.0f;
        yield return CoroutineStatus.Running;

            LightManager = new Lights.LightManager(base.GraphicsDevice, Content);

            TitleScreen.LoadState = 41.0f;
        yield return CoroutineStatus.Running;

            GUI.LoadContent();
            TitleScreen.LoadState = 42.0f;

        yield return CoroutineStatus.Running;

            TaskPool.Add("InitRelayNetworkAccess", SteamManager.InitRelayNetworkAccess(), (t) => { });

            FactionPrefab.LoadFactions();
            NPCSet.LoadSets();
            CharacterPrefab.LoadAll();
            MissionPrefab.Init();
            TraitorMissionPrefab.Init();
            MapEntityPrefab.Init();
            Tutorials.Tutorial.Init();
            MapGenerationParams.Init();
            LevelGenerationParams.LoadPresets();
            CaveGenerationParams.LoadPresets();
            OutpostGenerationParams.LoadPresets();
            WreckAIConfig.LoadAll();
            EventSet.LoadPrefabs();
            ItemPrefab.LoadAll(GetFilesOfType(ContentType.Item));
            AfflictionPrefab.LoadAll(GetFilesOfType(ContentType.Afflictions));
            SkillSettings.Load(GetFilesOfType(ContentType.SkillSettings));
            TalentPrefab.LoadAll(GetFilesOfType(ContentType.Talents));
            TalentTree.LoadAll(GetFilesOfType(ContentType.TalentTrees));
            Order.Init();
            EventManagerSettings.Init();
            BallastFloraPrefab.LoadAll(GetFilesOfType(ContentType.MapCreature));
            HintManager.Init();
            TitleScreen.LoadState = 50.0f;
        yield return CoroutineStatus.Running;

            StructurePrefab.LoadAll(GetFilesOfType(ContentType.Structure));
            TitleScreen.LoadState = 55.0f;
        yield return CoroutineStatus.Running;

            UpgradePrefab.LoadAll(GetFilesOfType(ContentType.UpgradeModules));
            TitleScreen.LoadState = 56.0f;
        yield return CoroutineStatus.Running;

            JobPrefab.LoadAll(GetFilesOfType(ContentType.Jobs));
            CorpsePrefab.LoadAll(GetFilesOfType(ContentType.Corpses));

            NPCConversation.LoadAll(GetFilesOfType(ContentType.NPCConversations));

            ItemAssemblyPrefab.LoadAll();
            TitleScreen.LoadState = 60.0f;
        yield return CoroutineStatus.Running;

            GameModePreset.Init();

            SaveUtil.DeleteDownloadedSubs();
            SubmarineInfo.RefreshSavedSubs();

            TitleScreen.LoadState = 65.0f;
        yield return CoroutineStatus.Running;

            GameScreen = new GameScreen(GraphicsDeviceManager.GraphicsDevice, Content);

            TitleScreen.LoadState = 68.0f;
        yield return CoroutineStatus.Running;

            MainMenuScreen          = new MainMenuScreen(this);
            ServerListScreen        = new ServerListScreen();

            TitleScreen.LoadState = 70.0f;
        yield return CoroutineStatus.Running;

#if USE_STEAM
            SteamWorkshopScreen = new SteamWorkshopScreen();
            if (SteamManager.IsInitialized)
            {
                Steamworks.SteamFriends.OnGameRichPresenceJoinRequested += OnInvitedToGame;
                Steamworks.SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoinRequested;
            }
#endif

            SubEditorScreen         = new SubEditorScreen();
            TestScreen              = new TestScreen();

            TitleScreen.LoadState = 75.0f;
        yield return CoroutineStatus.Running;

            ParticleEditorScreen    = new ParticleEditorScreen();

            TitleScreen.LoadState = 80.0f;
        yield return CoroutineStatus.Running;

            LevelEditorScreen       = new LevelEditorScreen();
            SpriteEditorScreen      = new SpriteEditorScreen();
            EventEditorScreen       = new EventEditorScreen();
            CharacterEditorScreen   = new CharacterEditor.CharacterEditorScreen();
            CampaignEndScreen       = new CampaignEndScreen();

        yield return CoroutineStatus.Running;

            TitleScreen.LoadState = 85.0f;
            ParticleManager = new ParticleManager(GameScreen.Cam);
            ParticleManager.LoadPrefabs();
            TitleScreen.LoadState = 88.0f;
            LevelObjectPrefab.LoadAll();

            TitleScreen.LoadState = 90.0f;
        yield return CoroutineStatus.Running;

            DecalManager = new DecalManager();
            LocationType.Init();
            MainMenuScreen.Select();

            foreach (string steamError in SteamManager.InitializationErrors)
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get(steamError));
            }

            TitleScreen.LoadState = 100.0f;
            hasLoaded = true;
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("LOADING COROUTINE FINISHED", Color.Lime);
            }
        yield return CoroutineStatus.Success;

        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            TextureLoader.CancelAll();
            CoroutineManager.StopCoroutines("Load");
            Video.Close();
            VoipCapture.Instance?.Dispose();
            SoundManager?.Dispose();
            MainThread = null;
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

        public void OnInvitedToGame(Steamworks.Friend friend, string connectCommand) => OnInvitedToGame(connectCommand);

        public void OnInvitedToGame(string connectCommand)
        {
            try
            {
                ToolBox.ParseConnectCommand(ToolBox.SplitCommand(connectCommand), out ConnectName, out ConnectEndpoint, out ConnectLobby);
            }
            catch (IndexOutOfRangeException e)
            {
#if DEBUG
                DebugConsole.ThrowError($"Failed to parse a Steam friend's connect invitation command ({connectCommand})", e);
#else
                DebugConsole.Log($"Failed to parse a Steam friend's connect invitation command ({connectCommand})\n" + e.StackTrace.CleanupStackTrace());
#endif
                ConnectName = null;
                ConnectEndpoint = null;
                ConnectLobby = 0;
            }

            DebugConsole.NewMessage(ConnectName + ", " + ConnectEndpoint, Color.Yellow);
        }

        public void OnLobbyJoinRequested(Steamworks.Data.Lobby lobby, Steamworks.SteamId friendId)
        {
            SteamManager.JoinLobby(lobby.Id, true);
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            Timing.Accumulator += gameTime.ElapsedGameTime.TotalSeconds;
            int updateIterations = (int)Math.Floor(Timing.Accumulator / Timing.Step);
            if (Timing.Accumulator > Timing.Step * 6.0)
            {
                //if the game's running too slowly then we have no choice
                //but to skip a bunch of steps
                //otherwise it snowballs and becomes unplayable
                Timing.Accumulator = Timing.Step;
            }

            CrossThread.ProcessTasks();

            PlayerInput.UpdateVariable();

            if (SoundManager != null)
            {
                if (WindowActive || !Config.MuteOnFocusLost)
                {
                    SoundManager.ListenerGain = SoundManager.CompressionDynamicRangeGain;
                }
                else
                {
                    SoundManager.ListenerGain = 0.0f;
                }
            }

            while (Timing.Accumulator >= Timing.Step)
            {
                Timing.TotalTime += Timing.Step;

                Stopwatch sw = new Stopwatch();
                sw.Start();

                fixedTime.IsRunningSlowly = gameTime.IsRunningSlowly;
                TimeSpan addTime = new TimeSpan(0, 0, 0, 0, 16);
                fixedTime.ElapsedGameTime = addTime;
                fixedTime.TotalGameTime.Add(addTime);
                base.Update(fixedTime);

                PlayerInput.Update(Timing.Step);


                if (loadingScreenOpen)
                {
                    //reset accumulator if loading
                    // -> less choppy loading screens because the screen is rendered after each update
                    // -> no pause caused by leftover time in the accumulator when starting a new shift
                    ResetFrameTime();

                    if (!TitleScreen.PlayingSplashScreen)
                    {
                        SoundPlayer.Update((float)Timing.Step);
                    }

                    if (TitleScreen.LoadState >= 100.0f && !TitleScreen.PlayingSplashScreen &&
                        (!waitForKeyHit || ((PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0 || PlayerInput.PrimaryMouseButtonClicked()) && WindowActive)))
                    {
                        loadingScreenOpen = false;
                    }

#if DEBUG
                    CancelQuickStart |= PlayerInput.KeyDown(Keys.LeftShift);

                    if (TitleScreen.LoadState >= 100.0f && !TitleScreen.PlayingSplashScreen && (Config.AutomaticQuickStartEnabled || Config.AutomaticCampaignLoadEnabled || Config.TestScreenEnabled) && FirstLoad && !CancelQuickStart)
                    {
                        loadingScreenOpen = false;
                        FirstLoad = false;

                        if (Config.TestScreenEnabled)
                        {
                            TestScreen.Select();
                        }
                        else if (Config.AutomaticQuickStartEnabled)
                        {
                            MainMenuScreen.QuickStart();
                        }
                        else if (Config.AutomaticCampaignLoadEnabled)
                        {
                            IEnumerable<string> saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);

                            if (saveFiles.Count() > 0)
                            {
                                saveFiles = saveFiles.OrderBy(file => File.GetLastWriteTime(file));
                                try
                                {
                                    SaveUtil.LoadGame(saveFiles.Last());
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.ThrowError("Loading save \"" + saveFiles.Last() + "\" failed", e);
                                    return;
                                }
                            }
                        }
                    }
#endif

                    if (!hasLoaded && !CoroutineManager.IsCoroutineRunning(loadingCoroutine))
                    {
                        throw new LoadingException(loadingCoroutine.Exception);
                    }
                }
                else if (hasLoaded)
                {
                    if (ConnectLobby != 0)
                    {
                        if (Client != null)
                        {
                            Client.Disconnect();
                            Client = null;

                            GameMain.MainMenuScreen.Select();
                        }
                        Steam.SteamManager.JoinLobby(ConnectLobby, true);

                        ConnectLobby = 0;
                        ConnectEndpoint = null;
                        ConnectName = null;
                    }
                    else if (!string.IsNullOrWhiteSpace(ConnectEndpoint))
                    {
                        if (Client != null)
                        {
                            Client.Disconnect();
                            Client = null;

                            GameMain.MainMenuScreen.Select();
                        }
                        UInt64 serverSteamId = SteamManager.SteamIDStringToUInt64(ConnectEndpoint);
                        Client = new GameClient(Config.PlayerName,
                                                serverSteamId != 0 ? null : ConnectEndpoint,
                                                serverSteamId,
                                                string.IsNullOrWhiteSpace(ConnectName) ? ConnectEndpoint : ConnectName);
                        ConnectLobby = 0;
                        ConnectEndpoint = null;
                        ConnectName = null;
                    }

                    SoundPlayer.Update((float)Timing.Step);

                    if (PlayerInput.KeyHit(Keys.Escape) && WindowActive)
                    {
                        // Check if a text input is selected.
                        if (GUI.KeyboardDispatcher.Subscriber != null)
                        {
                            if (GUI.KeyboardDispatcher.Subscriber is GUITextBox textBox)
                            {
                                textBox.Deselect();
                            }
                            GUI.KeyboardDispatcher.Subscriber = null;
                        }
                        //if a verification prompt (are you sure you want to x) is open, close it
                        else if (GUIMessageBox.VisibleBox as GUIMessageBox != null &&
                                GUIMessageBox.VisibleBox.UserData as string == "verificationprompt")
                        {
                            ((GUIMessageBox)GUIMessageBox.VisibleBox).Close();
                        }
                        else if (GUIMessageBox.VisibleBox?.UserData is RoundSummary roundSummary &&
                                roundSummary.ContinueButton != null &&
                                roundSummary.ContinueButton.Visible)
                        {
                            GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
                        }
                        else if (Tutorial.Initialized && Tutorial.ContentRunning)
                        {
                            (GameSession.GameMode as TutorialMode).Tutorial.CloseActiveContentGUI();
                        }
                        else if (GameSession.IsTabMenuOpen)
                        {
                            gameSession.ToggleTabMenu();
                        }
                        else if (GUI.PauseMenuOpen)
                        {
                            GUI.TogglePauseMenu();
                        }
                        //open the pause menu if not controlling a character OR if the character has no UIs active that can be closed with ESC
                        else if ((Character.Controlled == null || !itemHudActive())
                            && CharacterHealth.OpenHealthWindow == null
                            && !CrewManager.IsCommandInterfaceOpen
                            && !(Screen.Selected is SubEditorScreen editor && !editor.WiringMode && Character.Controlled?.SelectedConstruction != null))
                        {
                            // Otherwise toggle pausing, unless another window/interface is open.
                            GUI.TogglePauseMenu();
                        }

                        static bool itemHudActive()
                        {
                            if (Character.Controlled?.SelectedConstruction == null) { return false; }
                            return
                                Character.Controlled.SelectedConstruction.ActiveHUDs.Any(ic => ic.GuiFrame != null) ||
                                ((Character.Controlled.ViewTarget as Item)?.Prefab?.FocusOnSelected ?? false);
                        }
                    }

#if DEBUG
                    if (NetworkMember == null)
                    {
                        if (PlayerInput.KeyHit(Keys.P) && !(GUI.KeyboardDispatcher.Subscriber is GUITextBox))
                        {
                            DebugConsole.Paused = !DebugConsole.Paused;
                        }
                    }
#endif

                    GUI.ClearUpdateList();
                    Paused = (DebugConsole.IsOpen || GUI.PauseMenuOpen || GUI.SettingsMenuOpen || Tutorial.ContentRunning || DebugConsole.Paused) &&
                             (NetworkMember == null || !NetworkMember.GameStarted);
                    if (GameSession?.GameMode != null && GameSession.GameMode.Paused)
                    {
                        Paused = true;
                        GameSession.GameMode.UpdateWhilePaused((float)Timing.Step);
                    }

#if !DEBUG
                    if (NetworkMember == null && !WindowActive && !Paused && true && Config.PauseOnFocusLost &&
                        Screen.Selected != MainMenuScreen && Screen.Selected != ServerListScreen && Screen.Selected != NetLobbyScreen &&
                        Screen.Selected != SubEditorScreen && Screen.Selected != LevelEditorScreen)
                    {
                        GUI.TogglePauseMenu();
                        Paused = true;
                    }
#endif

                    Screen.Selected.AddToGUIUpdateList();

                    Client?.AddToGUIUpdateList();

                    SubmarinePreview.AddToGUIUpdateList();

                    FileSelection.AddToGUIUpdateList();

                    DebugConsole.AddToGUIUpdateList();

                    DebugConsole.Update((float)Timing.Step);

                    if (!Paused)
                    {
                        Screen.Selected.Update(Timing.Step);
                    }
                    else if (Tutorial.Initialized && Tutorial.ContentRunning)
                    {
                        (GameSession.GameMode as TutorialMode).Update((float)Timing.Step);
                    }
                    else
                    {
                        if (Screen.Selected.Cam == null)
                        {
                            DebugConsole.Paused = false;
                        }
                        else
                        {
                            Screen.Selected.Cam.MoveCamera((float)Timing.Step, allowMove: DebugConsole.Paused, allowZoom: DebugConsole.Paused);
                        }
                    }

                    NetworkMember?.Update((float)Timing.Step);

                    GUI.Update((float)Timing.Step);
                }

                CoroutineManager.Update((float)Timing.Step, Paused ? 0.0f : (float)Timing.Step);

                SteamManager.Update((float)Timing.Step);

                TaskPool.Update();

                SoundManager?.Update();

                Timing.Accumulator -= Timing.Step;

                sw.Stop();
                PerformanceCounter.AddElapsedTicks("Update total", sw.ElapsedTicks);
                PerformanceCounter.UpdateTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);
                PerformanceCounter.UpdateIterationsGraph.Update(updateIterations);
            }

            if (!Paused) Timing.Alpha = Timing.Accumulator / Timing.Step;
        }

        public static void ResetFrameTime()
        {
            Timing.Accumulator = 0.0f;
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        protected override void Draw(GameTime gameTime)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            if (Timing.FrameLimit > 0)
            {
                double step = 1.0 / Timing.FrameLimit;
                while (!Config.VSyncEnabled && sw.Elapsed.TotalSeconds + deltaTime < step)
                {
                    Thread.Sleep(1);
                }
            }

            PerformanceCounter.Update(sw.Elapsed.TotalSeconds + deltaTime);

            if (loadingScreenOpen)
            {
                TitleScreen.Draw(spriteBatch, base.GraphicsDevice, (float)deltaTime);
            }
            else if (hasLoaded)
            {
                Screen.Selected.Draw(deltaTime, base.GraphicsDevice, spriteBatch);
            }

            if (DebugDraw && GUI.MouseOn != null)
            {
                spriteBatch.Begin();
                GUI.DrawRectangle(spriteBatch, GUI.MouseOn.MouseRect, Color.Lime);
                GUI.DrawRectangle(spriteBatch, GUI.MouseOn.Rect, Color.Cyan);
                spriteBatch.End();
            }

            sw.Stop();
            PerformanceCounter.AddElapsedTicks("Draw total", sw.ElapsedTicks);
            PerformanceCounter.DrawTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);
        }


        public static void QuitToMainMenu(bool save, bool showVerificationPrompt)
        {
            if (showVerificationPrompt)
            {
                string text = (Screen.Selected is CharacterEditor.CharacterEditorScreen || Screen.Selected is SubEditorScreen) ? "PauseMenuQuitVerificationEditor" : "PauseMenuQuitVerification";
                var msgBox = new GUIMessageBox("", TextManager.Get(text), new string[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
                {
                    UserData = "verificationprompt"
                };
                msgBox.Buttons[0].OnClicked = (yesBtn, userdata) =>
                {
                    QuitToMainMenu(save);
                    return true;
                };
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
        }

        public static void QuitToMainMenu(bool save)
        {
            if (save)
            {
                GUI.SetSavingIndicatorState(true);

                if (GameSession.Submarine != null && !GameSession.Submarine.Removed)
                {
                    GameSession.SubmarineInfo = new SubmarineInfo(GameSession.Submarine);
                }

                // Update store stock when saving and quitting in an outpost (normally updated when CampaignMode.End() is called)
                if (GameSession?.Campaign is SinglePlayerCampaign spCampaign && Level.IsLoadedOutpost && spCampaign.Map?.CurrentLocation != null && spCampaign.CargoManager != null)
                {
                    spCampaign.Map.CurrentLocation.AddToStock(spCampaign.CargoManager.SoldItems);
                    spCampaign.CargoManager.ClearSoldItemsProjSpecific();
                    spCampaign.Map.CurrentLocation.RemoveFromStock(spCampaign.CargoManager.PurchasedItems);
                }

                SaveUtil.SaveGame(GameSession.SavePath);
            }

            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }

            CoroutineManager.StopCoroutines("EndCinematic");

            if (GameSession != null)
            {
                if (Tutorial.Initialized)
                {
                    ((TutorialMode)GameSession.GameMode).Tutorial?.Stop();
                }
            }
            GUIMessageBox.CloseAll();
            MainMenuScreen.Select();
            GameSession = null;
        }

        public void ShowCampaignDisclaimer(Action onContinue = null)
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CampaignDisclaimerTitle"), TextManager.Get("CampaignDisclaimerText"),
                new string[] { TextManager.Get("CampaignRoadMapTitle"), TextManager.Get("OK") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                ShowOpenUrlInWebBrowserPrompt("https://trello.com/b/hBXI8ltN/barotrauma-roadmap-known-issues");
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += (_, __) => { onContinue?.Invoke(); return true; };

            Config.CampaignDisclaimerShown = true;
            Config.SaveNewPlayerConfig();
        }

        public void ShowEditorDisclaimer()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("EditorDisclaimerTitle"), TextManager.Get("EditorDisclaimerText"));
            var linkHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), msgBox.Content.RectTransform)) { Stretch = true, RelativeSpacing = 0.025f };
            linkHolder.RectTransform.MaxSize = new Point(int.MaxValue, linkHolder.Rect.Height);
            List<Pair<string, string>> links = new List<Pair<string, string>>()
            {
                new Pair<string, string>(TextManager.Get("EditorDisclaimerWikiLink"), TextManager.Get("EditorDisclaimerWikiUrl")),
                new Pair<string, string>(TextManager.Get("EditorDisclaimerDiscordLink"), TextManager.Get("EditorDisclaimerDiscordUrl")),
            };
            foreach (var link in links)
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), linkHolder.RectTransform), link.First, style: "MainMenuGUIButton", textAlignment: Alignment.Left)
                {
                    UserData = link.Second,
                    OnClicked = (btn, userdata) =>
                    {
                        ShowOpenUrlInWebBrowserPrompt(userdata as string);
                        return true;
                    }
                };
            }

            msgBox.InnerFrame.RectTransform.MinSize = new Point(0,
                msgBox.InnerFrame.Rect.Height + linkHolder.Rect.Height + msgBox.Content.AbsoluteSpacing * 2 + 10);
            Config.EditorDisclaimerShown = true;
            Config.SaveNewPlayerConfig();
        }

        public void ShowBugReporter()
        {
            if (GUIMessageBox.VisibleBox != null && GUIMessageBox.VisibleBox.UserData as string == "bugreporter")
            {
                return;
            }

            var msgBox = new GUIMessageBox(TextManager.Get("bugreportbutton"), "")
            {
                UserData = "bugreporter"
            };
            var linkHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), msgBox.Content.RectTransform)) { Stretch = true, RelativeSpacing = 0.025f };
            linkHolder.RectTransform.MaxSize = new Point(int.MaxValue, linkHolder.Rect.Height);

#if !UNSTABLE
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), linkHolder.RectTransform), TextManager.Get("bugreportfeedbackform"), style: "MainMenuGUIButton", textAlignment: Alignment.Left)
            {
                UserData = "https://steamcommunity.com/app/602960/discussions/1/",
                OnClicked = (btn, userdata) =>
                {
                    if (!SteamManager.OverlayCustomURL(userdata as string))
                    {
                        ShowOpenUrlInWebBrowserPrompt(userdata as string);
                    }
                    msgBox.Close();
                    return true;
                }
            };
#endif

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), linkHolder.RectTransform), TextManager.Get("bugreportgithubform"), style: "MainMenuGUIButton", textAlignment: Alignment.Left)
            {
#if UNSTABLE
                UserData = "https://barotraumagame.com/unstable-3rf3w5t4ter/",
#else
                UserData = "https://github.com/Regalis11/Barotrauma/issues/new?template=bug_report.md",
#endif
                OnClicked = (btn, userdata) =>
                {
                    ShowOpenUrlInWebBrowserPrompt(userdata as string);
                    msgBox.Close();
                    return true;
                }
            };

            msgBox.InnerFrame.RectTransform.MinSize = new Point(0,
                msgBox.InnerFrame.Rect.Height + linkHolder.Rect.Height + msgBox.Content.AbsoluteSpacing * 2 + (int)(50 * GUI.Scale));
        }

        static bool waitForKeyHit = true;
        public CoroutineHandle ShowLoading(IEnumerable<object> loader, bool waitKeyHit = true)
        {
            waitForKeyHit = waitKeyHit;
            loadingScreenOpen = true;
            TitleScreen.LoadState = null;
            return CoroutineManager.StartCoroutine(TitleScreen.DoLoading(loader));
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            exiting = true;
            DebugConsole.NewMessage("Exiting...");
            NetworkMember?.Disconnect();
            SteamManager.ShutDown();

            try
            {
                SaveUtil.CleanUnnecessarySaveFiles();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while cleaning unnecessary save files", e);
            }

            if (GameSettings.SendUserStatistics) { GameAnalytics.OnQuit(); }
            if (GameSettings.SaveDebugConsoleLogs) { DebugConsole.SaveLogs(); }

            base.OnExiting(sender, args);
        }

        public void ShowOpenUrlInWebBrowserPrompt(string url, string promptExtensionTag = null)
        {
            if (string.IsNullOrEmpty(url)) { return; }
            if (GUIMessageBox.VisibleBox?.UserData as string == "verificationprompt") { return; }

            string text = TextManager.GetWithVariable("openlinkinbrowserprompt", "[link]", url);
            string extensionText = TextManager.Get(promptExtensionTag, returnNull: true, useEnglishAsFallBack: false);
            if (!string.IsNullOrEmpty(extensionText))
            {
                text += $"\n\n{extensionText}";
            }

            var msgBox = new GUIMessageBox("", text, new string[] { TextManager.Get("Yes"), TextManager.Get("No") })
            {
                UserData = "verificationprompt"
            };
            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                ToolBox.OpenFileWithShell(url);
                msgBox.Close();
                return true;
            };
            msgBox.Buttons[1].OnClicked = msgBox.Close;
        }
    }
}
