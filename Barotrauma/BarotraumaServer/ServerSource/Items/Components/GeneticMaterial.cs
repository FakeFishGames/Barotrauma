using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent
    {
        public override void OnLoadCharacterInventory(Character character)
        {
            item.CreateServerEvent(this);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(tainted);
            if (tainted)
            {
                msg.WriteUInt32(selectedTaintedEffect?.UintIdentifier ?? 0);
            }
            else
            {
                msg.WriteUInt32(selectedEffect?.UintIdentifier ?? 0);
            }
        }
    }
}
