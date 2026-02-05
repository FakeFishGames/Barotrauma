using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class ConnectionSelectorComponent : ItemComponent
    {
        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            SelectedConnection = msg.ReadRangedInteger(0, 255);
        }
    }
}
