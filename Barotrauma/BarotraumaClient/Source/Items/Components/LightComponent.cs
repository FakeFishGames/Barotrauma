using Microsoft.Xna.Framework;
using Barotrauma.Lights;
using System;
using System.Xml.Linq;
using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private LightSource light;

        public void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, bool editing = false)
        {
            if (light.LightSprite != null && (item.body == null || item.body.Enabled))
            {
                light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, 0.0f, 1.0f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, item.Sprite.Depth - 0.0001f);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            IsOn = msg.ReadBoolean();
        }
    }
}
