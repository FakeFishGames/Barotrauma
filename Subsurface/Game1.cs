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
    /// <summary>
    /// This is the main type for your game
    /// </summary>
    class Game1 : Game
    {
        GraphicsDeviceManager graphics;
        static int graphicsWidth, graphicsHeight;
        static SpriteBatch spriteBatch;

        public static FrameCounter frameCounter;

        public static readonly Version version = Assembly.GetEntryAssembly().GetName().Version;

        public static GameScreen            GameScreen;
        public static MainMenuScreen        MainMenuScreen;
        public static LobbyScreen           LobbyScreen;
        public static NetLobbyScreen        NetLobbyScreen;
        public static EditMapScreen         EditMapScreen;
        public static EditCharacterScreen   EditCharacterScreen;

        public static GameSession GameSession;
                        
        public static GameClient Client;
        public static GameServer Server;

        public static ParticleManager particleManager;

        public static TextureLoader textureLoader;
        
        public static World world;

        public static Random localRandom;
        public static Random random;

        private Stopwatch renderTimer;
        public static int renderTimeElapsed;

        
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
        
        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);

            graphicsWidth = 1280;
            graphicsHeight = 720;

            //graphics.IsFullScreen = true;
            graphics.PreferredBackBufferWidth = graphicsWidth;
            graphics.PreferredBackBufferHeight = graphicsHeight;
            Content.RootDirectory = "Content";

            //graphics.SynchronizeWithVerticalRetrace = false;
            //graphics.ApplyChanges();

            frameCounter = new FrameCounter();

            renderTimer = new Stopwatch();

            IsMouseVisible = true;

            IsFixedTimeStep = false;
            //TargetElapsedTime = new TimeSpan(0, 0, 0, 0, 55);

            world = new World(new Vector2(0, -9.82f));
            Settings.VelocityIterations = 2;
            Settings.PositionIterations = 1;

            random = new Random();
            localRandom = new Random();
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            // TODO: Add your initialization logic here
            base.Initialize();

            particleManager = new ParticleManager("Content/Particles/prefabs.xml", Cam);

            GameMode.Init();
            GUIComponent.Init(Window);
            DebugConsole.Init(Window);
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

            Job.LoadAll("Content/Characters/Jobs.xml");

            StructurePrefab.LoadAll("Content/Map/StructurePrefabs.xml");
            ItemPrefab.LoadAll();
                        
            AmbientSoundManager.Init("Content/Sounds/Sounds.xml");

            Map.PreloadMaps("Content/SavedMaps");
            GameScreen          =   new GameScreen(graphics.GraphicsDevice);
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

            if (!DebugConsole.IsOpen || Server != null || Client != null) Screen.Selected.Update(deltaTime);

            GUI.Update((float)deltaTime);

            if (Server != null)
            {
                Server.Update();
            }
            else if (Client != null)
            {
                Client.Update();
            }
            else
            {
                NetworkEvent.events.Clear();
            }           
        }


        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        protected override void Draw(GameTime gameTime)
        {
            renderTimer.Restart();

            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            frameCounter.Update(deltaTime);

            Screen.Selected.Draw(deltaTime, GraphicsDevice, spriteBatch);

            renderTimeElapsed = (int)renderTimer.Elapsed.Ticks;
            renderTimer.Stop();
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            if (Client != null) Client.Disconnect();

            base.OnExiting(sender, args);
        }

    }
}
