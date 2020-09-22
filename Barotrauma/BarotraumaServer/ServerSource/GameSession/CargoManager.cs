using System.Collections.Generic;

namespace Barotrauma
{
    partial class CargoManager
    {
        public void SellBackPurchasedItems(List<PurchasedItem> itemsToSell)
        {
            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = GetBuyValueAtCurrentLocation(item);
                Location.StoreCurrentBalance -= itemValue;
                campaign.Money += itemValue;
                PurchasedItems.Remove(item);
            }
        }

        public void BuyBackSoldItems(List<SoldItem> itemsToBuy)
        {
            foreach (SoldItem item in itemsToBuy)
            {
                var itemValue = GetSellValueAtCurrentLocation(item.ItemPrefab);
                if (Location.StoreCurrentBalance < itemValue || item.Removed) { continue; }
                Location.StoreCurrentBalance += itemValue;
                campaign.Money -= itemValue;
                SoldItems.Remove(item);
            }
        }

        public void SellItems(List<SoldItem> itemsToSell)
        {
            var canAddToRemoveQueue = (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer) && Entity.Spawner != null;
            foreach (SoldItem item in itemsToSell)
            {
                var itemValue = GetSellValueAtCurrentLocation(item.ItemPrefab);

                // check if the store can afford the item and if the item hasn't been removed already
                if (Location.StoreCurrentBalance < itemValue || item.Removed) { continue; }

                if (!item.Removed && canAddToRemoveQueue && Entity.FindEntityByID(item.ID) is Item entity)
                {
                    item.Removed = true;
                    Entity.Spawner.AddToRemoveQueue(entity);
                }
                SoldItems.Add(item);
                Location.StoreCurrentBalance -= itemValue;
                campaign.Money += itemValue;
            }
            OnSoldItemsChanged?.Invoke();
        }

        public void ClearSoldItemsProjSpecific()
        {
            SoldItems.Clear();
        }
    }
}
