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

        private Video splashScreen;
        public Video SplashScreen
        {
            get
            {
                lock (loadMutex)
                {
                    return splashScreen;
                }
            }
            set
            {
                lock (loadMutex)
                {
                    splashScreen = value;
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
                    DrawSplashScreen(spriteBatch);
                    if (SplashScreen != null && SplashScreen.IsPlaying) return;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Playing splash screen video failed", e);
                    GameMain.Config.EnableSplashScreen = false;
                }
            }

            var titleStyle = GUI.Style?.GetComponentStyle("TitleText");
            Sprite titleSprite = null;
            if (titleStyle != null && titleStyle.Sprites.ContainsKey(GUIComponent.ComponentState.None))
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

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
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

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            titleSprite?.Draw(spriteBatch, TitlePosition, Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), scale: titleScale);

            if (DrawLoadingText)
            {
                string loadText = "";
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

                if (GUI.Font != null && selectedTip != null)
                {
                    string wrappedTip = ToolBox.WrapText(selectedTip, GameMain.GraphicsWidth * 0.5f, GUI.Font);
                    string[] lines = wrappedTip.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        GUI.Font.DrawString(spriteBatch, lines[i],
                            new Vector2(GameMain.GraphicsWidth / 2.0f - GUI.Font.MeasureString(lines[i]).X / 2.0f, GameMain.GraphicsHeight * 0.78f + i * 15), Color.White);
                    }
                }

            }
            spriteBatch.End();

        }

        private void DrawSplashScreen(SpriteBatch spriteBatch)
        {
            if (SplashScreen != null)
            {
                if (SplashScreen.IsPlaying)
                {
                    spriteBatch.Begin();
                    spriteBatch.Draw(SplashScreen.GetTexture(), new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                    spriteBatch.End();

                    if (PlayerInput.KeyHit(Keys.Space) || PlayerInput.KeyHit(Keys.Enter) || PlayerInput.LeftButtonDown())
                    {
                        SplashScreen.Dispose(); SplashScreen = null;
                    }
                }
                else
                {
                    SplashScreen.Dispose(); SplashScreen = null;
                }
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
