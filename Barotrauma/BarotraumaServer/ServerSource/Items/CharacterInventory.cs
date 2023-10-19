using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterInventory : Inventory
    { 
        public void ServerEventWrite(IWriteMessage msg, Client c, Character.InventoryStateEventData inventoryData)
        {
            SharedWrite(msg, inventoryData.SlotRange);
        }
    }
}
