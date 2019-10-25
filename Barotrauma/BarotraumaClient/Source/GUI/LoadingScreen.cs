using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Media;
using System.Linq;

namespace Barotrauma
{
    class LoadingScreen
    {
        private Texture2D backgroundTexture;

        private RenderTarget2D renderTarget;

        private Sprite languageSelectionCursor;
        private ScalableFont languageSelectionFont, languageSelectionFontCJK;

        private Video currSplashScreen;
        private DateTime videoStartTime;

        private Queue<Triplet<string, Point, float>> pendingSplashScreens = new Queue<Triplet<string, Point, float>>();
        /// <summary>
        /// Triplet.first = filepath, Triplet.second = resolution, Triplet.third = audio gain
        /// </summary>
        public Queue<Triplet<string, Point, float>> PendingSplashScreens
        {
            get
            {
                lock (loadMutex)
                {
                    return pendingSplashScreens;
                }
            }
            set
            {
                lock (loadMutex)
                {
                    pendingSplashScreens = value;
                }
            }
        }

        public bool PlayingSplashScreen
        {
            get
            {
                lock (loadMutex)
                {
                    return currSplashScreen != null || pendingSplashScreens.Count > 0;
                }
            }
        }

        private float state;
        
        private string selectedTip;

        public Vector2 BackgroundPosition;

        public Vector2 TitlePosition;

        private object loadMutex = new object();
        private float? loadState;
        
        public float? LoadState
        {
            get
            {
                lock (loadMutex)
                {
                    return loadState;
                }
            }        
            set 
            {
                lock (loadMutex)
                {
                    loadState = value;
                    DrawLoadingText = true;
                }
            }
        }

        public bool DrawLoadingText
        {
            get;
            set;
        }

        public bool WaitForLanguageSelection
        {
            get;
            set;
        }

        public LoadingScreen(GraphicsDevice graphics)
        {
            backgroundTexture = TextureLoader.FromFile("Content/UI/titleBackground.png");

            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            GameMain.Instance.OnResolutionChanged += () =>
            {
                renderTarget?.Dispose();
                renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            };

            DrawLoadingText = true;
            selectedTip = TextManager.Get("LoadingScreenTip", true);
        }

        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphics, float deltaTime)
        {
            if (GameMain.Config.EnableSplashScreen)
            {
                try
                {
                    DrawSplashScreen(spriteBatch, graphics);
                    if (currSplashScreen != null || PendingSplashScreens.Count > 0) { return; }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Playing splash screen video failed", e);
                    GameMain.Config.EnableSplashScreen = false;
                }
            }
                        
            var titleStyle = GUI.Style?.GetComponentStyle("TitleText");
            Sprite titleSprite = null;
            if (!WaitForLanguageSelection && titleStyle != null && titleStyle.Sprites.ContainsKey(GUIComponent.ComponentState.None))
            {
                titleSprite = titleStyle.Sprites[GUIComponent.ComponentState.None].First()?.Sprite;
            }

            drawn = true;

            graphics.SetRenderTarget(renderTarget);

            float backgroundScale = GameMain.GraphicsHeight / 1500.0f;
            float titleScale = MathHelper.SmoothStep(0.8f, 1.0f, state / 10.0f) * GameMain.GraphicsHeight / 1000.0f;

            state += deltaTime;

            if (DrawLoadingText)
            {
                BackgroundPosition = new Vector2(GameMain.GraphicsWidth * 0.3f, GameMain.GraphicsHeight * 0.45f);
                TitlePosition = new Vector2(GameMain.GraphicsWidth * 0.5f, GameMain.GraphicsHeight * 0.45f);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, samplerState: GUI.SamplerState);
            graphics.Clear(Color.Black);

            spriteBatch.Draw(backgroundTexture, BackgroundPosition, null, Color.White * Math.Min(state / 5.0f, 1.0f), 0.0f,
                new Vector2(backgroundTexture.Width / 2.0f, backgroundTexture.Height / 2.0f),
                backgroundScale * 1.5f, SpriteEffects.None, 0.2f);
            
            titleSprite?.Draw(spriteBatch, TitlePosition, Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), scale: titleScale);
            
            spriteBatch.End();

            graphics.SetRenderTarget(null);

            if (WaterRenderer.Instance != null)
            {
                WaterRenderer.Instance.ScrollWater(Vector2.One * 10.0f, deltaTime);
                WaterRenderer.Instance.RenderWater(spriteBatch, renderTarget, null);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, samplerState: GUI.SamplerState);

