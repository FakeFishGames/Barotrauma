using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedInteger(Channel, MinChannel, MaxChannel);
        }
    }
}
