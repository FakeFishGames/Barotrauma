using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class MemoryComponent : ItemComponent
    {
        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            Value = msg.ReadString();
        }
    }
}
