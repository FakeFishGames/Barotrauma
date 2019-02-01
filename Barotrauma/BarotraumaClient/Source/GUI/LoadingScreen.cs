using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class LoadingScreen
    {
        private Texture2D backgroundTexture, monsterTexture, titleTexture;

        private RenderTarget2D renderTarget;

        private float state;
        
        private string selectedTip;

        public Vector2 CenterPosition;

        public Vector2 TitlePosition;

        private object loadMutex = new object();
        private float? loadState;
#if !(LINUX || OSX)
        Video splashScreenVideo;
        VideoPlayer videoPlayer;
#endif
        public Vector2 TitleSize
        {
            get { return new Vector2(titleTexture.Width, titleTexture.Height); }
        }

        public float Scale
        {
            get;
            private set;
        }

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
#if !(LINUX || OSX)

            if (GameMain.Config.EnableSplashScreen)
            {
                try
                {
                    splashScreenVideo = GameMain.Instance.Content.Load<Video>("utg_4");
                } 

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to load splashscreen", e);
                    GameMain.Config.EnableSplashScreen = false;
                }
            }
#endif


            backgroundTexture = TextureLoader.FromFile("Content/UI/titleBackground.png");
            monsterTexture = TextureLoader.FromFile("Content/UI/titleMonster.png");
            titleTexture = TextureLoader.FromFile("Content/UI/titleText.png");

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
#if !(LINUX || OSX)
            if (GameMain.Config.EnableSplashScreen && splashScreenVideo != null)
            {
                try
                {
                    DrawSplashScreen(spriteBatch);
                    if (videoPlayer != null && videoPlayer.State == MediaState.Playing)
                        return;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Playing splash screen video failed", e);
                    GameMain.Config.EnableSplashScreen = false;
                }
            }
#endif
            
            drawn = true;

            graphics.SetRenderTarget(renderTarget);

            Scale = GameMain.GraphicsHeight / 1500.0f;

            state += deltaTime;

            if (DrawLoadingText)
            {
                CenterPosition = new Vector2(GameMain.GraphicsWidth * 0.3f, GameMain.GraphicsHeight / 2.0f);
                TitlePosition = CenterPosition + new Vector2(-0.0f + (float)Math.Sqrt(state) * 220.0f, 0.0f) * Scale;
                TitlePosition.X = Math.Min(TitlePosition.X, (float)GameMain.GraphicsWidth / 2.0f);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            graphics.Clear(Color.Black);

            spriteBatch.Draw(backgroundTexture, CenterPosition, null, Color.White * Math.Min(state / 5.0f, 1.0f), 0.0f,
                new Vector2(backgroundTexture.Width / 2.0f, backgroundTexture.Height / 2.0f),
                Scale * 1.5f, SpriteEffects.None, 0.2f);

            spriteBatch.Draw(monsterTexture,
                CenterPosition + new Vector2((state % 40) * 100.0f - 1800.0f, (state % 40) * 30.0f - 200.0f) * Scale, null,
                Color.White, 0.0f, Vector2.Zero, Scale, SpriteEffects.None, 0.1f);

            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);

            spriteBatch.End();

            graphics.SetRenderTarget(null);

            if (WaterRenderer.Instance != null)
            {
                WaterRenderer.Instance.ScrollWater(Vector2.One * 10.0f, deltaTime);
                WaterRenderer.Instance.RenderWater(spriteBatch, renderTarget, null);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 3.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);

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
                    GUI.LargeFont.DrawString(spriteBatch, loadText,
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

#if !(LINUX || OSX)
        private void DrawSplashScreen(SpriteBatch spriteBatch)
        {
            if (videoPlayer == null)
            {
                videoPlayer = new VideoPlayer();
                videoPlayer.Play(splashScreenVideo);
                videoPlayer.Volume = GameMain.Config.SoundVolume;
            }
            else
            {
                Texture2D videoTexture = null;

                if (videoPlayer.State == MediaState.Stopped)
                {
                    videoPlayer.Dispose();
                    videoPlayer = null;

                    splashScreenVideo.Dispose();
                    splashScreenVideo = null;
                }
                else
                {
                    videoTexture = videoPlayer.GetTexture();

                    spriteBatch.Begin();
                    spriteBatch.Draw(videoTexture, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
                    spriteBatch.End();

                    if (PlayerInput.KeyHit(Keys.Space) || PlayerInput.KeyHit(Keys.Enter) || PlayerInput.LeftButtonDown())
                    {
                        videoPlayer.Stop();
                    }
                }

            }
        }
#endif
 
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
