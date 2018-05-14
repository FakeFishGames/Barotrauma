using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class StructurePrefab : MapEntityPrefab
    {
        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam, Rectangle? placeRect = null)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);
            Rectangle newRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);

            if (placeRect.HasValue)
            {
                newRect = placeRect.Value;
            }
            else if (placePosition == Vector2.Zero)
            {
                if (PlayerInput.LeftButtonHeld())
                    placePosition = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                newRect.X = (int)position.X;
                newRect.Y = (int)position.Y;
            }
            else
            {
                Vector2 placeSize = size;
                if (ResizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (ResizeVertical) placeSize.Y = placePosition.Y - position.Y;

                newRect = Submarine.AbsRect(placePosition, placeSize);
            }

            sprite.DrawTiled(spriteBatch, new Vector2(newRect.X, -newRect.Y), new Vector2(newRect.Width, newRect.Height));

            if (!placeRect.HasValue)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X - GameMain.GraphicsWidth, -newRect.Y, newRect.Width + GameMain.GraphicsWidth * 2, newRect.Height), Color.White);
                GUI.DrawRectangle(spriteBatch, new Rectangle(newRect.X, -newRect.Y - GameMain.GraphicsHeight, newRect.Width, newRect.Height + GameMain.GraphicsHeight * 2), Color.White);
            }
        }
    }
}
