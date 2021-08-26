using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Media;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class LoadingScreen
    {
        private readonly Texture2D defaultBackgroundTexture, overlay;
        private readonly SpriteSheet decorativeGraph, decorativeMap;
        private Texture2D currentBackgroundTexture;
        private Sprite noiseSprite;

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

        private string selectedTip;
        private List<RichTextData> selectedTipRichTextData;
        private bool selectedTipRichTextUnparsed;
        private void SetSelectedTip(string tip)
        {
            selectedTip = tip;
            selectedTipRichTextData = null;
            selectedTipRichTextUnparsed = true;
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

        public LoadingScreen(GraphicsDevice graphics)
        {
            defaultBackgroundTexture = TextureLoader.FromFile("Content/Map/LocationPortraits/AlienRuins.png");

            decorativeMap = new SpriteSheet("Content/Map/MapHUD.png", 6, 5, Vector2.Zero, sourceRect: new Rectangle(0, 0, 2048, 640));
            decorativeGraph = new SpriteSheet("Content/Map/MapHUD.png", 4, 10, Vector2.Zero, sourceRect: new Rectangle(1025, 1259, 1024, 732));

            overlay = TextureLoader.FromFile("Content/UI/LoadingScreenOverlay.png");
            noiseSprite = new Sprite("Content/UI/noise.png", Vector2.Zero);
            DrawLoadingText = true;
            SetSelectedTip(TextManager.Get("LoadingScreenTip", true));
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

            currentBackgroundTexture ??= defaultBackgroundTexture;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, samplerState: GUI.SamplerState);

            float scale = (GameMain.GraphicsWidth / (float)currentBackgroundTexture.Width) * 1.2f;
            float paddingX = currentBackgroundTexture.Width * scale - GameMain.GraphicsWidth;
            float paddingY = currentBackgroundTexture.Height * scale - GameMain.GraphicsHeight;

            double noiseT = (Timing.TotalTime * 0.02f);
            Vector2 pos = new Vector2((float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0) - 0.5f, (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0.5f) - 0.5f);
            pos = new Vector2(pos.X * paddingX, pos.Y * paddingY);

            spriteBatch.Draw(currentBackgroundTexture,
                new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2 + pos,
                null, Color.White, 0.0f, new Vector2(currentBackgroundTexture.Width / 2, currentBackgroundTexture.Height / 2),
                scale, SpriteEffects.None, 0.0f);

            spriteBatch.Draw(overlay, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), null, Color.White, 0.0f, Vector2.Zero, SpriteEffects.None, 0.0f);

            float noiseStrength = (float)PerlinNoise.CalculatePerlin(noiseT, noiseT, 0);
            float noiseScale = (float)PerlinNoise.CalculatePerlin(noiseT * 5.0f, noiseT * 2.0f, 0) * 4.0f;
            noiseSprite.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                startOffset: new Vector2(Rand.Range(0.0f, noiseSprite.SourceRect.Width), Rand.Range(0.0f, noiseSprite.SourceRect.Height)),
                color: Color.White * noiseStrength * 0.1f,
                textureScale: Vector2.One * noiseScale);

            titleSprite?.Draw(spriteBatch, new Vector2(GameMain.GraphicsWidth * 0.05f, GameMain.GraphicsHeight * 0.125f), 
                Color.White, origin: new Vector2(0.0f, titleSprite.SourceRect.Height / 2.0f), 
                scale: GameMain.GraphicsHeight / 2000.0f);

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
#if DEBUG
                        if (GameMain.Config.AutomaticQuickStartEnabled || GameMain.Config.AutomaticCampaignLoadEnabled || GameMain.Config.TestScreenEnabled && GameMain.FirstLoad)
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
                        }
                    }
                    if (GUI.LargeFont != null)
                    {
                        GUI.LargeFont.DrawString(spriteBatch, loadText.ToUpper(),
                            new Vector2(GameMain.GraphicsWidth / 2.0f - GUI.LargeFont.MeasureString(loadText.ToUpper()).X / 2.0f, GameMain.GraphicsHeight * 0.75f),
                            Color.White);
                    }
                }

                if (GUI.Font != null && selectedTip != null)
                {
                    if (selectedTipRichTextUnparsed)
                    {
                        selectedTipRichTextData = RichTextData.GetRichTextData(selectedTip, out selectedTip);
                        selectedTipRichTextUnparsed = false;
                    }

                    string wrappedTip = ToolBox.WrapText(selectedTip, GameMain.GraphicsWidth * 0.5f, GUI.Font);
                    string[] lines = wrappedTip.Split('\n');
                    float lineHeight = GUI.Font.MeasureString(selectedTip).Y;

                    if (selectedTipRichTextData != null)
                    {
                        int rtdOffset = 0;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            GUI.Font.DrawStringWithColors(spriteBatch, lines[i],
                                new Vector2((int)(GameMain.GraphicsWidth / 2.0f - GUI.Font.MeasureString(lines[i]).X / 2.0f), (int)(GameMain.GraphicsHeight * 0.8f + i * lineHeight)), Color.White,
                                0f, Vector2.Zero, 1f, SpriteEffects.None, 0f, selectedTipRichTextData, rtdOffset);
                            rtdOffset += lines[i].Length;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < lines.Length; i++)
                        {
                            GUI.Font.DrawString(spriteBatch, lines[i],
                                new Vector2((int)(GameMain.GraphicsWidth / 2.0f - GUI.Font.MeasureString(lines[i]).X / 2.0f), (int)(GameMain.GraphicsHeight * 0.8f + i * lineHeight)), Color.White);
                        }
                    }
                }

            }
            spriteBatch.End();

            spriteBatch.Begin(blendState: BlendState.Additive);

            Vector2 decorativeScale = new Vector2(GameMain.GraphicsHeight / 1080.0f);

            float noiseVal = (float)PerlinNoise.CalculatePerlin(Timing.TotalTime * 0.25f, Timing.TotalTime * 0.5f, 0);
            decorativeGraph.Draw(spriteBatch, (int)(decorativeGraph.FrameCount * noiseVal),
                new Vector2(GameMain.GraphicsWidth * 0.001f, GameMain.GraphicsHeight * 0.24f),
                Color.White, Vector2.Zero, 0.0f, decorativeScale, SpriteEffects.FlipVertically);
            
            decorativeMap.Draw(spriteBatch, (int)(decorativeMap.FrameCount * noiseVal),
                new Vector2(GameMain.GraphicsWidth * 0.99f, GameMain.GraphicsHeight * 0.66f),
                Color.White, decorativeMap.FrameSize.ToVector2(), 0.0f, decorativeScale);

            if (noiseVal < 0.2f)
            {
                //SCP-CB reference
                randText = (new string[] { "NIL", "black white gray", "Sometimes we would have had time to scream", "e8m106]af", "NO" }).GetRandom();
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

            GUI.LargeFont?.DrawString(spriteBatch, randText,
                new Vector2(GameMain.GraphicsWidth - decorativeMap.FrameSize.X * decorativeScale.X * 0.8f, GameMain.GraphicsHeight * 0.57f),
                Color.White * (1.0f - noiseVal));

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
                if (hover && PlayerInput.PrimaryMouseButtonClicked())
                {
                    GameMain.Config.Language = language;
                    //reload tip in the selected language
                    SetSelectedTip(TextManager.Get("LoadingScreenTip", true));
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
                string fileName = newSplashScreen.Filename;
                try
                {
                    currSplashScreen = Video.Load(graphics, GameMain.SoundManager, fileName);
                    currSplashScreen.AudioGain = newSplashScreen.Gain;
                    videoStartTime = DateTime.Now;
                }
                catch (Exception e)
                {
                    GameMain.Config.EnableSplashScreen = false;
                    DebugConsole.ThrowError("Playing the splash screen \"" + fileName + "\" failed.", e);
                    PendingSplashScreens.Clear();
                    currSplashScreen = null;
                }

                if (currSplashScreen == null) { return; }
            }

            if (currSplashScreen.IsPlaying)
            {
                spriteBatch.Begin();
                spriteBatch.Draw(currSplashScreen.GetTexture(), new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                spriteBatch.End();

                if (DateTime.Now > videoStartTime + new TimeSpan(0, 0, 0, 0, milliseconds: 500) && GameMain.WindowActive && (PlayerInput.KeyHit(Keys.Escape) || PlayerInput.KeyHit(Keys.Space) || PlayerInput.KeyHit(Keys.Enter) || PlayerInput.PrimaryMouseButtonDown()))
                {
                    currSplashScreen.Dispose(); currSplashScreen = null;
                }
            }
            else if (DateTime.Now > videoStartTime + new TimeSpan(0, 0, 0, 0, milliseconds: 1500))
            {
                currSplashScreen.Dispose(); currSplashScreen = null;
            }
        }

        bool drawn;
        public IEnumerable<object> DoLoading(IEnumerable<object> loader)
        {
            drawn = false;
            LoadState = null;
            SetSelectedTip(TextManager.Get("LoadingScreenTip", true));
            currentBackgroundTexture = LocationType.List.GetRandom()?.GetPortrait(Rand.Int(int.MaxValue))?.Texture;
            
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
