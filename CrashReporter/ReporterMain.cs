using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma;
using System.IO;

namespace CrashReporter
{
    /// <summary>
    /// This is the main type for your game
    /// </summary>
    public class ReporterMain : Game
    {
        GraphicsDeviceManager graphics;
        SpriteBatch spriteBatch;
        
        int graphicsWidth, graphicsHeight;

        Texture2D backgroundTexture, titleTexture;

        GUIFrame guiRoot;

        string crashReport;

        public ReporterMain()
            : base()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = 640;
            graphics.PreferredBackBufferHeight = 360;
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
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            graphicsWidth = GraphicsDevice.Viewport.Width;
            graphicsHeight = GraphicsDevice.Viewport.Height;

            TextureLoader.Init(GraphicsDevice);

            GUI.Init(Content);

            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            GUI.LoadContent(GraphicsDevice);

            backgroundTexture = TextureLoader.FromFile("Content/UI/titleBackground.png");
            titleTexture = TextureLoader.FromFile("Content/UI/titleText.png");

            guiRoot = new GUIFrame(new Rectangle(0, 0, graphicsWidth, graphicsHeight), Color.Transparent);
            guiRoot.Padding = new Vector4(40.0f, 40.0f, 40.0f, 40.0f);
            
            GUIListBox infoBox = new GUIListBox(new Rectangle(0, 0, 330, 150), GUI.Style, guiRoot);
            infoBox.Visible = false;

            crashReport = System.IO.File.ReadAllText("CrashReport.txt");

            string wrappedText = ToolBox.WrapText(crashReport, infoBox.Rect.Width, GUI.SmallFont);

            int lineHeight = (int)GUI.SmallFont.MeasureString(" ").Y;

            string[] lines = wrappedText.Split('\n');
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, lineHeight),
                    line, GUI.Style,
                    Alignment.TopLeft, Alignment.TopLeft,
                    infoBox, false, GUI.SmallFont);
                textBlock.CanBeFocused = false;
            }

            GUIButton sendButton = new GUIButton(new Rectangle(0, 0, 100, 30), "SEND", Alignment.BottomRight, GUI.Style, guiRoot);
            //se.OnClicked = LaunchClick;
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            // TODO: Unload any non ContentManager content here
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // TODO: Add your update logic here

            base.Update(gameTime);

            guiRoot.Update((float)gameTime.ElapsedGameTime.TotalMilliseconds);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            spriteBatch.Begin();

            guiRoot.Draw(spriteBatch);

            spriteBatch.End();
        }

        private void SendReport()
        {
        }
    }
}
