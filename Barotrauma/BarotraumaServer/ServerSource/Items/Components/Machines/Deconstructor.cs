using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            bool active = msg.ReadBoolean();

            item.CreateServerEvent(this);

            if (item.CanClientAccess(c))
            {
                SetActive(active, c.Character);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteUInt16(user?.ID ?? 0);
            msg.WriteBoolean(IsActive);
            msg.WriteSingle(progressTimer);
        }
    }
}
