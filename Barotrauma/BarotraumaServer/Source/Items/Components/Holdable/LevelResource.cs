using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class LevelResource : ItemComponent, IServerSerializable
    {
        private float lastSentDeattachTimer;

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(deattachTimer);
        }
    }
}
