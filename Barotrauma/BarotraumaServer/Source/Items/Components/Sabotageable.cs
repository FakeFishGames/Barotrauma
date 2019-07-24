using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class Sabotageable : ItemComponent, IServerSerializable, IClientSerializable
    {
        void InitProjSpecific()
        {
            //let the clients know the initial deterioration delay
            item.CreateServerEvent(this);
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            if (c.Character == null) return;
            StartRepairing(c.Character);
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            // msg.Write(deteriorationTimer);
        }
    }
}