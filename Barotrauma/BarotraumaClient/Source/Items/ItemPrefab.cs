using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    partial class ItemPrefab : MapEntityPrefab
    {
        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            Vector2 position = Submarine.MouseToWorldGrid(cam, Submarine.MainSub);

            if (PlayerInput.RightButtonClicked())
            {
                selected = null;
                return;
            }

            if (!resizeHorizontal && !resizeVertical)
            {
                sprite.Draw(spriteBatch, new Vector2(position.X + sprite.size.X / 2.0f, -position.Y + sprite.size.Y / 2.0f), SpriteColor);
            }
            else
            {
                Vector2 placeSize = size;

                if (placePosition == Vector2.Zero)
                {
                    if (PlayerInput.LeftButtonHeld()) placePosition = position;
                }
                else
                {
                    if (resizeHorizontal)
                        placeSize.X = Math.Max(position.X - placePosition.X, size.X);
                    if (resizeVertical)
                        placeSize.Y = Math.Max(placePosition.Y - position.Y, size.Y);

                    position = placePosition;
                }

                if (sprite != null) sprite.DrawTiled(spriteBatch, new Vector2(position.X, -position.Y), placeSize, SpriteColor);
            }

            //if (PlayerInput.GetMouseState.RightButton == ButtonState.Pressed) selected = null;

        }
    }
}
