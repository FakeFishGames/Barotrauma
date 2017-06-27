using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Lidgren.Network;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Hull : MapEntity, IPropertyObject, IServerSerializable
    {
        public override bool IsMouseOn(Vector2 position)
        {
            return false;
        }
    }
}
