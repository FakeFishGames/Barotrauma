using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedInteger(Channel, MinChannel, MaxChannel);
        }
    }
}
