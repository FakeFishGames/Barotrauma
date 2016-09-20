using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Barotrauma.Particles;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using System.Xml;

namespace Barotrauma
{
    class GameMain : Game
    {
        public static GraphicsDeviceManager Graphics;
        static int graphicsWidth, graphicsHeight;
        static SpriteBatch spriteBatch;

        public static GameMain Instance;

        public static bool WindowActive
        {
            get { return Instance == null || Instance.IsActive; }
        }

        public static bool DebugDraw;

        public static GraphicsDevice CurrGraphicsDevice;

        public static FrameCounter FrameCounter;

        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static GameScreen            GameScreen;
        public static MainMenuScreen        MainMenuScreen;
        public static LobbyScreen           LobbyScreen;

        public static NetLobbyScreen        NetLobbyScreen;
        public static ServerListScreen      ServerListScreen;

        public static EditMapScreen         EditMapScreen;
        public static EditCharacterScreen   EditCharacterScreen;

        public static Lights.LightManager LightManager;
        
        public static ContentPackage SelectedPackage
        {
            get { return Config.SelectedContentPackage; }
        }

        public static Level Level;

        public static GameSession GameSession;

        public static NetworkMember NetworkMember;

        public static ParticleManager ParticleManager;

        //public static TextureLoader TextureLoader;
        
        public static World World;

        public static LoadingScreen TitleScreen;
        private static bool titleScreenOpen;

        public static GameSettings Config;

        private bool hasLoaded;

        private GameTime fixedTime;
        private double updatesToMake;

        //public static Random localRandom;
        //public static Random random;

        //private Stopwatch renderTimer;
        //public static int renderTimeElapsed;
                        
        public Camera Cam
        {
            get { return GameScreen.Cam; }
        }

        public static int GraphicsWidth
        {
            get { return graphicsWidth; }
        }

        public static int GraphicsHeight
        {
            get { return graphicsHeight; }
        }

        public static GameServer Server
        {
            get { return NetworkMember as GameServer; }
        }

        public static GameClient Client
        {
            get { return NetworkMember as GameClient; }
        }
                
        public GameMain()
        {
            Graphics = new GraphicsDeviceManager(this)
            {
                SynchronizeWithVerticalRetrace = false,
            };

            Window.Title = "Barotrauma";

            Instance = this;

            Config = new GameSettings("config.xml");
            if (Config.WasGameUpdated)
            {
                UpdaterUtil.CleanOldFiles();
                Config.WasGameUpdated = false;
                Config.Save("config.xml");
            }
            
            graphicsWidth = Config.GraphicsWidth;
            graphicsHeight = Config.GraphicsHeight;
            Graphics.SynchronizeWithVerticalRetrace = Config.VSyncEnabled;

            Graphics.HardwareModeSwitch = Config.WindowMode != WindowMode.BorderlessWindowed;

            Graphics.IsFullScreen = Config.WindowMode == WindowMode.Fullscreen || Config.WindowMode == WindowMode.BorderlessWindowed;
            Graphics.PreferredBackBufferWidth = graphicsWidth;
            Graphics.PreferredBackBufferHeight = graphicsHeight;
            Content.RootDirectory = "Content";

            //graphics.SynchronizeWithVerticalRetrace = false;
            //graphics.ApplyChanges();

            FrameCounter = new FrameCounter();

            //IsMouseVisible = true;

            IsFixedTimeStep = false;
            //TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 55);

            updatesToMake = 0.0;
            fixedTime = new GameTime();

            World = new World(new Vector2(0, -9.82f));
            FarseerPhysics.Settings.AllowSleep = true;
            FarseerPhysics.Settings.ContinuousPhysics = false;
            FarseerPhysics.Settings.VelocityIterations = 1;
            FarseerPhysics.Settings.PositionIterations = 1;

            Graphics.ApplyChanges();
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

            CurrGraphicsDevice = GraphicsDevice;

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
            graphicsWidth   = GraphicsDevice.Viewport.Width;
            graphicsHeight  = GraphicsDevice.Viewport.Height;
                        
            Sound.Init();

            ConvertUnits.SetDisplayUnitToSimUnitRatio(Physics.DisplayToSimRation);
                        
            spriteBatch = new SpriteBatch(GraphicsDevice);
            TextureLoader.Init(GraphicsDevice);

            titleScreenOpen = true;
            TitleScreen = new LoadingScreen(GraphicsDevice);

            CoroutineManager.StartCoroutine(Load());
        }

