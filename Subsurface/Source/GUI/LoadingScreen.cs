using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class LoadingScreen
    {
        private Texture2D backgroundTexture,monsterTexture,titleTexture;

        readonly RenderTarget2D renderTarget;

        float state;

        public Vector2 CenterPosition;

        public Vector2 TitlePosition;
        
        private float? loadState;

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
            get { return loadState; }        
            set 
            {
                loadState = value;
                DrawLoadingText = true;
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
            monsterTexture = TextureLoader.FromFile("Content/UI/titleMonster.png");
            titleTexture = TextureLoader.FromFile("Content/UI/titleText.png");

            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            DrawLoadingText = true;
        }

        
        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphics, float deltaTime)
        {
            drawn = true;

            graphics.SetRenderTarget(renderTarget);

            Scale = GameMain.GraphicsHeight/1500.0f;

            state += deltaTime;

            if (DrawLoadingText)
            {
                CenterPosition = new Vector2(GameMain.GraphicsWidth*0.3f, GameMain.GraphicsHeight/2.0f); 
                TitlePosition = CenterPosition + new Vector2(-0.0f + (float)Math.Sqrt(state) * 220.0f, 0.0f) * Scale;
                TitlePosition.X = Math.Min(TitlePosition.X, (float)GameMain.GraphicsWidth / 2.0f);
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            graphics.Clear(Color.Black);

            spriteBatch.Draw(backgroundTexture, CenterPosition, null, Color.White * Math.Min(state / 5.0f, 1.0f), 0.0f,
                new Vector2(backgroundTexture.Width / 2.0f, backgroundTexture.Height / 2.0f),
                Scale*1.5f, SpriteEffects.None, 0.2f);

            spriteBatch.Draw(monsterTexture,
                CenterPosition + new Vector2((state % 40) * 100.0f - 1800.0f, (state % 40) * 30.0f - 200.0f) * Scale, null,
                Color.White, 0.0f, Vector2.Zero, Scale, SpriteEffects.None, 0.1f);

            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);
            
            spriteBatch.End();

            graphics.SetRenderTarget(null);

            if (Hull.renderer != null)
            {
                Hull.renderer.ScrollWater(deltaTime);
                Hull.renderer.RenderBack(spriteBatch, renderTarget, 0.0f);
            }
            
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
            
            spriteBatch.Draw(titleTexture,
                TitlePosition, null,
                Color.White * Math.Min((state - 3.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), Scale, SpriteEffects.None, 0.0f);

            if (DrawLoadingText)
            {
                string loadText = "";
                if (loadState == 100.0f)
                {
                    loadText = "Press any key to continue";
                }
                else
                {
                    loadText = "Loading... ";
                    if (loadState!=null)
                    {
                        loadText += (int)loadState + " %";
                    }
                }

                if (GUI.LargeFont!=null)
                {
                    spriteBatch.DrawString(GUI.LargeFont, loadText, 
                        new Vector2(GameMain.GraphicsWidth/2.0f - GUI.LargeFont.MeasureString(loadText).X/2.0f, GameMain.GraphicsHeight*0.8f), 
                        Color.White); 
                }
           
            }
            spriteBatch.End();

        }

        /*public void Update()
        {
            if (Hull.renderer != null)
            {
                Hull.renderer.ScrollWater();
            }
        }*/

        bool drawn;
        public IEnumerable<object> DoLoading(IEnumerable<object> loader)
        {
            drawn = false;
            LoadState = null;

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

            loadState = 100.0f;

            yield return CoroutineStatus.Success;
        }
    }
}
