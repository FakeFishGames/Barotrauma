using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    partial class MapEntityPrefab
    {
        public virtual void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 placeSize = Submarine.GridSize;

            if (placePosition == Vector2.Zero)
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                GUI.DrawLine(spriteBatch, new Vector2(position.X - GameMain.GraphicsWidth, -position.Y), new Vector2(position.X + GameMain.GraphicsWidth, -position.Y), Color.White, 0, (int)(2.0f / cam.Zoom));

                GUI.DrawLine(spriteBatch, new Vector2(position.X, -(position.Y - GameMain.GraphicsHeight)), new Vector2(position.X, -(position.Y + GameMain.GraphicsHeight)), Color.White, 0, (int)(2.0f / cam.Zoom));
            }
            else
            {
                Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

                if (resizeHorizontal) placeSize.X = position.X - placePosition.X;
                if (resizeVertical) placeSize.Y = placePosition.Y - position.Y;

                Rectangle newRect = Submarine.AbsRect(placePosition, placeSize);
                newRect.Width = (int)Math.Max(newRect.Width, Submarine.GridSize.X);
                newRect.Height = (int)Math.Max(newRect.Height, Submarine.GridSize.Y);

                if (Submarine.MainSub != null)
                {
                    newRect.Location -= Submarine.MainSub.Position.ToPoint();
                }

                newRect.Y = -newRect.Y;
                GUI.DrawRectangle(spriteBatch, newRect, Color.DarkBlue);
            }
        }

        public void DrawListLine(SpriteBatch spriteBatch, Vector2 pos, Color color)
        {
            GUI.Font.DrawString(spriteBatch, name, pos, color);
        }
    }
}