        public IEnumerable<object> Load()
        {
            GUI.Init(Content);

            GUIComponent.Init(Window);
            DebugConsole.Init(Window);
            DebugConsole.Log(SelectedPackage == null ? "No content package selected" : "Content package ''" + SelectedPackage.Name + "'' selected");
        yield return CoroutineStatus.Running;

            LightManager = new Lights.LightManager(GraphicsDevice);

            Hull.renderer = new WaterRenderer(GraphicsDevice, Content);
            TitleScreen.LoadState = 1.0f;
        yield return CoroutineStatus.Running;

            GUI.LoadContent(GraphicsDevice);
            TitleScreen.LoadState = 2.0f;
        yield return CoroutineStatus.Running;

            Mission.Init();
            MapEntityPrefab.Init();
            TitleScreen.LoadState = 10.0f;
        yield return CoroutineStatus.Running;

            JobPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Jobs));
            StructurePrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Structure));
            TitleScreen.LoadState = 20.0f;
        yield return CoroutineStatus.Running;

            ItemPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Item));
            TitleScreen.LoadState = 30.0f;
        yield return CoroutineStatus.Running;

            Debug.WriteLine("sounds");
            CoroutineManager.StartCoroutine(SoundPlayer.Init());

            int i = 0;
            while (!SoundPlayer.Initialized)
            {
                i++;
                TitleScreen.LoadState = Math.Min((float)TitleScreen.LoadState + 40.0f / 41, 70.0f);
                yield return CoroutineStatus.Running;
            }

            TitleScreen.LoadState = 70.0f;
        yield return CoroutineStatus.Running;

            GameModePreset.Init();

            Submarine.Preload();
            TitleScreen.LoadState = 80.0f;
        yield return CoroutineStatus.Running;

            GameScreen          =   new GameScreen(Graphics.GraphicsDevice, Content);
            TitleScreen.LoadState = 90.0f;
        yield return CoroutineStatus.Running;

            MainMenuScreen      =   new MainMenuScreen(this); 
            LobbyScreen         =   new LobbyScreen();
            
            ServerListScreen    =   new ServerListScreen();

            EditMapScreen       =   new EditMapScreen();
            EditCharacterScreen =   new EditCharacterScreen();

        yield return CoroutineStatus.Running;

            ParticleManager = new ParticleManager("Content/Particles/ParticlePrefabs.xml", Cam);
        yield return CoroutineStatus.Running;

            LocationType.Init();
            MainMenuScreen.Select();

            TitleScreen.LoadState = 100.0f;
            hasLoaded = true;
        yield return CoroutineStatus.Success;

        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            Sound.Dispose();
        }
        
        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            double realDeltaTime = gameTime.ElapsedGameTime.TotalSeconds;
            double deltaTime = 0.016;
            updatesToMake += realDeltaTime;

            while (updatesToMake > 0.0)
            {
                fixedTime.IsRunningSlowly = gameTime.IsRunningSlowly;
                TimeSpan addTime = new TimeSpan(0,0,0,0,16);
                fixedTime.ElapsedGameTime = addTime;
                fixedTime.TotalGameTime.Add(addTime);
                base.Update(fixedTime);

                PlayerInput.Update(deltaTime);

                bool paused = false;

                if (titleScreenOpen)
                {
                    //TitleScreen.Update();
                }
                else if (hasLoaded)
                {
                    SoundPlayer.Update();

                    if (PlayerInput.KeyHit(Keys.Escape)) GUI.TogglePauseMenu();

                    DebugConsole.Update(this, (float)deltaTime);

                    paused = (DebugConsole.IsOpen || GUI.PauseMenuOpen || GUI.SettingsMenuOpen) &&
                             (NetworkMember == null || !NetworkMember.GameStarted);

                    if (!paused)
                    {
                        Screen.Selected.Update(deltaTime);
                    }

                    if (NetworkMember != null)
                    {
                        NetworkMember.Update((float)deltaTime);
                    }
                    else
                    {

                    }

                    GUI.Update((float)deltaTime);
                }

                CoroutineManager.Update((float)deltaTime, paused ? 0.0f : (float)deltaTime);

                updatesToMake -= deltaTime;
            }
        }


        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        protected override void Draw(GameTime gameTime)
        {
            //renderTimer.Restart();

            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            FrameCounter.Update(deltaTime);

            if (titleScreenOpen)
            {
                TitleScreen.Draw(spriteBatch, GraphicsDevice, (float)deltaTime);
                if (TitleScreen.LoadState>=100.0f && 
                    (!waitForKeyHit || PlayerInput.GetKeyboardState.GetPressedKeys().Length>0 || PlayerInput.LeftButtonClicked()))
                {
                    titleScreenOpen = false;
                }
            }
            else if (hasLoaded)
            {
                Screen.Selected.Draw(deltaTime, GraphicsDevice, spriteBatch);
            }

            //double elapsed = sw.Elapsed.TotalSeconds;
            //if (elapsed < Physics.step)
            //{
            //    System.Threading.Thread.Sleep((int)((Physics.step - elapsed) * 1000.0));
            //}
        }

        static bool waitForKeyHit = true;
        public static CoroutineHandle ShowLoading(IEnumerable<object> loader, bool waitKeyHit = true)
        {
            waitForKeyHit = waitKeyHit;
            titleScreenOpen = true;
            TitleScreen.LoadState = null;
            return CoroutineManager.StartCoroutine(TitleScreen.DoLoading(loader));
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            if (NetworkMember != null) NetworkMember.Disconnect();
           
            base.OnExiting(sender, args);
        }

    }
}