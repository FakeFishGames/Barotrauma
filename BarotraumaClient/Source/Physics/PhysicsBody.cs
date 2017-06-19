using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null, float scale = 1.0f)
        {
            if (!Enabled) return;

            UpdateDrawPosition();

            if (sprite == null) return;

            SpriteEffects spriteEffect = (dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            if (GameMain.DebugDraw)
            {
                if (!body.Awake) color = Color.Blue;

                if (targetPosition != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)targetPosition);
                    if (Submarine != null) pos += Submarine.DrawPosition;

                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(pos.X - 5, -(pos.Y + 5)),
                        Vector2.One * 10.0f, Color.Red, false, 0, 3);
                }

                if (offsetFromTargetPos != Vector2.Zero)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(body.Position);
                    if (Submarine != null) pos += Submarine.DrawPosition;

                    GUI.DrawLine(spriteBatch,
                        new Vector2(pos.X, -pos.Y),
                        new Vector2(DrawPosition.X, -DrawPosition.Y),
                        Color.Cyan, 0, 5);
                }
            }

            sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, -drawRotation, scale, spriteEffect, depth);
        }

        public void DebugDraw(SpriteBatch spriteBatch, Color color)
        {
            if (bodyShapeTexture == null)
            {
                switch (BodyShape)
                {
                    case PhysicsBody.Shape.Rectangle:
                        bodyShapeTexture = GUI.CreateRectangle(
                            (int)ConvertUnits.ToDisplayUnits(width),
                            (int)ConvertUnits.ToDisplayUnits(height));
                        break;

                    case PhysicsBody.Shape.Capsule:
                        bodyShapeTexture = GUI.CreateCapsule(
                            (int)ConvertUnits.ToDisplayUnits(radius),
                            (int)ConvertUnits.ToDisplayUnits(Math.Max(height, width)));
                        break;
                    case PhysicsBody.Shape.Circle:
                        bodyShapeTexture = GUI.CreateCircle((int)ConvertUnits.ToDisplayUnits(radius));
                        break;
                }
            }

            float rot = -DrawRotation;
            if (bodyShape == PhysicsBody.Shape.Capsule && width > height)
            {
                rot -= MathHelper.PiOver2;
            }

            spriteBatch.Draw(
                bodyShapeTexture,
                new Vector2(DrawPosition.X, -DrawPosition.Y),
                null,
                color,
                rot,
                new Vector2(bodyShapeTexture.Width / 2, bodyShapeTexture.Height / 2),
                1.0f, SpriteEffects.None, 0.0f);
        }
    }
}
