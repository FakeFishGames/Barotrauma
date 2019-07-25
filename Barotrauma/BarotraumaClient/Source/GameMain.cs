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
using System.IO;
using System.Threading;
using Barotrauma.Tutorials;
using Barotrauma.Media;

namespace Barotrauma
{
    class GameMain : Game
    {
        public static bool ShowFPS = false;
        public static bool ShowPerf = false;
        public static bool DebugDraw;
        public static bool IsMultiplayer => NetworkMember != null;

        public static PerformanceCounter PerformanceCounter;

        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static string[] ConsoleArguments;

        public static GameScreen GameScreen;
        public static MainMenuScreen MainMenuScreen;
        public static LobbyScreen LobbyScreen;

        public static NetLobbyScreen NetLobbyScreen;
        public static ServerListScreen ServerListScreen;
        public static SteamWorkshopScreen SteamWorkshopScreen;

        public static SubEditorScreen SubEditorScreen;
        public static ParticleEditorScreen ParticleEditorScreen;
        public static LevelEditorScreen LevelEditorScreen;
        public static SpriteEditorScreen SpriteEditorScreen;
        public static CharacterEditorScreen CharacterEditorScreen;

        public static Lights.LightManager LightManager;

        public static Sounds.SoundManager SoundManager;

        public static Thread MainThread { get; private set; }

        public static HashSet<ContentPackage> SelectedPackages
        {
            get { return Config?.SelectedContentPackages; }
        }

