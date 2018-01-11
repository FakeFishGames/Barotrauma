using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class Level
    {
        private LevelRenderer renderer;

        private BackgroundCreatureManager backgroundCreatureManager;
        
        public void DrawFront(SpriteBatch spriteBatch)
        {
            if (renderer == null) return;
            renderer.Draw(spriteBatch);

            if (GameMain.DebugDraw)
            {
                foreach (InterestingPosition pos in positionsOfInterest)
                {
                    Color color = Color.Yellow;
                    if (pos.PositionType == PositionType.Cave)
                    {
                        color = Color.DarkOrange;
                    }
                    else if (pos.PositionType == PositionType.Ruin)
                    {
                        color = Color.LightGray;
                    }


                    GUI.DrawRectangle(spriteBatch, new Vector2(pos.Position.X - 15.0f, -pos.Position.Y - 15.0f), new Vector2(30.0f, 30.0f), color, true);
                }
            }
        }

        public void DrawBack(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, BackgroundCreatureManager backgroundCreatureManager = null)
        {
            float brightness = MathHelper.Clamp(1.1f + (cam.Position.Y - Size.Y) / 100000.0f, 0.1f, 1.0f);            
            GameMain.LightManager.AmbientLight = new Color(backgroundColor * brightness, 1.0f);

            graphics.Clear(backgroundColor);

            if (renderer == null) return;
            renderer.DrawBackground(spriteBatch, cam, backgroundSpriteManager, backgroundCreatureManager);
        }
    }
}
