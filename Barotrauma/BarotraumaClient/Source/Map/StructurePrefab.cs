using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)ScaledSize.X, (int)ScaledSize.Y);

            if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.PrimaryMouseButtonHeld())
                    placePosition = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;
            }
            else
            {
                Vector2 placeSize = ScaledSize;
                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                newRect = Submarine.AbsRect(placePosition, placeSize);
            }

            sprite.DrawTiled(spriteBatch, new Vector2(newRect.X, -newRect.Y), new Vector2(newRect.Width, newRect.Height), textureScale: TextureScale * Scale);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X - GameMain.GraphicsWidth, -newRect.Y, newRect.Width + GameMain.GraphicsWidth * 2, newRect.Height), Color.White);
            GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X, -newRect.Y - GameMain.GraphicsHeight, newRect.Width, newRect.Height + GameMain.GraphicsHeight * 2), Color.White);
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Rectangle placeRect, float scale = 1.0f, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            SpriteEffects oldEffects = sprite.effects;
            sprite.effects ^= spriteEffects;

            sprite.DrawTiled(
                spriteBatch, 
                new Vector2(placeRect.X, -placeRect.Y), 
                new Vector2(placeRect.Width, placeRect.Height), 
                color: Color.White * 0.8f, 
                textureScale: TextureScale * scale);

            sprite.effects = oldEffects;
        }
    }
}
