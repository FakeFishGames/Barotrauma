using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    public partial class Controller : ItemComponent, IServerSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(State);
            msg.WriteUInt16(User == null || User.Removed ? (ushort)0 : User.ID);
        }
    }
}
