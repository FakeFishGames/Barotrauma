using Barotrauma.Networking;

namespace Barotrauma
{
    public partial class ItemInventory : Inventory
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, Item.InventoryStateEventData inventoryData)
        {
            SharedWrite(msg, inventoryData.SlotRange);
        }
    }
}