        private static ContentPackage vanillaContent;
        public static ContentPackage VanillaContent
        {
            get
            {
                if (vanillaContent == null)
                {
                    // TODO: Dynamic method for defining and finding the vanilla content package.
                    vanillaContent = ContentPackage.List.SingleOrDefault(cp => Path.GetFileName(cp.Path).ToLowerInvariant() == "vanilla 0.9.xml");
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

        public static World World;

        public static LoadingScreen TitleScreen;
        private bool loadingScreenOpen;

        public static GameSettings Config;

        private CoroutineHandle loadingCoroutine;
        private bool hasLoaded;

        private GameTime fixedTime;

        private static SpriteBatch spriteBatch;

        private Viewport defaultViewport;

        public event Action OnResolutionChanged;

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
            get { return Instance == null || Instance.IsActive; }
        }

        public static GameClient Client;
        public static NetworkMember NetworkMember
        {
            get { return Client; }
        }

        public static Process ServerChildProcess;

        public static RasterizerState ScissorTestEnable
        {
            get;
            private set;
        }

        public bool LoadingScreenOpen
        {
            get { return loadingScreenOpen; }
        }

        public GameMain(string[] args)
        {
            Content.RootDirectory = "Content";

            GraphicsDeviceManager = new GraphicsDeviceManager(this);

            Window.Title = "Barotrauma";

            Instance = this;

            Config = new GameSettings();

            ConsoleArguments = args;

            GUI.KeyboardDispatcher = new EventInput.KeyboardDispatcher(Window);

            PerformanceCounter = new PerformanceCounter();

            IsFixedTimeStep = false;

            GameMain.ResetFrameTime();
            fixedTime = new GameTime();

            World = new World(new Vector2(0, -9.82f));
            FarseerPhysics.Settings.AllowSleep = true;
            FarseerPhysics.Settings.ContinuousPhysics = false;
            FarseerPhysics.Settings.VelocityIterations = 1;
            FarseerPhysics.Settings.PositionIterations = 1;

            MainThread = Thread.CurrentThread;
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
            GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.Reach;
            GraphicsDeviceManager.PreferredBackBufferFormat = SurfaceFormat.Color;
            GraphicsDeviceManager.PreferMultiSampling = false;
            GraphicsDeviceManager.SynchronizeWithVerticalRetrace = Config.VSyncEnabled;
            SetWindowMode(Config.WindowMode);

            defaultViewport = GraphicsDevice.Viewport;

            OnResolutionChanged?.Invoke();
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

            ConvertUnits.SetDisplayUnitToSimUnitRatio(Physics.DisplayToSimRation);

            spriteBatch = new SpriteBatch(GraphicsDevice);
            TextureLoader.Init(GraphicsDevice);

            //do this here because we need it for the loading screen
            WaterRenderer.Instance = new WaterRenderer(base.GraphicsDevice, Content);

            loadingScreenOpen = true;
            TitleScreen = new LoadingScreen(GraphicsDevice)
            {
                WaitForLanguageSelection = Config.ShowLanguageSelectionPrompt
            };

            bool canLoadInSeparateThread = true;

            ApplyGraphicsSettings();

            loadingCoroutine = CoroutineManager.StartCoroutine(Load(canLoadInSeparateThread), "Load", canLoadInSeparateThread);
        }

        private void InitUserStats()
        {
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
            SoundManager.SetCategoryGainMultiplier("default", Config.SoundVolume);
            SoundManager.SetCategoryGainMultiplier("ui", Config.SoundVolume);
            SoundManager.SetCategoryGainMultiplier("waterambience", Config.SoundVolume);
            SoundManager.SetCategoryGainMultiplier("music", Config.MusicVolume);
            SoundManager.SetCategoryGainMultiplier("voip", Config.VoiceChatVolume * 20.0f);

            if (ConsoleArguments.Contains("skipintro")) {
                Config.EnableSplashScreen = false;
            }

            if (Config.EnableSplashScreen)
            {
                var pendingSplashScreens = TitleScreen.PendingSplashScreens;
                pendingSplashScreens?.Enqueue(new Pair<string, Point>("Content/Splash_UTG.mp4", new Point(1280, 720)));
                pendingSplashScreens?.Enqueue(new Pair<string, Point>("Content/Splash_FF.mp4", new Point(1280, 720)));
                pendingSplashScreens?.Enqueue(new Pair<string, Point>("Content/Splash_Daedalic.mp4", new Point(1920, 1080)));
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

            GUI.Init(Window, Config.SelectedContentPackages, GraphicsDevice);
            DebugConsole.Init();

            if (Config.AutoUpdateWorkshopItems)
            {
                if (SteamManager.AutoUpdateWorkshopItems())
                {
                    ContentPackage.LoadAll(ContentPackage.Folder);
                    Config.ReloadContentPackages();
                }
            }

            if (SelectedPackages.Count == 0)
            {
                DebugConsole.Log("No content packages selected");
            }
            else
            {
                DebugConsole.Log("Selected content packages: " + string.Join(", ", SelectedPackages.Select(cp => cp.Name)));
            }

/*#if DEBUG
            GameSettings.ShowUserStatisticsPrompt = false;
            GameSettings.SendUserStatistics = false;
#endif*/

            InitUserStats();

        yield return CoroutineStatus.Running;

            LightManager = new Lights.LightManager(base.GraphicsDevice, Content);

            TitleScreen.LoadState = 1.0f;
        yield return CoroutineStatus.Running;

            GUI.LoadContent();
            TitleScreen.LoadState = 2.0f;

        yield return CoroutineStatus.Running;

            MissionPrefab.Init();
            MapEntityPrefab.Init();
            Tutorials.Tutorial.Init();
            MapGenerationParams.Init();
            LevelGenerationParams.LoadPresets();
            ScriptedEventSet.LoadPrefabs();
            AfflictionPrefab.LoadAll(GetFilesOfType(ContentType.Afflictions));
            TitleScreen.LoadState = 10.0f;
        yield return CoroutineStatus.Running;

            StructurePrefab.LoadAll(GetFilesOfType(ContentType.Structure));
            TitleScreen.LoadState = 15.0f;
        yield return CoroutineStatus.Running;

            ItemPrefab.LoadAll(GetFilesOfType(ContentType.Item));
            TitleScreen.LoadState = 25.0f;
        yield return CoroutineStatus.Running;

            JobPrefab.LoadAll(GetFilesOfType(ContentType.Jobs));
            // Add any missing jobs from the prefab into Config.JobNamePreferences.
            foreach (JobPrefab job in JobPrefab.List)
            {
                if (!Config.JobPreferences.Contains(job.Identifier)) { Config.JobPreferences.Add(job.Identifier); }
            }

            NPCConversation.LoadAll(GetFilesOfType(ContentType.NPCConversations));

            ItemAssemblyPrefab.LoadAll();
            TitleScreen.LoadState = 30.0f;
        yield return CoroutineStatus.Running;

            Debug.WriteLine("sounds");

            int i = 0;
            foreach (object crObj in SoundPlayer.Init())
            {
                CoroutineStatus status = (CoroutineStatus)crObj;
                if (status == CoroutineStatus.Success) break;

                i++;
                TitleScreen.LoadState = SoundPlayer.SoundCount == 0 ?
                    30.0f :
                    Math.Min(30.0f + 40.0f * i / Math.Max(SoundPlayer.SoundCount, 1), 70.0f);

                yield return CoroutineStatus.Running;
            }

            TitleScreen.LoadState = 70.0f;
        yield return CoroutineStatus.Running;

            GameModePreset.Init();

            Submarine.RefreshSavedSubs();

            TitleScreen.LoadState = 80.0f;

        yield return CoroutineStatus.Running;

            GameScreen = new GameScreen(GraphicsDeviceManager.GraphicsDevice, Content);

            TitleScreen.LoadState = 90.0f;

        yield return CoroutineStatus.Running;

            MainMenuScreen          = new MainMenuScreen(this);
            LobbyScreen             = new LobbyScreen();
            ServerListScreen        = new ServerListScreen();

            if (SteamManager.USE_STEAM)
            {
                SteamWorkshopScreen     = new SteamWorkshopScreen();
            }

            SubEditorScreen         = new SubEditorScreen();
            ParticleEditorScreen    = new ParticleEditorScreen();
            LevelEditorScreen       = new LevelEditorScreen();
            SpriteEditorScreen      = new SpriteEditorScreen();
            CharacterEditorScreen   = new CharacterEditorScreen();

        yield return CoroutineStatus.Running;

            TitleScreen.LoadState = 95.0f;
            ParticleManager = new ParticleManager(GameScreen.Cam);
            ParticleManager.LoadPrefabs();
            TitleScreen.LoadState = 97.0f;
            LevelObjectPrefab.LoadAll();
            DecalManager = new DecalManager();
            TitleScreen.LoadState = 99.0f;
        yield return CoroutineStatus.Running;

            LocationType.Init();
            MainMenuScreen.Select();

            CheckContentPackage();

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

        private void CheckContentPackage()
        {
            foreach (ContentPackage contentPackage in Config.SelectedContentPackages)
            {
                var exePaths = contentPackage.GetFilesOfType(ContentType.Executable);
                if (exePaths.Any() && AppDomain.CurrentDomain.FriendlyName != exePaths.First())
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariables("IncorrectExe",
                        new string[2] { "[selectedpackage]", "[exename]" }, new string[2] { contentPackage.Name, exePaths.First() }),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    msgBox.Buttons[0].OnClicked += (_, userdata) =>
                    {
                        string fullPath = Path.GetFullPath(exePaths.First());
                        Process.Start(fullPath);
                        Exit();
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked = msgBox.Close;
                    break;
                }
            }
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            CoroutineManager.StopCoroutines("Load");
            Video.Close();
            VoipCapture.Instance?.Dispose();
            SoundManager?.Dispose();
        }

        /// <summary>
        /// Returns the file paths of all files of the given type in the content packages.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="searchAllContentPackages">If true, also returns files in content packages that are installed but not currently selected.</param>
        public IEnumerable<string> GetFilesOfType(ContentType type, bool searchAllContentPackages = false)
        {
            if (searchAllContentPackages)
            {
                return ContentPackage.GetFilesOfType(ContentPackage.List, type);
            }
            else
            {
                return ContentPackage.GetFilesOfType(SelectedPackages, type);
            }
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            Timing.TotalTime = gameTime.TotalGameTime.TotalSeconds;
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

            bool paused = true;

            if (SoundManager != null)
            {
                if (WindowActive || !Config.MuteOnFocusLost)
                {
                    SoundManager.ListenerGain = SoundManager.PlaybackCompressedGain;
                }
                else
                {
                    SoundManager.ListenerGain = 0.0f;
                }
            }

            while (Timing.Accumulator >= Timing.Step)
            {
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
                    GameMain.ResetFrameTime();

                    if (TitleScreen.LoadState >= 100.0f && !TitleScreen.PlayingSplashScreen &&
                        (!waitForKeyHit || ((PlayerInput.GetKeyboardState.GetPressedKeys().Length > 0 || PlayerInput.LeftButtonClicked()) && WindowActive)))
                    {
                        loadingScreenOpen = false;
                    }

                    if (!hasLoaded && !CoroutineManager.IsCoroutineRunning(loadingCoroutine))
                    {
                        string errMsg = "Loading was interrupted due to an error";
                        if (loadingCoroutine.Exception != null)
                        {
                            errMsg += ": " + loadingCoroutine.Exception.Message + "\n" + loadingCoroutine.Exception.StackTrace;
                        }
                        throw new Exception(errMsg);
                    }
                }
                else if (hasLoaded)
                {
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
                        else if (Tutorial.Initialized && Tutorial.ContentRunning)
                        {
                            (GameSession.GameMode as TutorialMode).Tutorial.CloseActiveContentGUI();
                        }
                        else if (GUI.PauseMenuOpen)
                        {
                            GUI.TogglePauseMenu();
                        }
                        else if ((Character.Controlled?.SelectedConstruction == null || !Character.Controlled.SelectedConstruction.ActiveHUDs.Any(ic => ic.GuiFrame != null))
                            && Inventory.SelectedSlot == null && CharacterHealth.OpenHealthWindow == null)
                        {
                            // Otherwise toggle pausing, unless another window/interface is open.
                            GUI.TogglePauseMenu();
                        }
                    }

                    GUI.ClearUpdateList();
                    paused = (DebugConsole.IsOpen || GUI.PauseMenuOpen || GUI.SettingsMenuOpen || Tutorial.ContentRunning) &&
                             (NetworkMember == null || !NetworkMember.GameStarted);

#if !DEBUG
                    if (NetworkMember == null && !WindowActive && !paused && true && Screen.Selected != MainMenuScreen && Config.PauseOnFocusLost)
                    {
                        GUI.TogglePauseMenu();
                        paused = true;
                    }
#endif

                    Screen.Selected.AddToGUIUpdateList();

                    if (Client != null)
                    {
                        Client.AddToGUIUpdateList();
                    }

                    DebugConsole.AddToGUIUpdateList();

                    DebugConsole.Update(this, (float)Timing.Step);
                    paused = paused || (DebugConsole.IsOpen && (NetworkMember == null || !NetworkMember.GameStarted));

                    if (!paused)
                    {
                        Screen.Selected.Update(Timing.Step);
                    }
                    else if (Tutorial.Initialized && Tutorial.ContentRunning)
                    {
                        (GameSession.GameMode as TutorialMode).Update((float)Timing.Step);
                    }

                    if (NetworkMember != null)
                    {
                        NetworkMember.Update((float)Timing.Step);
                    }

                    GUI.Update((float)Timing.Step);
                }

                CoroutineManager.Update((float)Timing.Step, paused ? 0.0f : (float)Timing.Step);

                SteamManager.Update((float)Timing.Step);

                SoundManager.Update();

                Timing.Accumulator -= Timing.Step;

                sw.Stop();
                PerformanceCounter.AddElapsedTicks("Update total", sw.ElapsedTicks);
                PerformanceCounter.UpdateTimeGraph.Update(sw.ElapsedTicks / (float)TimeSpan.TicksPerMillisecond);
                PerformanceCounter.UpdateIterationsGraph.Update(updateIterations);
            }

            if (!paused) Timing.Alpha = Timing.Accumulator / Timing.Step;
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

            PerformanceCounter.Update(deltaTime);

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
                spriteBatch.End();
            }


            sw.Stop();
            PerformanceCounter.AddElapsedTicks("Draw total", sw.ElapsedTicks);
            PerformanceCounter.DrawTimeGraph.Update(sw.ElapsedTicks / (float)TimeSpan.TicksPerMillisecond);
        }

        public void ShowCampaignDisclaimer(Action onContinue = null)
        {
            var msgBox = new GUIMessageBox(TextManager.Get("CampaignDisclaimerTitle"), TextManager.Get("CampaignDisclaimerText"),
                new string[] { TextManager.Get("CampaignRoadMapTitle"), TextManager.Get("OK") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                var roadMap = new GUIMessageBox(TextManager.Get("CampaignRoadMapTitle"), TextManager.Get("CampaignRoadMapText"),
                                new string[] { TextManager.Get("Back"), TextManager.Get("OK") });
                roadMap.Buttons[0].OnClicked += roadMap.Close;
                roadMap.Buttons[0].OnClicked += (_, __) => { ShowCampaignDisclaimer(onContinue); return true; };
                roadMap.Buttons[1].OnClicked += roadMap.Close;
                roadMap.Buttons[1].OnClicked += (_, __) => { onContinue?.Invoke(); return true; };
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
                new Pair<string, string>(TextManager.Get("EditorDisclaimerWikiLink"),TextManager.Get("EditorDisclaimerWikiUrl")),
                new Pair<string, string>(TextManager.Get("EditorDisclaimerDiscordLink"),TextManager.Get("EditorDisclaimerDiscordUrl")),
                new Pair<string, string>(TextManager.Get("EditorDisclaimerForumLink"),TextManager.Get("EditorDisclaimerForumUrl")),
            };
            foreach (var link in links)
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), linkHolder.RectTransform), link.First, style: "MainMenuGUIButton", textAlignment: Alignment.Left)
                {
                    UserData = link.Second,
                    OnClicked = (btn, userdata) =>
                    {
                        Process.Start(userdata as string);
                        return true;
                    }
                };
            }

