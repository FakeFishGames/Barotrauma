using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        private float lastSentDeattachTimer;

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteSingle(deattachTimer);
        }
    }
}
