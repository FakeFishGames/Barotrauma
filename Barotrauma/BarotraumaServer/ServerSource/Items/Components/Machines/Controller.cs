using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Controller : ItemComponent, IServerSerializable
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(State);
            msg.Write(user == null ? (ushort)0 : user.ID);
        }
    }
}
