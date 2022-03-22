using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class TriggerComponent : ItemComponent, IServerSerializable
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedSingle(CurrentForceFluctuation, 0.0f, 1.0f, 8);
        }
    }
}