            msgBox.InnerFrame.RectTransform.MinSize = new Point(0,
                msgBox.InnerFrame.Rect.Height + linkHolder.Rect.Height + msgBox.Content.AbsoluteSpacing * 2 + 10);
            Config.EditorDisclaimerShown = true;
            Config.SaveNewPlayerConfig();
        }

        // ToDo: Move texts/links to localization, when possible.
        public void ShowBugReporter()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("bugreportbutton"), "");
            msgBox.UserData = "bugreporter";
            var linkHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), msgBox.Content.RectTransform)) { Stretch = true, RelativeSpacing = 0.025f };
            linkHolder.RectTransform.MaxSize = new Point(int.MaxValue, linkHolder.Rect.Height);

            List<Pair<string, string>> links = new List<Pair<string, string>>()
                {
                    new Pair<string, string>(TextManager.Get("bugreportfeedbackform"),"https://barotraumagame.com/feedback"),
                    new Pair<string, string>(TextManager.Get("bugreportgithubform"),"https://github.com/Regalis11/Barotrauma/issues/new?template=bug_report.md")
                };
            foreach (var link in links)
            {
                new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), linkHolder.RectTransform), link.First, style: "MainMenuGUIButton", textAlignment: Alignment.Left)
                {
                    UserData = link.Second,
                    OnClicked = (btn, userdata) =>
                    {
                        Process.Start(userdata as string);
                        msgBox.Close();
                        return true;
                    }
                };
            }

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
            if (NetworkMember != null) NetworkMember.Disconnect();
            SteamManager.ShutDown();
            if (GameSettings.SendUserStatistics) GameAnalytics.OnQuit();
            if (GameSettings.SaveDebugConsoleLogs) DebugConsole.SaveLogs();
            base.OnExiting(sender, args);
        }
    }
}
