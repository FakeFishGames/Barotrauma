using Barotrauma.Networking;
using Microsoft.Xna.Framework;

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
