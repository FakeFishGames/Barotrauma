using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class TriggerComponent : ItemComponent, IServerSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteRangedSingle(CurrentForceFluctuation, 0.0f, 1.0f, 8);
        }
    }
}
