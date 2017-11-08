using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable
    {
        public override bool IsMouseOn(Vector2 position)
        {
            return false;
        }
    }
}
