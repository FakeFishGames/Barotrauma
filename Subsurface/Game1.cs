using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using Subsurface.Particles;

namespace Subsurface
{
    class Game1 : Game
    {
        public static GraphicsDeviceManager Graphics;
        static int graphicsWidth, graphicsHeight;
        static SpriteBatch spriteBatch;

        public static bool DebugDraw;

        public static GraphicsDevice CurrGraphicsDevice;

        public static FrameCounter frameCounter;

        public static readonly Version Version = Assembly.GetEntryAssembly().GetName().Version;

        public static GameScreen            GameScreen;
        public static MainMenuScreen        MainMenuScreen;
        public static LobbyScreen           LobbyScreen;
        public static NetLobbyScreen        NetLobbyScreen;
        public static EditMapScreen         EditMapScreen;
        public static EditCharacterScreen   EditCharacterScreen;

        public static Level Level;

        public static GameSession GameSession;

        public static NetworkMember NetworkMember;

        public static ParticleManager particleManager;

        public static TextureLoader textureLoader;
        
        public static World World;

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
                
        public Game1()
        {
            Graphics = new GraphicsDeviceManager(this);
            
            graphicsWidth = 1280;
            graphicsHeight = 720;

            //graphics.IsFullScreen = true;
            Graphics.PreferredBackBufferWidth = graphicsWidth;
            Graphics.PreferredBackBufferHeight = graphicsHeight;
            Content.RootDirectory = "Content";

            //graphics.SynchronizeWithVerticalRetrace = false;
            //graphics.ApplyChanges();

            frameCounter = new FrameCounter();

            //renderTimer = new Stopwatch();

            IsMouseVisible = true;

            IsFixedTimeStep = false;
            //TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 55);

            World = new World(new Vector2(0, -9.82f));
            Settings.VelocityIterations = 2;
            Settings.PositionIterations = 1;
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

            particleManager = new ParticleManager("Content/Particles/prefabs.xml", Cam);

            GameMode.Init();
            GUIComponent.Init(Window);
            DebugConsole.Init(Window);

            LocationType.Init("Content/Map/locationTypes.xml");
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
            textureLoader = new TextureLoader(GraphicsDevice);
                                   
            Hull.renderer = new WaterRenderer(GraphicsDevice);

            GUI.font = Content.Load<SpriteFont>("SpriteFont1");
            GUI.LoadContent(GraphicsDevice);

            MapEntityPrefab.Init();

            JobPrefab.LoadAll("Content/Characters/Jobs.xml");

            StructurePrefab.LoadAll("Content/Map/StructurePrefabs.xml");
            ItemPrefab.LoadAll();
                        
            AmbientSoundManager.Init("Content/Sounds/Sounds.xml");

            Submarine.Preload("Content/SavedMaps");
            GameScreen          =   new GameScreen(Graphics.GraphicsDevice);
            MainMenuScreen      =   new MainMenuScreen(this); 
            LobbyScreen         =   new LobbyScreen();
            NetLobbyScreen      =   new NetLobbyScreen();
            EditMapScreen       =   new EditMapScreen();
            EditCharacterScreen =   new EditCharacterScreen();


            MainMenuScreen.Select();            
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


            //if (PlayerInput.KeyDown(Keys.Escape)) Quit();

            DebugConsole.Update(this, (float)deltaTime);

            if (!DebugConsole.IsOpen || NetworkMember != null) Screen.Selected.Update(deltaTime);

            GUI.Update((float)deltaTime);

            if (NetworkMember != null)
            {
                NetworkMember.Update();
            }
            else
            {
                NetworkEvent.events.Clear();
            }

            CoroutineManager.Update();
        }


        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        protected override void Draw(GameTime gameTime)
        {
            //renderTimer.Restart();

            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            frameCounter.Update(deltaTime);

            Screen.Selected.Draw(deltaTime, GraphicsDevice, spriteBatch);

            //renderTimeElapsed = (int)renderTimer.Elapsed.Ticks;
            //renderTimer.Stop();
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            if (NetworkMember != null) NetworkMember.Disconnect();

            base.OnExiting(sender, args);
        }

    }
}
