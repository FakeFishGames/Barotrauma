using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class TriggerComponent : ItemComponent, IServerSerializable, IDrawableComponent
    {
        public Vector2 DrawSize =>
            Vector2.One *
            (Radius > 0.0f ? Radius * 2 : Math.Max(Width, Height));

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (editing)
            {
                PhysicsBody.DebugDraw(spriteBatch, Color.LightGray * 0.7f);
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            CurrentForceFluctuation = msg.ReadRangedSingle(0.0f, 1.0f, 8);
        }

    }
}
