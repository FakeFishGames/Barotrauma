using System;
using System.Diagnostics;
using System.Reflection;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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

        public static GameScreen            gameScreen;
        public static MainMenuScreen        mainMenuScreen;
        public static LobbyScreen           lobbyScreen;
        public static NetLobbyScreen        netLobbyScreen;
        public static EditMapScreen         editMapScreen;
        public static EditCharacterScreen   editCharacterScreen;

        public static GameSession gameSession;
                        
        public static GameClient client;
        public static GameServer server;

        public static ParticleManager particleManager;

        public static TextureLoader textureLoader;
        
        public static World world;

        public static Random localRandom;
        public static Random random;

        
        public Camera Cam
        {
            get { return gameScreen.Cam; }
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
            graphicsHeight = 700;

            //graphics.IsFullScreen = true;
            graphics.PreferredBackBufferWidth = graphicsWidth;
            graphics.PreferredBackBufferHeight = graphicsHeight;
            Content.RootDirectory = "Content";

            //graphics.SynchronizeWithVerticalRetrace = false;
            //graphics.ApplyChanges();

            frameCounter = new FrameCounter();

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

            gameScreen          =   new GameScreen(graphics.GraphicsDevice);
            mainMenuScreen      =   new MainMenuScreen(this); 
            lobbyScreen         =   new LobbyScreen();
            netLobbyScreen      =   new NetLobbyScreen();
            editMapScreen       =   new EditMapScreen();
            editCharacterScreen =   new EditCharacterScreen();

            mainMenuScreen.Select();            
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

            if (!DebugConsole.IsOpen || server != null || client != null) Screen.Selected.Update(deltaTime);

            if (server != null)
            {
                server.Update();
            }
            else if (client != null)
            {
                client.Update();
            }
            else
            {
                NetworkEvent.events.Clear();
            }           
        }


        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="deltaTime">elapsed time in seconds</param>
        protected override void Draw(GameTime gameTime)
        {
            //System.Diagnostics.Debug.WriteLine(deltaTime);
            //System.Diagnostics.Debug.WriteLine(gameTime.ElapsedGameTime.TotalSeconds);
            double deltaTime = gameTime.ElapsedGameTime.TotalSeconds;

            frameCounter.Update(deltaTime);

            Screen.Selected.Draw(deltaTime, GraphicsDevice, spriteBatch);            
        }

        protected override void OnExiting(object sender, EventArgs args)
        {
            if (client != null) client.Disconnect();

            base.OnExiting(sender, args);
        }

    }
}
