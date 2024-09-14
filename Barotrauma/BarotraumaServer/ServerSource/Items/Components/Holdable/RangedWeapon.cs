using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class RangedWeapon : ItemComponent, IServerSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteInt32(BurstIndex);
        }
    }
}
