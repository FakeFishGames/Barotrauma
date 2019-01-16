using Barotrauma.Lights;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private LightSource light;
        public LightSource Light
        {
            get { return light; }
        }
        
        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            if (light.LightSprite != null && (item.body == null || item.body.Enabled) && lightBrightness > 0.0f)
            {
                light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, 0.0f, 1.0f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, item.SpriteDepth - 0.0001f);
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            if (light?.LightSprite != null)
            {
                light.LightSpriteEffect = light.LightSpriteEffect == SpriteEffects.None ?
                    SpriteEffects.FlipHorizontally : SpriteEffects.None;                
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            IsOn = msg.ReadBoolean();
        }
    }
}
