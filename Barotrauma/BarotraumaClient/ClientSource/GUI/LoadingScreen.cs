using Barotrauma.Extensions;
using Barotrauma.Media;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class LoadingScreen
    {
        private readonly Texture2D defaultBackgroundTexture, overlay;
        private readonly SpriteSheet decorativeGraph, decorativeMap;
        private Texture2D currentBackgroundTexture;
        private readonly Sprite noiseSprite;

        private string randText = "";

        private Sprite languageSelectionCursor;
        private ScalableFont languageSelectionFont, languageSelectionFontCJK;

        private Video currSplashScreen;
        private DateTime videoStartTime;

        public struct PendingSplashScreen
        {
            public string Filename;
            public float Gain;
            public PendingSplashScreen(string filename, float gain)
            {
                Filename = filename;
                Gain = gain;
            }
        }

        private Queue<PendingSplashScreen> pendingSplashScreens = new Queue<PendingSplashScreen>();
        /// <summary>
        /// Triplet.first = filepath, Triplet.second = resolution, Triplet.third = audio gain
        /// </summary>
        public Queue<PendingSplashScreen> PendingSplashScreens
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

        private RichString selectedTip;
        private void SetSelectedTip(LocalizedString tip)
        {
            selectedTip = RichString.Rich(tip);
        }

        private readonly object loadMutex = new object();
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

        public LanguageIdentifier[] AvailableLanguages = null;

        public LoadingScreen(GraphicsDevice graphics)
        {
            defaultBackgroundTexture = TextureLoader.FromFile("Content/Map/LocationPortraits/AlienRuins.png");

            decorativeMap = new SpriteSheet("Content/Map/MapHUD.png", 6, 5, Vector2.Zero, sourceRect: new Rectangle(0, 0, 2048, 640));
            decorativeGraph = new SpriteSheet("Content/Map/MapHUD.png", 4, 10, Vector2.Zero, sourceRect: new Rectangle(1025, 1259, 1024, 732));

            overlay = TextureLoader.FromFile("Content/UI/MainMenuVignette.png");
            noiseSprite = new Sprite("Content/UI/noise.png", Vector2.Zero);
            DrawLoadingText = true;
            SetSelectedTip(TextManager.Get("LoadingScreenTip"));
        }

        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphics, float deltaTime)
        {
            if (GameSettings.CurrentConfig.EnableSplashScreen)
            {
                try
                {
                    DrawSplashScreen(spriteBatch, graphics);
                    if (currSplashScreen != null || PendingSplashScreens.Count > 0) { return; }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Playing splash screen video failed", e);
                    DisableSplashScreen();
                }
            }

            drawn = true;

            currentBackgroundTexture ??= defaultBackgroundTexture;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, samplerState: GUI.SamplerState);
            float scale = Math.Max(
                (float)GameMain.GraphicsWidth / currentBackgroundTexture.Width,
                (float)GameMain.GraphicsHeight / currentBackgroundTexture.Height) * 1.2f;
            float paddingX = currentBackgroundTexture.Width * scale - GameMain.GraphicsWidth;
            float paddingY = currentBackgroundTexture.Height * scale - GameMain.GraphicsHeight;

            double noiseT = (Timing.TotalTime * 0.02f);
            Vector2 pos = new Vector2((float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0.5f) - 0.5f);
            pos = new Vector2(pos.X * paddingX, pos.Y * paddingY);

            spriteBatch.Draw(currentBackgroundTexture,
                new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2 + pos,
                null, Color.White, 0.0f, new Vector2(currentBackgroundTexture.Width / 2, currentBackgroundTexture.Height / 2),
                scale, SpriteEffects.None, 0.0f);

            spriteBatch.Draw(overlay, Vector2.Zero, null, Color.White, 0.0f, Vector2.Zero, Math.Min(GameMain.GraphicsWidth / (float)overlay.Width, GameMain.GraphicsHeight / (float)overlay.Height), SpriteEffects.None, 0.0f);

            float noiseStrength = (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0);
            float noiseScale = (float)PerlinNoise.CalculatePerlin(noiseT * 5.0f, noiseT * 2.0f, 0) * 4.0f;
            noiseSprite.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                startOffset: new Vector2(Rand.Range(0.0f, noiseSprite.SourceRect.Width), Rand.Range(0.0f, noiseSprite.SourceRect.Height)),
                color: Color.White * noiseStrength * 0.1f,
                textureScale: Vector2.One * noiseScale);

            Vector2 textPos = new Vector2((int)(GameMain.GraphicsWidth * 0.05f), (int)(GameMain.GraphicsHeight * 0.75f));
            if (WaitForLanguageSelection)
            {
                DrawLanguageSelectionPrompt(spriteBatch, graphics);
            }
            else if (DrawLoadingText)
            {
                LocalizedString loadText;
                if (LoadState == 100.0f)
                {
#if DEBUG
                    if (GameSettings.CurrentConfig.AutomaticQuickStartEnabled || GameSettings.CurrentConfig.AutomaticCampaignLoadEnabled || (GameSettings.CurrentConfig.TestScreenEnabled && GameMain.FirstLoad))
                    {
                        loadText = "QUICKSTARTING ...";
                    }
                    else
                    {
#endif
                        loadText = TextManager.Get("PressAnyKey");
#if DEBUG
                    }
#endif
                }
                else
                {
                    loadText = TextManager.Get("Loading");
                    if (LoadState != null)
                    {
                        loadText += " " + (int)LoadState + " %";

#if DEBUG
                        if (GameMain.FirstLoad && GameMain.CancelQuickStart)
                        {
                            loadText += " (Quickstart aborted)";
                        }
#endif
                    }
                }

                if (GUIStyle.LargeFont.HasValue)
                {
                    GUIStyle.LargeFont.DrawString(spriteBatch, loadText.ToUpper(),
                        textPos,
                        Color.White);
                    textPos.Y += GUIStyle.LargeFont.MeasureString(loadText.ToUpper()).Y * 1.2f;
                }

                if (GUIStyle.Font.HasValue && selectedTip != null)
                {
                    string wrappedTip = ToolBox.WrapText(selectedTip.SanitizedValue, GameMain.GraphicsWidth * 0.3f, GUIStyle.Font.Value);
                    string[] lines = wrappedTip.Split('\n');
                    float lineHeight = GUIStyle.Font.MeasureString(selectedTip).Y;

                    if (selectedTip.RichTextData != null)
                    {
                        int rtdOffset = 0;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            GUIStyle.Font.DrawStringWithColors(spriteBatch, lines[i],
                                new Vector2(textPos.X, (int)(textPos.Y + i * lineHeight)),
                                Color.White,
                                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f, selectedTip.RichTextData.Value, rtdOffset);
                            rtdOffset += lines[i].Length;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < lines.Length; i++)
                        {
                            GUIStyle.Font.DrawString(spriteBatch, lines[i],
                                new Vector2(textPos.X, (int)(textPos.Y + i * lineHeight)),
                                new Color(228, 217, 167, 255));
                        }
                    }
                }
            }
            GUI.DrawMessageBoxesOnly(spriteBatch);
            spriteBatch.End();

            spriteBatch.Begin(blendState: BlendState.Additive);

            Vector2 decorativeScale = new Vector2(GameMain.GraphicsHeight / 1080.0f);

            float noiseVal = (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.25f, Timing.TotalTime * 0.5f, 0);
            if (!WaitForLanguageSelection)
            {
                decorativeGraph.Draw(spriteBatch, (int)(decorativeGraph.FrameCount * noiseVal),
                    new Vector2(GameMain.GraphicsWidth * 0.001f, textPos.Y),
                    Color.White, new Vector2(0, decorativeMap.FrameSize.Y), 0.0f, decorativeScale, SpriteEffects.FlipVertically);
            }

            decorativeMap.Draw(spriteBatch, (int)(decorativeMap.FrameCount * noiseVal),
                new Vector2(GameMain.GraphicsWidth * 0.99f, GameMain.GraphicsHeight * 0.01f),
                Color.White, new Vector2(decorativeMap.FrameSize.X, 0), 0.0f, decorativeScale, SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically);

            if (noiseVal < 0.2f)
            {
                //SCP-CB reference
                randText = (new string[] { "NIL", "black white gray", "Sometimes we would have had time to scream", "e8m106]af", "NO" }).GetRandomUnsynced();
            }
            else if (noiseVal < 0.3f)
            {
                randText = ToolBox.RandomSeed(9);
            }
            else if (noiseVal < 0.5f)
            {
                randText =
                    Rand.Int(100).ToString().PadLeft(2, '0') + " " +
                    Rand.Int(100).ToString().PadLeft(2, '0') + " " +
                    Rand.Int(100).ToString().PadLeft(2, '0') + " " +
                    Rand.Int(100).ToString().PadLeft(2, '0');
            }

            if (GUIStyle.LargeFont.HasValue)
            {
                Vector2 textSize = GUIStyle.LargeFont.MeasureString(randText);
                GUIStyle.LargeFont.DrawString(spriteBatch, randText,
                    new Vector2(GameMain.GraphicsWidth * 0.95f - textSize.X, GameMain.GraphicsHeight * 0.06f),
                    Color.White * (1.0f - noiseVal));
            }

            spriteBatch.End();
        }

        private void DrawLanguageSelectionPrompt(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
        {
            if (AvailableLanguages is null) { return; }

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

            Vector2 textPos = new Vector2((int)(GameMain.GraphicsWidth * 0.05f), (int)(GameMain.GraphicsHeight * 0.3f));
            Vector2 textSpacing = new Vector2(0.0f, GameMain.GraphicsHeight * 0.5f / AvailableLanguages.Length);
            foreach (LanguageIdentifier language in AvailableLanguages)
            {
                string localizedLanguageName = TextManager.GetTranslatedLanguageName(language);
                var font = TextManager.IsCJK(localizedLanguageName) ? languageSelectionFontCJK : languageSelectionFont;

                Vector2 textSize = font.MeasureString(localizedLanguageName);
                bool hover =
                    PlayerInput.MousePosition.X > textPos.X && PlayerInput.MousePosition.X < textPos.X + textSize.X &&
                    PlayerInput.MousePosition.Y > textPos.Y && PlayerInput.MousePosition.Y < textPos.Y + textSize.Y;

                font.DrawString(spriteBatch, localizedLanguageName, textPos,
                    hover ? Color.White : Color.White * 0.6f);
                if (hover && PlayerInput.PrimaryMouseButtonClicked())
                {
                    var config = GameSettings.CurrentConfig;
                    config.Language = language;
                    GameSettings.SetCurrentConfig(config);
                    //reload tip in the selected language
                    SetSelectedTip(TextManager.Get("LoadingScreenTip"));
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
                string fileName = newSplashScreen.Filename;
                try
                {
                    currSplashScreen = Video.Load(graphics, GameMain.SoundManager, fileName);
                    currSplashScreen.AudioGain = newSplashScreen.Gain;
                    videoStartTime = DateTime.Now;
                }
                catch (Exception e)
                {
                    DisableSplashScreen();
                    DebugConsole.ThrowError("Playing the splash screen \"" + fileName + "\" failed.", e);
                    PendingSplashScreens.Clear();
                    currSplashScreen = null;
                }

                if (currSplashScreen == null) { return; }
            }

            if (currSplashScreen.IsPlaying)
            {
                graphics.Clear(Color.Black);
                float videoAspectRatio = (float)currSplashScreen.Width / (float)currSplashScreen.Height;
                int width; int height;
                if (GameMain.GraphicsHeight * videoAspectRatio > GameMain.GraphicsWidth)
                {
                    width = GameMain.GraphicsWidth;
                    height = (int)(GameMain.GraphicsWidth / videoAspectRatio);
                }
                else
                {
                    width = (int)(GameMain.GraphicsHeight * videoAspectRatio);
                    height = GameMain.GraphicsHeight;
                }

                spriteBatch.Begin();
                spriteBatch.Draw(
                    currSplashScreen.GetTexture(),
                    destinationRectangle: new Rectangle(
                        GameMain.GraphicsWidth / 2 - width / 2,
                        GameMain.GraphicsHeight / 2 - height / 2,
                        width,
                        height),
                    sourceRectangle: new Rectangle(0, 0, currSplashScreen.Width, currSplashScreen.Height),
                    Color.White,
                    rotation: 0.0f,
                    origin: Vector2.Zero,
                    SpriteEffects.None,
                    layerDepth: 0.0f);
                spriteBatch.End();

                if (DateTime.Now > videoStartTime + new TimeSpan(0, 0, 0, 0, milliseconds: 500)
                    && GameMain.WindowActive
                    && (PlayerInput.KeyHit(Keys.Escape)
                        || PlayerInput.KeyHit(Keys.Space)
                        || PlayerInput.KeyHit(Keys.Enter)
                        || PlayerInput.PrimaryMouseButtonDown()))
                {
                    currSplashScreen.Dispose(); currSplashScreen = null;
                }
            }
            else if (DateTime.Now > videoStartTime + new TimeSpan(0, 0, 0, 0, milliseconds: 1500))
            {
                currSplashScreen.Dispose(); currSplashScreen = null;
            }
        }

        private void DisableSplashScreen()
        {
            var config = GameSettings.CurrentConfig;
            config.EnableSplashScreen = false;
            GameSettings.SetCurrentConfig(config);
        }
        
        bool drawn;
        public IEnumerable<CoroutineStatus> DoLoading(IEnumerable<CoroutineStatus> loader)
        {
            drawn = false;
            LoadState = null;
            SetSelectedTip(TextManager.Get("LoadingScreenTip"));
            currentBackgroundTexture = LocationType.Prefabs.Where(p => p.UsePortraitInRandomLoadingScreens).GetRandomUnsynced()?.GetPortrait(Rand.Int(int.MaxValue))?.Texture;

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
