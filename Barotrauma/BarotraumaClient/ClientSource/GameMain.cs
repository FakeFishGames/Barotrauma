using Barotrauma.IO;
using Barotrauma.Media;
using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Steam;
using Barotrauma.Transition;
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
using System.Threading;

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

        private static Stopwatch performanceCounterTimer;
        private static int updateCount = 0;
        public static int CurrentUpdateRate
        {
            get; private set;
        }

        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static string[] ConsoleArguments;

        public static GameScreen GameScreen;
        public static MainMenuScreen MainMenuScreen;

        public static NetLobbyScreen NetLobbyScreen;
        public static ModDownloadScreen ModDownloadScreen;

        public static void ResetNetLobbyScreen()
        {
            NetLobbyScreen?.Release();
            NetLobbyScreen = new NetLobbyScreen();
            ModDownloadScreen?.Release();
            ModDownloadScreen = new ModDownloadScreen();
        }
        
        public static ServerListScreen ServerListScreen;

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

        public static ContentPackage VanillaContent => ContentPackageManager.VanillaCorePackage;

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

        private CoroutineHandle loadingCoroutine;
        public bool HasLoaded { get; private set; }

        private readonly GameTime fixedTime;

        public string ConnectName;
        public string ConnectEndpoint;
        public UInt64 ConnectLobby;

        private static SpriteBatch spriteBatch;

        private Viewport defaultViewport;

        public event Action ResolutionChanged;

        private bool exiting;

        public static bool IsFirstLaunch
        {
            get;
            private set;
        }

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

        public static ChatMode ActiveChatMode { get; set; } = ChatMode.Radio;

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

            GameSettings.Init();
            
            Md5Hash.Cache.Load();

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

        public void ApplyGraphicsSettings(bool recalculateFontsAndStyles = false)
        {
            static void updateConfig()
            {
                var config = GameSettings.CurrentConfig;
                config.Graphics.Width = GraphicsWidth;
                config.Graphics.Height = GraphicsHeight;
                GameSettings.SetCurrentConfig(config);
            }

            GraphicsWidth = GameSettings.CurrentConfig.Graphics.Width;
            GraphicsHeight = GameSettings.CurrentConfig.Graphics.Height;

            if (GraphicsWidth <= 0 || GraphicsHeight <= 0)
            {
                GraphicsWidth = GraphicsDevice.DisplayMode.Width;
                GraphicsHeight = GraphicsDevice.DisplayMode.Height;
                updateConfig();
            }

            switch (GameSettings.CurrentConfig.Graphics.DisplayMode)
            {
                case WindowMode.BorderlessWindowed:
                    GraphicsWidth = GraphicsDevice.DisplayMode.Width;
                    GraphicsHeight = GraphicsDevice.DisplayMode.Height;
                    updateConfig();
                    break;
                case WindowMode.Windowed:
                    GraphicsWidth = Math.Min(GraphicsDevice.DisplayMode.Width, GraphicsWidth);
                    GraphicsHeight = Math.Min(GraphicsDevice.DisplayMode.Height, GraphicsHeight);
                    updateConfig();
                    break;
            }
            GraphicsDeviceManager.GraphicsProfile = GfxProfile;
            GraphicsDeviceManager.PreferredBackBufferFormat = SurfaceFormat.Color;
            GraphicsDeviceManager.PreferMultiSampling = false;
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = GameSettings.CurrentConfig.Graphics.VSync;
            SetWindowMode(GameSettings.CurrentConfig.Graphics.DisplayMode);

            defaultViewport = GraphicsDevice.Viewport;

            if (recalculateFontsAndStyles)
            {
                GUIStyle.RecalculateFonts();
                GUIStyle.RecalculateSizeRestrictions();
            }

            ResolutionChanged?.Invoke();
        }

        public void SetWindowMode(WindowMode windowMode)
        {
            WindowMode = windowMode;
            GraphicsDeviceManager.HardwareModeSwitch = windowMode != WindowMode.BorderlessWindowed;
            GraphicsDeviceManager.IsFullScreen = windowMode == WindowMode.Fullscreen || windowMode == WindowMode.BorderlessWindowed;
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

            performanceCounterTimer = Stopwatch.StartNew();
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
                WaitForLanguageSelection = GameSettings.CurrentConfig.Language == LanguageIdentifier.None
            };

            bool canLoadInSeparateThread = true;

            loadingCoroutine = CoroutineManager.StartCoroutine(Load(canLoadInSeparateThread), "Load", canLoadInSeparateThread);
        }

        public class LoadingException : Exception
        {
            public LoadingException(Exception e) : base("Loading was interrupted due to an error.", innerException: e)
            {
            }
        }

        private IEnumerable<CoroutineStatus> Load(bool isSeparateThread)
        {
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                DebugConsole.NewMessage("LOADING COROUTINE", Color.Lime);
            }

            ContentPackageManager.LoadVanillaFileList();

            if (TitleScreen.WaitForLanguageSelection)
            {
                ContentPackageManager.VanillaCorePackage.LoadFilesOfType<TextFile>();
                TitleScreen.AvailableLanguages = TextManager.AvailableLanguages.OrderBy(l => l.Value != "english".ToIdentifier()).ThenBy(l => l.Value).ToArray();
                while (TitleScreen.WaitForLanguageSelection)
                {
                    yield return CoroutineStatus.Running;
                }
                ContentPackageManager.VanillaCorePackage.UnloadFilesOfType<TextFile>();
            }

            SoundManager = new Sounds.SoundManager();
            SoundManager.ApplySettings();

            if (GameSettings.CurrentConfig.EnableSplashScreen && !ConsoleArguments.Contains("-skipintro"))
            {
                var pendingSplashScreens = TitleScreen.PendingSplashScreens;
                float baseVolume = MathHelper.Clamp(GameSettings.CurrentConfig.Audio.SoundVolume * 2.0f, 0.0f, 1.0f);
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

            GUI.Init();

            yield return CoroutineStatus.Running;

            UgcTransition.Prepare();
            var contentPackageLoadRoutine = ContentPackageManager.Init();
            foreach (var progress in contentPackageLoadRoutine)
            {
                const float min = 1f, max = 70f;
                TitleScreen.LoadState = MathHelper.Lerp(min, max, progress.Value);
                yield return CoroutineStatus.Running;
            }

            TextManager.VerifyLanguageAvailable();

            DebugConsole.Init();

            ContentPackageManager.LogEnabledRegularPackageErrors();

#if !DEBUG && !OSX
            GameAnalyticsManager.InitIfConsented();
#endif

            TaskPool.Add("InitRelayNetworkAccess", SteamManager.InitRelayNetworkAccess(), (t) => { });

            HintManager.Init();
        yield return CoroutineStatus.Running;
            CoreEntityPrefab.InitCorePrefabs();
            GameModePreset.Init();

            SaveUtil.DeleteDownloadedSubs();
            SubmarineInfo.RefreshSavedSubs();

            TitleScreen.LoadState = 75.0f;
        yield return CoroutineStatus.Running;

            GameScreen = new GameScreen(GraphicsDeviceManager.GraphicsDevice, Content);

            ParticleManager = new ParticleManager(GameScreen.Cam);
            LightManager = new Lights.LightManager(base.GraphicsDevice, Content);
            
            TitleScreen.LoadState = 80.0f;
        yield return CoroutineStatus.Running;

            MainMenuScreen          = new MainMenuScreen(this);
            ServerListScreen        = new ServerListScreen();

            TitleScreen.LoadState = 85.0f;
        yield return CoroutineStatus.Running;

#if USE_STEAM
            if (SteamManager.IsInitialized)
            {
                Steamworks.SteamFriends.OnGameRichPresenceJoinRequested += OnInvitedToGame;
                Steamworks.SteamFriends.OnGameLobbyJoinRequested += OnLobbyJoinRequested;

                if (SteamManager.TryGetUnlockedAchievements(out List<Steamworks.Data.Achievement> achievements))
                {
                    //check the achievements too, so we don't consider people who've played the game before this "gamelaunchcount" stat was added as being 1st-time-players
                    //(people who have played previous versions, but not unlocked any achievements, will be incorrectly considered 1st-time-players, but that should be a small enough group to not skew the statistics)
                    if (!achievements.Any() && SteamManager.GetStatInt("gamelaunchcount".ToIdentifier()) <= 0)
                    {
                        IsFirstLaunch = true;
                        GameAnalyticsManager.AddDesignEvent("FirstLaunch");
                    }
                }
                SteamManager.IncrementStat("gamelaunchcount".ToIdentifier(), 1);
            }
#endif

            SubEditorScreen         = new SubEditorScreen();
            TestScreen              = new TestScreen();

            TitleScreen.LoadState = 90.0f;
        yield return CoroutineStatus.Running;

            ParticleEditorScreen    = new ParticleEditorScreen();

            TitleScreen.LoadState = 95.0f;
        yield return CoroutineStatus.Running;

            LevelEditorScreen       = new LevelEditorScreen();
            SpriteEditorScreen      = new SpriteEditorScreen();
            EventEditorScreen       = new EventEditorScreen();
            CharacterEditorScreen   = new CharacterEditor.CharacterEditorScreen();
            CampaignEndScreen       = new CampaignEndScreen();

        yield return CoroutineStatus.Running;

#if DEBUG
            LevelGenerationParams.CheckValidity();
#endif

            MainMenuScreen.Select();

            foreach (Identifier steamError in SteamManager.InitializationErrors)
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get(steamError));
            }

            TitleScreen.LoadState = 100.0f;
            HasLoaded = true;
            if (GameSettings.CurrentConfig.VerboseLogging)
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
            if (Timing.Accumulator > Timing.AccumulatorMax)
            {
                //prevent spiral of death:
                //if the game's running too slowly then we have no choice but to skip a bunch of steps
                //otherwise it snowballs and becomes unplayable
                Timing.Accumulator = Timing.Step;
            }

            CrossThread.ProcessTasks();

            PlayerInput.UpdateVariable();

            if (SoundManager != null)
            {
                if (WindowActive || !GameSettings.CurrentConfig.Audio.MuteOnFocusLost)
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
                        GUI.ClearUpdateList();
                        GUI.UpdateGUIMessageBoxesOnly((float)Timing.Step);
                    }

                    if (TitleScreen.LoadState >= 100.0f && !TitleScreen.PlayingSplashScreen &&
                        (!waitForKeyHit || ((PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0 || PlayerInput.PrimaryMouseButtonClicked()) && WindowActive)))
                    {
                        loadingScreenOpen = false;
                    }

