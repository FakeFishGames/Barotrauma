using Barotrauma.Networking;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        private float bodyShapeTextureScale;
        
        private Texture2D bodyShapeTexture;
        public Texture2D BodyShapeTexture
        {
            get { return bodyShapeTexture; }
        }

        public void Draw(DeformableSprite deformSprite, Camera cam, Vector2 scale, Color color, bool invert = false)
        {
            if (!Enabled) { return; }
            UpdateDrawPosition();
            deformSprite?.Draw(cam, 
                new Vector3(DrawPosition, MathHelper.Clamp(deformSprite.Sprite.Depth, 0, 1)), 
                deformSprite.Origin, 
                -DrawRotation, 
                scale, color, Dir < 0, invert);
        }

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null, float scale = 1.0f, bool mirrorX = false, bool mirrorY = false)
        {
            if (!Enabled) { return; }
            UpdateDrawPosition();
            if (sprite == null) { return; }
            SpriteEffects spriteEffect = (Dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            if (mirrorX)
            {
                spriteEffect = spriteEffect == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            if (mirrorY)
            {
                spriteEffect |= SpriteEffects.FlipVertically;
            }
            sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, -drawRotation, scale, spriteEffect, depth);
        }

        public void DebugDraw(SpriteBatch spriteBatch, Color color, bool forceColor = false)
        {
            if (!forceColor)
            {
                if (!FarseerBody.Enabled)
                {
                    color = Color.Black;
                }
                else if (!FarseerBody.Awake)
                {
                    color = Color.Blue;
                }
            }

            if (targetPosition != null)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)targetPosition);
                if (Submarine != null) pos += Submarine.DrawPosition;

                GUI.DrawRectangle(spriteBatch,
                    new Vector2(pos.X - 5, -(pos.Y + 5)),
                    Vector2.One * 10.0f, GUIStyle.Red, false, 0, 3);
            }

            if (drawOffset != Vector2.Zero)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(FarseerBody.Position);
                if (Submarine != null) pos += Submarine.DrawPosition;

                GUI.DrawLine(spriteBatch,
                    new Vector2(pos.X, -pos.Y),
                    new Vector2(DrawPosition.X, -DrawPosition.Y),
                    Color.Cyan, 0, 5);
            }
            if (bodyShapeTexture == null && IsValidShape(Radius, Height, Width))
            {
                switch (BodyShape)
                {
                    case Shape.Rectangle:
                        {
                            float maxSize = Math.Max(ConvertUnits.ToDisplayUnits(Width), ConvertUnits.ToDisplayUnits(Height));
                            if (maxSize > 128.0f)
                            {
                                bodyShapeTextureScale = 128.0f / maxSize;
                            }
                            else
                            {
                                bodyShapeTextureScale = 1.0f;
                            }

                            bodyShapeTexture = GUI.CreateRectangle(
                                (int)ConvertUnits.ToDisplayUnits(Width * bodyShapeTextureScale),
                                (int)ConvertUnits.ToDisplayUnits(Height * bodyShapeTextureScale));
                            break;
                        }
                    case Shape.Capsule:
                    case Shape.HorizontalCapsule:
                        {
                            float maxSize = Math.Max(ConvertUnits.ToDisplayUnits(Radius), ConvertUnits.ToDisplayUnits(Math.Max(Height, Width)));
                            if (maxSize > 128.0f)
                            {
                                bodyShapeTextureScale = 128.0f / maxSize;
                            }
                            else
                            {
                                bodyShapeTextureScale = 1.0f;
                            }

                            bodyShapeTexture = GUI.CreateCapsule(
                                (int)ConvertUnits.ToDisplayUnits(Radius * bodyShapeTextureScale),
                                (int)ConvertUnits.ToDisplayUnits(Math.Max(Height, Width) * bodyShapeTextureScale));
                            break;
                        }
                    case Shape.Circle:
                        if (ConvertUnits.ToDisplayUnits(Radius) > 128.0f)
                        {
                            bodyShapeTextureScale = 128.0f / ConvertUnits.ToDisplayUnits(Radius);
                        }
                        else
                        {
                            bodyShapeTextureScale = 1.0f;
                        }
                        bodyShapeTexture = GUI.CreateCircle((int)ConvertUnits.ToDisplayUnits(Radius * bodyShapeTextureScale));
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            float rot = -DrawRotation;
            if (bodyShape == Shape.HorizontalCapsule)
            {
                rot -= MathHelper.PiOver2;
            }
            
            if (bodyShapeTexture != null)
            {
                spriteBatch.Draw(
                    bodyShapeTexture,
                    new Vector2(DrawPosition.X, -DrawPosition.Y),
                    null,
                    color,
                    rot,
                    new Vector2(bodyShapeTexture.Width / 2, bodyShapeTexture.Height / 2),
                    1.0f / bodyShapeTextureScale, SpriteEffects.None, 0.0f);
            }
        }

        public PosInfo ClientRead(IReadMessage msg, float sendingTime, string parentDebugName)
        {
            float MaxVel            = NetConfig.MaxPhysicsBodyVelocity;
            float MaxAngularVel     = NetConfig.MaxPhysicsBodyAngularVelocity;

            Vector2 newPosition         = SimPosition;
            float? newRotation          = null;
            bool awake                  = FarseerBody.Awake;
            Vector2 newVelocity         = LinearVelocity;
            float? newAngularVelocity   = null;

            newPosition = new Vector2(
                msg.ReadSingle(), 
                msg.ReadSingle());
            
            awake = msg.ReadBoolean();
            bool fixedRotation = msg.ReadBoolean();

            if (!fixedRotation)
            {
                newRotation = msg.ReadRangedSingle(0.0f, MathHelper.TwoPi, 8);
            }
            if (awake)
            {
                newVelocity = new Vector2(
                    msg.ReadRangedSingle(-MaxVel, MaxVel, 12),
                    msg.ReadRangedSingle(-MaxVel, MaxVel, 12));
                newVelocity = NetConfig.Quantize(newVelocity, -MaxVel, MaxVel, 12);

                if (!fixedRotation)
                {
                    newAngularVelocity = msg.ReadRangedSingle(-MaxAngularVel, MaxAngularVel, 8);
                    newAngularVelocity = NetConfig.Quantize(newAngularVelocity.Value, -MaxAngularVel, MaxAngularVel, 8);
                }
            }
            msg.ReadPadBits();

            if (!MathUtils.IsValid(newPosition) || 
                !MathUtils.IsValid(newVelocity) ||
                (newRotation.HasValue && !MathUtils.IsValid(newRotation.Value)) ||
                (newAngularVelocity.HasValue && !MathUtils.IsValid(newAngularVelocity.Value)))
            {
                string errorMsg = "Received invalid position data for \"" + parentDebugName
                    + "\" (position: " + newPosition + ", rotation: " + (newRotation ?? 0) + ", velocity: " + newVelocity + ", angular velocity: " + (newAngularVelocity ?? 0) + ")";
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                GameAnalyticsManager.AddErrorEventOnce("PhysicsBody.ClientRead:InvalidData" + parentDebugName,
                    GameAnalyticsManager.ErrorSeverity.Error,
                    errorMsg);
                return null;
            }

            return lastProcessedNetworkState > sendingTime ? 
                null : 
                new PosInfo(newPosition, newRotation, newVelocity, newAngularVelocity, sendingTime);            
        }

        partial void DisposeProjSpecific()
        {
            if (bodyShapeTexture != null)
            {
                bodyShapeTexture.Dispose();
                bodyShapeTexture = null;
            }
        }
    }
}
