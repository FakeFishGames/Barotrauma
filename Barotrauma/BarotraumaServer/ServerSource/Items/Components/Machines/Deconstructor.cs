using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            bool active = msg.ReadBoolean();

            item.CreateServerEvent(this);

            if (item.CanClientAccess(c))
            {
                SetActive(active, c.Character);
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(user?.ID ?? 0);
            msg.Write(IsActive);
            msg.Write(progressTimer);
        }
    }
}
