using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(Snapped);
        }
    }
}
