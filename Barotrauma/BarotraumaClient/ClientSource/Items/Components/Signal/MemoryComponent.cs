using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class MemoryComponent : ItemComponent
    {
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            Value = msg.ReadString();
        }
    }
}
