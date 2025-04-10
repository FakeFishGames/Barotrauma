﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        /// <summary>
        /// Last known state the server has told us about.
        /// </summary>
        public PosInfo LastServerState;

        /// <summary>
        /// An offset used to corrections to positional errors look smoother. When a large positional correction needs to be done in multiplayer,
        /// the body is immediately moved to the correct position, but the draw position is interpolated to make the correction visually smoother.
        /// This value means the offset from the "actual" corrected position of the body to the "fake", interpolated draw position.
        /// </summary>
        public Vector2 NetworkPositionErrorOffset => drawOffset;

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

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null, float scale = 1.0f, bool mirrorX = false, bool mirrorY = false, Vector2? origin =  null)
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
            sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, origin ?? sprite.Origin, - drawRotation, scale, spriteEffect, depth);
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
                if (Submarine != null) { pos += Submarine.DrawPosition; }

                GUI.DrawLine(spriteBatch,
                    new Vector2(pos.X, -pos.Y),
                    new Vector2(DrawPosition.X, -DrawPosition.Y),
                    Color.Purple * 0.5f, 0, 5);
            }
            if (IsValidShape(Radius, Height, Width))
            {
                DrawShape(drawPosition, DrawRotation, color);
            }

            if (LastServerState != null)
            {
                Vector2 drawPos = ConvertUnits.ToDisplayUnits(LastServerState.Position);
                if (Submarine != null)
                {
                    drawPos += Submarine.DrawPosition;
                }
                float rotation = LastServerState.Rotation ?? 0.0f;

                DrawShape(drawPos, rotation, Color.Purple * 0.75f);
            }

            void DrawShape(Vector2 position, float rotation, Color color)
            {
                float radius = ConvertUnits.ToDisplayUnits(Radius);
                float height = ConvertUnits.ToDisplayUnits(Height);
                float width = ConvertUnits.ToDisplayUnits(Width);

                switch (BodyShape)
                {
                    case Shape.Rectangle:
                        GUI.DrawRectangle(spriteBatch, position.FlipY(), new Vector2(width, height), new Vector2(width, height) / 2, -rotation, color);
                        break;
                    case Shape.Capsule:
                        GUI.DrawCapsule(spriteBatch, position.FlipY(), height, radius, -rotation - MathHelper.PiOver2, color);
                        break;
                    case Shape.HorizontalCapsule:
                        GUI.DrawCapsule(spriteBatch, position.FlipY(), width, radius, -rotation, color);
                        break;
                    case Shape.Circle:
                        GUI.DrawDonutSection(spriteBatch, position.FlipY(), new Range<float>(radius - 0.5f, radius + 0.5f), MathHelper.TwoPi, color, 0, -rotation);
                        break;
                    default:
                        throw new NotImplementedException();
                }
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

            if (lastProcessedNetworkState > sendingTime) { return null; }

            LastServerState = new PosInfo(newPosition, newRotation, newVelocity, newAngularVelocity, sendingTime);
            return LastServerState;            
        }
    }
}
