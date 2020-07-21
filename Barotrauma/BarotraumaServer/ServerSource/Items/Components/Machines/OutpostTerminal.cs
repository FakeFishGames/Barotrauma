using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class OutpostTerminal : ItemComponent, IClientSerializable, IServerSerializable    
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {

        }
    }
}
