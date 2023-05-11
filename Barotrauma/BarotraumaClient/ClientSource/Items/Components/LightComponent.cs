﻿using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private bool? lastReceivedState;

        private CoroutineHandle resetPredictionCoroutine;
        private float resetPredictionTimer;

        /// <summary>
        /// The current multiplier for the light color (usually equal to <see cref="lightBrightness"/>, but in the case of e.g. blinking lights the multiplier
        /// doesn't go to 0 when the light turns off, because otherwise it'd take a while for it turn back on based on the lightBrightness which is interpolated
        /// towards the current voltage).
        /// </summary>
        private float lightColorMultiplier;

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The scale of the light sprite.")]
        public float LightSpriteScale { get; set; }

        public Vector2 DrawSize
        {
            get { return new Vector2(Light.Range * 2, Light.Range * 2); }
        }

        public LightSource Light { get; }

        public override void OnScaleChanged()
        {
            Light.SpriteScale = Vector2.One * item.Scale;
            Light.Position = ParentBody != null ? ParentBody.Position : item.Position;
        }

        partial void SetLightSourceState(bool enabled, float brightness)
        {
            if (Light == null) { return; }
            Light.Enabled = enabled;
            lightColorMultiplier = brightness;
            if (enabled)
            {
                Light.Color = LightColor.Multiply(lightColorMultiplier);
            }
        }

        partial void SetLightSourceTransformProjSpecific()
        {
            if (ParentBody != null)
            {
                Light.ParentBody = ParentBody;
            }
            else if (turret != null)
            {
                Light.Position = new Vector2(item.Rect.X + turret.TransformedBarrelPos.X, item.Rect.Y - turret.TransformedBarrelPos.Y);
            }
            else if (item.body != null)
            {
                Light.ParentBody = item.body;
            }
            else
            {
                Light.Position = item.Position;
            }
            PhysicsBody body = Light.ParentBody;
            if (body != null)
            {
                Light.Rotation = body.Dir > 0.0f ? body.DrawRotation : body.DrawRotation - MathHelper.Pi;
                Light.LightSpriteEffect = (body.Dir > 0.0f) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }
            else
            {
                Light.Rotation = -Rotation - item.RotationRad;
                Light.LightSpriteEffect = item.SpriteEffects;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (Light?.LightSprite == null) { return; }
            if ((item.body == null || item.body.Enabled) && lightBrightness > 0.0f && IsOn && Light.Enabled)
            {
                Vector2 origin = Light.LightSprite.Origin;
                if ((Light.LightSpriteEffect & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally) { origin.X = Light.LightSprite.SourceRect.Width - origin.X; }
                if ((Light.LightSpriteEffect & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically) { origin.Y = Light.LightSprite.SourceRect.Height - origin.Y; }

                Vector2 drawPos = item.body?.DrawPosition ?? item.DrawPosition;

                Color color = lightColor;
                if (Light.OverrideLightSpriteAlpha.HasValue)
                {
                    color = new Color(lightColor, Light.OverrideLightSpriteAlpha.Value);
                }
                Light.LightSprite.Draw(spriteBatch, 
                    new Vector2(drawPos.X, -drawPos.Y), 
                    color * lightBrightness, 
                    origin, 
                    -Light.Rotation, 
                    item.Scale * LightSpriteScale, 
                    Light.LightSpriteEffect, itemDepth - 0.0001f);
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            if (Light?.LightSprite != null && item.Prefab.CanSpriteFlipX)
            {
                Light.LightSpriteEffect = Light.LightSpriteEffect == SpriteEffects.None ?
                    SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            SetLightSourceTransformProjSpecific();
        }

        partial void OnStateChanged()
        {
            if (GameMain.Client == null || !lastReceivedState.HasValue) { return; }
            //reset to last known server state after the state hasn't changed in 1.0 seconds client-side
            resetPredictionTimer = 1.0f;
            if (resetPredictionCoroutine == null || !CoroutineManager.IsCoroutineRunning(resetPredictionCoroutine))
            {
                resetPredictionCoroutine = CoroutineManager.StartCoroutine(ResetPredictionAfterDelay());
            }
        }

        /// <summary>
        /// Reset client-side prediction of the light's state to the last known state sent by the server after resetPredictionTimer runs out
        /// </summary>
        private IEnumerable<CoroutineStatus> ResetPredictionAfterDelay()
        {
            while (resetPredictionTimer > 0.0f)
            {
                resetPredictionTimer -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            if (lastReceivedState.HasValue) { IsActive = lastReceivedState.Value; }
            resetPredictionCoroutine = null;
            yield return CoroutineStatus.Success;
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            IsActive = msg.ReadBoolean();
            lastReceivedState = IsActive;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            Light.Remove();
        }
    }
}
