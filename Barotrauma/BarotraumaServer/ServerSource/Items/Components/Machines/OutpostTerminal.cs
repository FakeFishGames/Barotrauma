using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class OutpostTerminal : ItemComponent, IClientSerializable, IServerSerializable    
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {

        }
    }
}
