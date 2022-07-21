using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Controller : ItemComponent, IServerSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.Write(State);
            msg.Write(user == null ? (ushort)0 : user.ID);
        }
    }
}
