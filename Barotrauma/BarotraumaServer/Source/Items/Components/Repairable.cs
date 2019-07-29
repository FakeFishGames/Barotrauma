using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            if (c.Character == null) return;
            StartRepairing(c.Character);
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(deteriorationTimer);
        }
    }
}