            titleSprite?.Draw(spriteBatch, TitlePosition, Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), scale: titleScale);

            if (WaitForLanguageSelection)
            {
                DrawLanguageSelectionPrompt(spriteBatch, graphics);
            }
            else if (DrawLoadingText)
            {
                if (TextManager.Initialized)
                {
                    string loadText;
                    if (LoadState == 100.0f)
                    {
                        loadText = TextManager.Get("PressAnyKey");
                    }
                    else
                    {
                        loadText = TextManager.Get("Loading");
                        if (LoadState != null)
                        {
                            loadText += " " + (int)LoadState + " %";
                        }
                    }
                    if (GUI.LargeFont != null)
                    {
                        GUI.LargeFont.DrawString(spriteBatch, loadText.ToUpper(),
                            new Vector2(GameMain.GraphicsWidth / 2.0f - GUI.LargeFont.MeasureString(loadText).X / 2.0f, GameMain.GraphicsHeight * 0.7f),
                            Color.White);
                    }
                }

                if (GUI.Font != null && selectedTip != null)
                {
                    string wrappedTip = ToolBox.WrapText(selectedTip, GameMain.GraphicsWidth * 0.5f, GUI.Font);
                    string[] lines = wrappedTip.Split('\n');
                    float lineHeight = GUI.Font.MeasureString(selectedTip).Y;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        GUI.Font.DrawString(spriteBatch, lines[i],
                            new Vector2((int)(GameMain.GraphicsWidth / 2.0f - GUI.Font.MeasureString(lines[i]).X / 2.0f), (int)(GameMain.GraphicsHeight * 0.78f + i * lineHeight)), Color.White);
                    }
                }

            }
            spriteBatch.End();
        }

        private void DrawLanguageSelectionPrompt(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
        {
            if (languageSelectionFont == null)
            {
                languageSelectionFont = new ScalableFont("Content/Fonts/NotoSans/NotoSans-Bold.ttf", 
                    (uint)(30 * (GameMain.GraphicsHeight / 1080.0f)), graphicsDevice);
            }
            if (languageSelectionFontCJK == null)
            {
                languageSelectionFontCJK = new ScalableFont("Content/Fonts/NotoSans/NotoSansCJKsc-Bold.otf", 
                    (uint)(30 * (GameMain.GraphicsHeight / 1080.0f)), graphicsDevice, dynamicLoading: true);
            }
            if (languageSelectionCursor == null)
            {
                languageSelectionCursor = new Sprite("Content/UI/cursor.png", Vector2.Zero);
            }

            Vector2 textPos = new Vector2(GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight * 0.3f);
            Vector2 textSpacing = new Vector2(0.0f, (GameMain.GraphicsHeight * 0.5f) / TextManager.AvailableLanguages.Count());
            foreach (string language in TextManager.AvailableLanguages)
            {
                string localizedLanguageName = TextManager.GetTranslatedLanguageName(language);
                var font = TextManager.IsCJK(localizedLanguageName) ? languageSelectionFontCJK : languageSelectionFont;

                Vector2 textSize = font.MeasureString(localizedLanguageName);
                bool hover = 
                    Math.Abs(PlayerInput.MousePosition.X - textPos.X) < textSize.X / 2 && 
                    Math.Abs(PlayerInput.MousePosition.Y - textPos.Y) < textSpacing.Y / 2;

                font.DrawString(spriteBatch, localizedLanguageName, textPos - textSize / 2, 
                    hover ? Color.White : Color.White * 0.6f);
                if (hover && PlayerInput.LeftButtonClicked())
                {
                    GameMain.Config.Language = language;
                    //reload tip in the selected language
                    selectedTip = TextManager.Get("LoadingScreenTip", true);
                    GameMain.Config.SetDefaultBindings(legacy: false);
                    GameMain.Config.CheckBindings(useDefaults: true);
                    WaitForLanguageSelection = false;
                    languageSelectionFont?.Dispose(); languageSelectionFont = null;
                    languageSelectionFontCJK?.Dispose(); languageSelectionFontCJK = null;
                    break;
                }

                textPos += textSpacing;
            }

            languageSelectionCursor.Draw(spriteBatch, PlayerInput.LatestMousePosition, scale: 0.5f);
        }

        private void DrawSplashScreen(SpriteBatch spriteBatch, GraphicsDevice graphics)
        {
            if (currSplashScreen == null && PendingSplashScreens.Count == 0) { return; }

            if (currSplashScreen == null)
            {
                var newSplashScreen = PendingSplashScreens.Dequeue();
                string fileName = newSplashScreen.First;
                Point resolution = newSplashScreen.Second;
                try
                {
                    currSplashScreen = new Video(graphics, GameMain.SoundManager, fileName, (uint)resolution.X, (uint)resolution.Y);
                    currSplashScreen.AudioGain = newSplashScreen.Third;
                    videoStartTime = DateTime.Now;
                }
                catch (Exception e)
                {
                    GameMain.Config.EnableSplashScreen = false;
                    DebugConsole.ThrowError("Playing the splash screen \"" + fileName + "\" failed.", e);
                    PendingSplashScreens.Clear();
                    currSplashScreen = null;
                }
            }

            if (currSplashScreen.IsPlaying)
            {
                spriteBatch.Begin();
                spriteBatch.Draw(currSplashScreen.GetTexture(), new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                spriteBatch.End();

                if (GameMain.WindowActive && (PlayerInput.KeyHit(Keys.Space) || PlayerInput.KeyHit(Keys.Enter) || PlayerInput.LeftButtonDown()))
                {
                    currSplashScreen.Dispose(); currSplashScreen = null;
                }
            }
            else if (DateTime.Now > videoStartTime + new TimeSpan(0, 0, 0, 0, milliseconds: 500))
            {
                currSplashScreen.Dispose(); currSplashScreen = null;
            }
        }

        bool drawn;
        public IEnumerable<object> DoLoading(IEnumerable<object> loader)
        {
            drawn = false;
            LoadState = null;
            selectedTip = TextManager.Get("LoadingScreenTip", true);
            
            while (!drawn)
            {
                yield return CoroutineStatus.Running;
            }

            CoroutineManager.StartCoroutine(loader);
            
            yield return CoroutineStatus.Running;

            while (CoroutineManager.IsCoroutineRunning(loader.ToString()))
            {
                yield return CoroutineStatus.Running;
            }

            LoadState = 100.0f;

            yield return CoroutineStatus.Success;
        }
    }
}
