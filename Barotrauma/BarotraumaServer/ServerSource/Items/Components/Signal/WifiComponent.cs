using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            SharedEventWrite(msg);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            SharedEventRead(msg);
            
            // Create an event to notify other clients about the changes
            item.CreateServerEvent(this);
        }
    }
}
