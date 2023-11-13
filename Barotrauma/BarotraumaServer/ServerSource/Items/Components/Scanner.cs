using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Scanner : ItemComponent, IServerSerializable
    {
        private float LastSentScanTimer { get; set; }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteSingle(scanTimer);
        }
    }
}