#if DEBUG
                    if (PlayerInput.KeyHit(Keys.LeftShift))
                    {
                        CancelQuickStart = !CancelQuickStart;
                    }

                    if (TitleScreen.LoadState >= 100.0f && !TitleScreen.PlayingSplashScreen &&
                        (GameSettings.CurrentConfig.AutomaticQuickStartEnabled ||
                         GameSettings.CurrentConfig.AutomaticCampaignLoadEnabled ||
                         GameSettings.CurrentConfig.TestScreenEnabled) && FirstLoad && !CancelQuickStart)
                    {
                        loadingScreenOpen = false;
                        FirstLoad = false;

                        if (GameSettings.CurrentConfig.TestScreenEnabled)
                        {
                            TestScreen.Select();
                        } 
                        else if (GameSettings.CurrentConfig.AutomaticQuickStartEnabled)
                        {
                            MainMenuScreen.QuickStart();
                        }
                        else if (GameSettings.CurrentConfig.AutomaticCampaignLoadEnabled)
                        {
                            var saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);
                            if (saveFiles.Count() > 0)
                            {
                                try
                                {
                                    SaveUtil.LoadGame(saveFiles.OrderBy(file => file.SaveTime).Last().FilePath);
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

                    NetworkMember?.Update((float)Timing.Step);

                    if (!HasLoaded && !CoroutineManager.IsCoroutineRunning(loadingCoroutine))
                    {
                        throw new LoadingException(loadingCoroutine.Exception);
                    }
                }
                else if (HasLoaded)
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
                        Client = new GameClient(MultiplayerPreferences.Instance.PlayerName.FallbackNullOrEmpty(SteamManager.GetUsername()),
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
                        else if (GameSession?.GameMode is TutorialMode tutorialMode && tutorialMode.Tutorial.ContentRunning)
                        {
                            tutorialMode.Tutorial.CloseActiveContentGUI();
                        }
                        else if (GameSession.IsTabMenuOpen)
                        {
                            gameSession.ToggleTabMenu();
                        }
                        else if (GUIMessageBox.VisibleBox as GUIMessageBox != null &&
                                 GUIMessageBox.VisibleBox.UserData as string == "bugreporter")
                        {
                            ((GUIMessageBox)GUIMessageBox.VisibleBox).Close();
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
                    Paused =
                        (DebugConsole.IsOpen || DebugConsole.Paused ||
                            GUI.PauseMenuOpen || GUI.SettingsMenuOpen ||
                            (GameSession?.GameMode is TutorialMode tutoMode && tutoMode.Tutorial.ContentRunning)) &&
                        (NetworkMember == null || !NetworkMember.GameStarted);
                    if (GameSession?.GameMode != null && GameSession.GameMode.Paused)
                    {
                        Paused = true;
                        GameSession.GameMode.UpdateWhilePaused((float)Timing.Step);
                    }

#if !DEBUG
                    if (NetworkMember == null && !WindowActive && !Paused && true && GameSettings.CurrentConfig.PauseOnFocusLost &&
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
                    else if (GameSession?.GameMode is TutorialMode tutorialMode && tutorialMode.Tutorial.ContentRunning)
                    {
                        tutorialMode.Update((float)Timing.Step);
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

#if DEBUG
                    if (DebugDraw && GUI.MouseOn != null && PlayerInput.IsCtrlDown() && PlayerInput.KeyHit(Keys.G))
                    {
                        List<GUIComponent> hierarchy = new List<GUIComponent>();
                        var currComponent = GUI.MouseOn;
                        while (currComponent != null)
                        {
                            hierarchy.Add(currComponent);
                            currComponent = currComponent.Parent;
                        }
                        DebugConsole.NewMessage("*********************");
                        foreach (var component in hierarchy)
                        {
                            if (component is { MouseRect: var mouseRect, Rect: var rect })
                            {
                                DebugConsole.NewMessage($"{component.GetType().Name} {component.Style?.Name ?? "[null]"} {rect.Bottom} {mouseRect.Bottom}", mouseRect!=rect ? Color.Lime : Color.Red);
                            }
                        }
                    }
#endif
                }

                CoroutineManager.Update((float)Timing.Step, Paused ? 0.0f : (float)Timing.Step);

                SteamManager.Update((float)Timing.Step);

                TaskPool.Update();

                SoundManager?.Update();

                Timing.Accumulator -= Timing.Step;

                updateCount++;

                sw.Stop();
                PerformanceCounter.AddElapsedTicks("Update", sw.ElapsedTicks);
                PerformanceCounter.UpdateTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);
            }

            if (!Paused) { Timing.Alpha = Timing.Accumulator / Timing.Step; }

            if (performanceCounterTimer.ElapsedMilliseconds > 1000)
            {
                CurrentUpdateRate = (int)Math.Round(updateCount / (double)(performanceCounterTimer.ElapsedMilliseconds / 1000.0));
                performanceCounterTimer.Restart();
                updateCount = 0;
            }
        }

        public static void ResetFrameTime()
        {
            Timing.Accumulator = 0.0f;
        }

        private void FixRazerCortex()
        {
#if WINDOWS
            //Razer Cortex's overlay is broken.
            //For whatever reason, it messes up the blendstate and,
            //because MonoGame reasonably assumes that you don't need
            //to touch it if you're setting it to the exact same one
            //you were already using, it doesn't fix Razer's mess.
            //Therefore, we need to change the blendstate TWICE:
            //once to force MonoGame to change it, and then again to
            //use the blendstate we actually want.
            var oldBlendState = GraphicsDevice.BlendState;
            GraphicsDevice.BlendState = oldBlendState == BlendState.Opaque ? BlendState.NonPremultiplied : BlendState.Opaque;
            GraphicsDevice.BlendState = oldBlendState;
#endif
        }
        
        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        protected override void Draw(GameTime gameTime)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            FixRazerCortex();
            
            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            if (Timing.FrameLimit > 0)
            {
                double step = 1.0 / Timing.FrameLimit;
                while (!GameSettings.CurrentConfig.Graphics.VSync && sw.Elapsed.TotalSeconds + deltaTime < step)
                {
                    Thread.Sleep(1);
                }
            }

            PerformanceCounter.Update(sw.Elapsed.TotalSeconds + deltaTime);

            if (loadingScreenOpen)
            {
                TitleScreen.Draw(spriteBatch, base.GraphicsDevice, (float)deltaTime);
            }
            else if (HasLoaded)
            {
                Screen.Selected.Draw(deltaTime, base.GraphicsDevice, spriteBatch);
            }

            if (DebugDraw && GUI.MouseOn != null)
            {
                spriteBatch.Begin();
                if (PlayerInput.IsCtrlDown() && PlayerInput.KeyDown(Keys.G))
                {
                    List<GUIComponent> hierarchy = new List<GUIComponent>();
                    var currComponent = GUI.MouseOn;
                    while (currComponent != null)
                    {
                        hierarchy.Add(currComponent);
                        currComponent = currComponent.Parent;
                    }

                    Color[] colors = { Color.Lime, Color.Yellow, Color.Aqua, Color.Red };
                    for (int index = 0; index < hierarchy.Count; index++)
                    {
                        var component = hierarchy[index];
                        if (component is { MouseRect: var mouseRect, Rect: var rect })
                        {
                            if (mouseRect.IsEmpty) { mouseRect = rect; }
                            mouseRect.Location += (index%2,(index%4)/2);
                            GUI.DrawRectangle(spriteBatch, mouseRect, colors[index%4]);
                        }
                    }
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, GUI.MouseOn.MouseRect, Color.Lime);
                    GUI.DrawRectangle(spriteBatch, GUI.MouseOn.Rect, Color.Cyan);
                }
                spriteBatch.End();
            }

            sw.Stop();
            PerformanceCounter.AddElapsedTicks("Draw", sw.ElapsedTicks);
            PerformanceCounter.DrawTimeGraph.Update(sw.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency);
        }


        public static void QuitToMainMenu(bool save, bool showVerificationPrompt)
        {
            if (showVerificationPrompt)
            {
                string text = (Screen.Selected is CharacterEditor.CharacterEditorScreen || Screen.Selected is SubEditorScreen) ? "PauseMenuQuitVerificationEditor" : "PauseMenuQuitVerification";
                var msgBox = new GUIMessageBox("", TextManager.Get(text), new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("Cancel") })
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
                if (GameSession?.Campaign is SinglePlayerCampaign spCampaign && Level.IsLoadedFriendlyOutpost && spCampaign.Map?.CurrentLocation != null && spCampaign.CargoManager != null)
                {
                    spCampaign.Map.CurrentLocation.AddStock(spCampaign.CargoManager.SoldItems);
                    spCampaign.CargoManager.ClearSoldItemsProjSpecific();
                    spCampaign.Map.CurrentLocation.RemoveStock(spCampaign.CargoManager.PurchasedItems);
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
                double roundDuration = Timing.TotalTime - GameSession.RoundStartTime;
                GameAnalyticsManager.AddProgressionEvent(GameAnalyticsManager.ProgressionStatus.Fail,
                    GameSession.GameMode?.Preset.Identifier.Value ?? "none",
                    roundDuration);
                string eventId = "QuitRound:" + (GameSession.GameMode?.Preset.Identifier.Value ?? "none") + ":";
                GameAnalyticsManager.AddDesignEvent(eventId + "EventManager:CurrentIntensity", GameSession.EventManager.CurrentIntensity);
                foreach (var activeEvent in GameSession.EventManager.ActiveEvents)
                {
                    GameAnalyticsManager.AddDesignEvent(eventId + "EventManager:ActiveEvents:" + activeEvent.ToString());
                }
                GameSession.LogEndRoundStats(eventId);
                if (GameSession.GameMode is TutorialMode tutorialMode)
                {
                    tutorialMode.Tutorial?.Stop();
                }
            }
            GUIMessageBox.CloseAll();
            MainMenuScreen.Select();
            GameSession = null;

        }

        public void ShowCampaignDisclaimer(Action onContinue = null)
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CampaignDisclaimerTitle"), TextManager.Get("CampaignDisclaimerText"),
                new LocalizedString[] { TextManager.Get("CampaignRoadMapTitle"), TextManager.Get("OK") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                ShowOpenUrlInWebBrowserPrompt("https://trello.com/b/hBXI8ltN/barotrauma-roadmap-known-issues");
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += (_, __) => { onContinue?.Invoke(); return true; };

            var config = GameSettings.CurrentConfig;
            config.CampaignDisclaimerShown = true;
            GameSettings.SetCurrentConfig(config);
            GameSettings.SaveCurrentConfig();
        }

        public void ShowEditorDisclaimer()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("EditorDisclaimerTitle"), TextManager.Get("EditorDisclaimerText"));
            var linkHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), msgBox.Content.RectTransform)) { Stretch = true, RelativeSpacing = 0.025f };
            linkHolder.RectTransform.MaxSize = new Point(int.MaxValue, linkHolder.Rect.Height);
            List<(LocalizedString Caption, string Url)> links = new List<(LocalizedString, string)>()
            {
                (TextManager.Get("EditorDisclaimerWikiLink"), TextManager.Get("EditorDisclaimerWikiUrl").Fallback("https://barotraumagame.com/wiki").Value),
                (TextManager.Get("EditorDisclaimerDiscordLink"), TextManager.Get("EditorDisclaimerDiscordUrl").Fallback("https://discordapp.com/invite/undertow").Value),
            };
            foreach (var link in links)
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), linkHolder.RectTransform), link.Caption, style: "MainMenuGUIButton", textAlignment: Alignment.Left)
                {
                    UserData = link.Url,
                    OnClicked = (btn, userdata) =>
                    {
                        ShowOpenUrlInWebBrowserPrompt(userdata as string);
                        return true;
                    }
                };
            }

            msgBox.InnerFrame.RectTransform.MinSize = new Point(0,
                msgBox.InnerFrame.Rect.Height + linkHolder.Rect.Height + msgBox.Content.AbsoluteSpacing * 2 + 10);
            var config = GameSettings.CurrentConfig;
            config.EditorDisclaimerShown = true;
            GameSettings.SetCurrentConfig(config);
            GameSettings.SaveCurrentConfig();
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
                UserData = "https://github.com/Regalis11/Barotrauma/issues/new/choose",
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
        public CoroutineHandle ShowLoading(IEnumerable<CoroutineStatus> loader, bool waitKeyHit = true)
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

            if (GameAnalyticsManager.SendUserStatistics) { GameAnalyticsManager.ShutDown(); }
            if (GameSettings.CurrentConfig.SaveDebugConsoleLogs
                || GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.SaveLogs(); }

            base.OnExiting(sender, args);
        }

        public void ShowOpenUrlInWebBrowserPrompt(string url, string promptExtensionTag = null)
        {
            if (string.IsNullOrEmpty(url)) { return; }
            if (GUIMessageBox.VisibleBox?.UserData as string == "verificationprompt") { return; }

            LocalizedString text = TextManager.GetWithVariable("openlinkinbrowserprompt", "[link]", url);
            LocalizedString extensionText = TextManager.Get(promptExtensionTag);
            if (!extensionText.IsNullOrEmpty())
            {
                text += $"\n\n{extensionText}";
            }

            var msgBox = new GUIMessageBox("", text, new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") })
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
