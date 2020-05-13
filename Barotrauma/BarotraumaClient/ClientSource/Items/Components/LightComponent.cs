using Barotrauma.Extensions;
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

        public Vector2 DrawSize
        {
            get { return new Vector2(light.Range * 2, light.Range * 2); }
        }

        private LightSource light;
        public LightSource Light
        {
            get { return light; }
        }

        public override void OnScaleChanged()
        {
            light.SpriteScale = Vector2.One * item.Scale;
            light.Position = ParentBody != null ? ParentBody.Position : item.Position;
        }

        partial void SetLightSourceState(bool enabled, float brightness)
        {
            if (light == null) { return; }
            light.Enabled = enabled;
            light.Color = LightColor.Multiply(brightness);
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            SetLightSourceState(IsActive, lightBrightness);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (light.LightSprite != null && (item.body == null || item.body.Enabled) && lightBrightness > 0.0f && IsOn)
            {
                Vector2 origin = light.LightSprite.Origin;
                if (light.LightSpriteEffect == SpriteEffects.FlipHorizontally) { origin.X = light.LightSprite.SourceRect.Width - origin.X; }
                if (light.LightSpriteEffect == SpriteEffects.FlipVertically) { origin.Y = light.LightSprite.SourceRect.Height - origin.Y; }
                light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, origin, -light.Rotation, item.Scale, light.LightSpriteEffect, item.SpriteDepth - 0.0001f);
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            if (light?.LightSprite != null && item.Prefab.CanSpriteFlipX && item.body == null)
            {
                light.LightSpriteEffect = light.LightSpriteEffect == SpriteEffects.None ?
                    SpriteEffects.FlipHorizontally : SpriteEffects.None;                
            }
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
        private IEnumerable<object> ResetPredictionAfterDelay()
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

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            IsActive = msg.ReadBoolean();
            lastReceivedState = IsActive;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            light.Remove();
        }
    }
}
