using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HomeAndHarbor
{
    public partial class ItemCatalog : ItemComponent
    {
        #region Networking
        public void ServerEventRead(IReadMessage msg, Client client)
        {
            CatalogItem catalogueItem = catalogItems[msg.ReadRangedInteger(0, catalogItems.Count - 1)];
            if (item.CanClientAccess(client) && client.Character != null)
            {
                catalogueItem.Purchase(client.Character, client);
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
        {
            CatalogEventDataServer data = ExtractEventData<CatalogEventDataServer>(extraData);

            msg.WriteByte((byte)data.EventType);

            switch (data.EventType)
            {
                case EventType.Purchase:
                    if (data.CatalogItem == null) { throw new ArgumentNullException(nameof(data.CatalogItem)); }
                    if (data.InstantiatedItem == null) { throw new ArgumentNullException(nameof(data.InstantiatedItem)); }

                    msg.WriteRangedInteger(catalogItems.IndexOf(data.CatalogItem), 0, catalogItems.Count - 1);
                    msg.WriteUInt16(data.InstantiatedItem.ID);
                    break;
                case EventType.StartReclaim:
                    if (reclaimingTarget != null && reclaimingUser != null)
                    {
                        msg.WriteUInt16(reclaimingTarget.ID);
                        msg.WriteUInt16(reclaimingUser.ID);
                    }
                    else
                    {
                        msg.WriteUInt16(0);
                        msg.WriteUInt16(0);
                    }
                    break;
            }
        }

        #endregion
    }
}
