using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using Subsurface.Particles;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using System.Xml;

namespace Subsurface
{
    class GameMain : Game
    {
        public static GraphicsDeviceManager Graphics;
        static int graphicsWidth, graphicsHeight;
        static SpriteBatch spriteBatch;

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

        public static TitleScreen TitleScreen;
        private bool titleScreenOpen;

        public static GameSettings Config;

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
            Graphics = new GraphicsDeviceManager(this);

            Config = new GameSettings("config.xml");
            
            graphicsWidth = Config.GraphicsWidth;
            graphicsHeight = Config.GraphicsHeight;

            Graphics.IsFullScreen = Config.FullScreenEnabled;
            Graphics.PreferredBackBufferWidth = graphicsWidth;
            Graphics.PreferredBackBufferHeight = graphicsHeight;
            Content.RootDirectory = "Content";

            //graphics.SynchronizeWithVerticalRetrace = false;
            //graphics.ApplyChanges();

            FrameCounter = new FrameCounter();

            //renderTimer = new Stopwatch();

            IsMouseVisible = true;

            IsFixedTimeStep = false;
            //TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 55);

            World = new World(new Vector2(0, -9.82f));
            FarseerPhysics.Settings.VelocityIterations = 2;
            FarseerPhysics.Settings.PositionIterations = 1;
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

            //Event.Init("Content/randomevents.xml");
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
            TitleScreen = new TitleScreen(GraphicsDevice);

            CoroutineManager.StartCoroutine(Load());
        }

        private float loadState = 0.0f;
        private IEnumerable<object> Load()
        {
            GUI.Init(Content);

            sw = new Stopwatch();

            LightManager = new Lights.LightManager(GraphicsDevice);

            Hull.renderer = new WaterRenderer(GraphicsDevice);
        loadState = 1.0f;
        yield return CoroutineStatus.Running;

            GUI.LoadContent(GraphicsDevice);
        loadState = 2.0f;
        yield return CoroutineStatus.Running;

            MapEntityPrefab.Init();
        loadState = 10.0f;
        yield return CoroutineStatus.Running;

            JobPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Jobs));
        loadState = 15.0f;
        yield return CoroutineStatus.Running;

            StructurePrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Structure));
        loadState = 25.0f;
        yield return CoroutineStatus.Running;

            ItemPrefab.LoadAll(SelectedPackage.GetFilesOfType(ContentType.Item));
        loadState = 40.0f;
        yield return CoroutineStatus.Running;

            Debug.WriteLine("sounds");
            CoroutineManager.StartCoroutine(AmbientSoundManager.Init());
        loadState = 70.0f;
        yield return CoroutineStatus.Running;

            GameModePreset.Init();

            Submarine.Preload("Content/SavedMaps");
        loadState = 80.0f;
        yield return CoroutineStatus.Running;

            GameScreen          =   new GameScreen(Graphics.GraphicsDevice);
        loadState = 90.0f;
        yield return CoroutineStatus.Running;

            MainMenuScreen      =   new MainMenuScreen(this); 
            LobbyScreen         =   new LobbyScreen();

            NetLobbyScreen      =   new NetLobbyScreen();
            ServerListScreen    =   new ServerListScreen();

            EditMapScreen       =   new EditMapScreen();
            EditCharacterScreen =   new EditCharacterScreen();

        yield return CoroutineStatus.Running;

            ParticleManager = new ParticleManager("Content/Particles/ParticlePrefabs.xml", Cam);
        yield return CoroutineStatus.Running;

            GUIComponent.Init(Window);
            DebugConsole.Init(Window);
        yield return CoroutineStatus.Running;

            LocationType.Init("Content/Map/locationTypes.xml");
            MainMenuScreen.Select();
        yield return CoroutineStatus.Running;

            loadState = 100.0f;
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
            base.Update(gameTime);

            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;
            PlayerInput.Update(deltaTime);

            if (loadState >= 100.0f && !titleScreenOpen)
            {
                if (PlayerInput.KeyHit(Keys.Escape)) GUI.TogglePauseMenu();

                DebugConsole.Update(this, (float)deltaTime);

                if ((!DebugConsole.IsOpen && !GUI.PauseMenuOpen) || (NetworkMember != null && NetworkMember.GameStarted)) Screen.Selected.Update(deltaTime);

                GUI.Update((float)deltaTime);

                if (NetworkMember != null)
                {
                    NetworkMember.Update((float)deltaTime);
                }
                else
                {
                    NetworkEvent.events.Clear();
                }
            }

            CoroutineManager.Update((float)deltaTime);
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
                TitleScreen.Draw(spriteBatch, GraphicsDevice, loadState, (float)deltaTime);
                if (loadState>=100.0f && (PlayerInput.GetKeyboardState.GetPressedKeys().Length>0 || PlayerInput.LeftButtonClicked()))
                {
                    titleScreenOpen = false;
                }
            }
            else if (loadState >= 100.0f)
            {
                Screen.Selected.Draw(deltaTime, GraphicsDevice, spriteBatch);
            }

            double elapsed =sw.Elapsed.TotalSeconds;
            if (elapsed < Physics.step)
            {
                System.Threading.Thread.Sleep((int)((Physics.step - elapsed) * 1000.0));
            }
            sw.Restart();
        }

        Stopwatch sw;

        protected override void OnExiting(object sender, EventArgs args)
        {
            if (NetworkMember != null) NetworkMember.Disconnect();

            base.OnExiting(sender, args);
        }

    }
}