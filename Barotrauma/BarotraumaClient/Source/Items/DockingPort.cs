using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (dockingState == 0.0f) return;

            Vector2 drawPos = item.DrawPosition;
            drawPos.Y = -drawPos.Y;

            var rect = overlaySprite.SourceRect;

            if (IsHorizontal)
            {
                drawPos.Y -= rect.Height / 2;

                if (dockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos,
                        new Rectangle(
                            rect.Center.X + (int)(rect.Width / 2 * (1.0f - dockingState)), rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);

                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitX * (rect.Width / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);
                }
            }
            else
            {
                drawPos.X -= rect.Width / 2;

                if (dockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitY * (rect.Height / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos,
                        new Rectangle(
                            rect.X, rect.Y + rect.Height / 2 + (int)(rect.Height / 2 * (1.0f - dockingState)),
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);
                }
            }
        }

    }
}
