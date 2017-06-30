using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics;
using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            Vector2 drawPos = new Vector2(item.Rect.X, item.Rect.Y);
            if (item.Submarine != null) drawPos += item.Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            if (barrelSprite != null)
            {
                barrelSprite.Draw(spriteBatch,
                     drawPos + barrelPos, Color.White,
                    rotation + MathHelper.PiOver2, 1.0f,
                    SpriteEffects.None, item.Sprite.Depth + 0.01f);
            }

            if (!editing) return;

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation)) * 60.0f,
                Color.Green);

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos(maxRotation), (float)Math.Sin(maxRotation)) * 60.0f,
                Color.Green);

            GUI.DrawLine(spriteBatch,
                drawPos + barrelPos,
                drawPos + barrelPos + new Vector2((float)Math.Cos((maxRotation + minRotation) / 2), (float)Math.Sin((maxRotation + minRotation) / 2)) * 60.0f,
                Color.LightGreen);

        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            UInt16 projectileID = msg.ReadUInt16();
            Item projectile = Entity.FindEntityByID(projectileID) as Item;

            if (projectile == null)
            {
                DebugConsole.ThrowError("Failed to launch a projectile - item with the ID \"" + projectileID + " not found");
                return;
            }

            Launch(projectile);
            PlaySound(ActionType.OnUse, item.WorldPosition);
        }
    }
}
