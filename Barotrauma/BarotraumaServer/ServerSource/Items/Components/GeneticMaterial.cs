using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(tainted);
            if (tainted)
            {
                msg.Write(selectedTaintedEffect?.UintIdentifier ?? 0);
            }
            else
            {
                msg.Write(selectedEffect?.UintIdentifier ?? 0);
            }
        }
    }
}
