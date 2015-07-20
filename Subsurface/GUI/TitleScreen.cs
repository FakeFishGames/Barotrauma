using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class TitleScreen
    {
        private Texture2D backgroundTexture,monsterTexture,titleTexture;

        readonly RenderTarget2D renderTarget;

        float state;

        public Vector2 Position;

        public TitleScreen(GraphicsDevice graphics)
        {
            backgroundTexture = Game1.TextureLoader.FromFile("Content/UI/titleBackground.png");
            monsterTexture = Game1.TextureLoader.FromFile("Content/UI/titleMonster.png");
            titleTexture = Game1.TextureLoader.FromFile("Content/UI/titleText.png");

            renderTarget = new RenderTarget2D(graphics, Game1.GraphicsWidth, Game1.GraphicsHeight);

        }

        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphics, float loadState, float deltaTime)
        {
            //if (stopwatch == null)
            //{
            //    stopwatch = new Stopwatch();
            //    stopwatch.Start();
            //}

            graphics.SetRenderTarget(renderTarget);
            //Debug.WriteLine(stopwatch.Elapsed.TotalMilliseconds);

            float scale = Game1.GraphicsHeight/2048.0f;

            state += deltaTime;

            Vector2 center = new Vector2(Game1.GraphicsWidth*0.3f, Game1.GraphicsHeight/2.0f) + Position*scale;

            Vector2 titlePos = center + new Vector2(-0.0f + (float)Math.Sqrt(state) * 220.0f, 0.0f) * scale;
            titlePos.X = Math.Min(titlePos.X, (float)Game1.GraphicsWidth / 2.0f);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
            graphics.Clear(Color.Black);

            spriteBatch.Draw(backgroundTexture, center, null, Color.White * Math.Min(state / 5.0f, 1.0f), 0.0f,
                new Vector2(backgroundTexture.Width / 2.0f, backgroundTexture.Height / 2.0f),
                scale, SpriteEffects.None, 0.2f);

            spriteBatch.Draw(monsterTexture,
                center + new Vector2(state * 100.0f - 1200.0f, state * 30.0f - 100.0f) * scale, null,
                Color.White, 0.0f, Vector2.Zero, scale, SpriteEffects.None, 0.1f);

            spriteBatch.Draw(titleTexture,
                titlePos, null,
                Color.White * Math.Min((state - 1.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), scale, SpriteEffects.None, 0.0f);
            
            spriteBatch.End();

            graphics.SetRenderTarget(null);

            Matrix transform = Matrix.CreateTranslation(
                new Vector3(Game1.GraphicsWidth / 2.0f,
                    Game1.GraphicsHeight / 2.0f, 0));     

            Hull.renderer.RenderBack(graphics, renderTarget, transform);


            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
            
            spriteBatch.Draw(titleTexture,
                titlePos, null,
                Color.White * Math.Min((state - 3.0f) / 5.0f, 1.0f), 0.0f, new Vector2(titleTexture.Width / 2.0f, titleTexture.Height / 2.0f), scale, SpriteEffects.None, 0.0f);

            string loadText = (loadState<100.0f) ? "Loading... "+(int)loadState+" %" : "Press any key to continue";
            spriteBatch.DrawString(GUI.Font, loadText, new Vector2(Game1.GraphicsWidth/2.0f - 50.0f, Game1.GraphicsHeight*0.8f), Color.White);
            
            spriteBatch.End();

        }
    }
}
