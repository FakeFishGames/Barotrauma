using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CargoManager
    {
        public void SellBackPurchasedItems(List<PurchasedItem> itemsToSell)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> buyValues = GetBuyValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));
            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = item.Quantity * buyValues[item.ItemPrefab];
                Location.StoreCurrentBalance -= itemValue;
                campaign.Money += itemValue;
                PurchasedItems.Remove(item);
            }
        }

        public void BuyBackSoldItems(List<SoldItem> itemsToBuy)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> sellValues = GetSellValuesAtCurrentLocation(itemsToBuy.Select(i => i.ItemPrefab));
            foreach (SoldItem item in itemsToBuy)
            {
                var itemValue = sellValues[item.ItemPrefab];
                if (Location.StoreCurrentBalance < itemValue || item.Removed) { continue; }
                Location.StoreCurrentBalance += itemValue;
                campaign.Money -= itemValue;
                SoldItems.Remove(item);
            }
        }

        public void SellItems(List<SoldItem> itemsToSell)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> sellValues = GetSellValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));
            var canAddToRemoveQueue = (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer) && Entity.Spawner != null;
            foreach (SoldItem item in itemsToSell)
            {
                var itemValue = sellValues[item.ItemPrefab];

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
