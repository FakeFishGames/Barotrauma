using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
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

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (light.LightSprite != null && (item.body == null || item.body.Enabled) && lightBrightness > 0.0f && IsOn)
            {
                light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, 0.0f, item.Scale, SpriteEffects.None, item.SpriteDepth - 0.0001f);
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

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            IsOn = msg.ReadBoolean();
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            light.Remove();
        }
    }
}
