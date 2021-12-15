using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Scanner : ItemComponent, IServerSerializable
    {
        private float LastSentScanTimer { get; set; }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(scanTimer);
        }
    }
}
