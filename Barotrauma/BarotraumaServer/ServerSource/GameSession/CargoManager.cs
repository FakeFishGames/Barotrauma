using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CargoManager
    {
        public void SellBackPurchasedItems(List<PurchasedItem> itemsToSell, Client client = null)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> buyValues = GetBuyValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));
            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = item.Quantity * buyValues[item.ItemPrefab];
                Location.StoreCurrentBalance -= itemValue;
                campaign.GetWallet(client).Give(itemValue);
                PurchasedItems.Remove(item);
            }
        }

        public void BuyBackSoldItems(List<SoldItem> itemsToBuy, Client client)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            var sellValues = GetSellValuesAtCurrentLocation(itemsToBuy.Select(i => i.ItemPrefab));
            foreach (var item in itemsToBuy)
            {
                int itemValue = sellValues[item.ItemPrefab];
                if (Location.StoreCurrentBalance < itemValue || item.Removed) { continue; }
                Location.StoreCurrentBalance += itemValue;
                campaign.Bank.TryDeduct(itemValue);
                SoldItems.Remove(item);
            }
        }

        public void SellItems(List<SoldItem> itemsToSell, Client client)
        {
            bool canAddToRemoveQueue = (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer) && Entity.Spawner != null;
            IEnumerable<Item> sellableItemsInSub = Enumerable.Empty<Item>();
            if (canAddToRemoveQueue && itemsToSell.Any(i => i.Origin == SoldItem.SellOrigin.Submarine && i.ID == Entity.NullEntityID && !i.Removed))
            {
                sellableItemsInSub = GetSellableItemsFromSub();
            }
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            var sellValues = GetSellValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));
            foreach (var item in itemsToSell)
            {
                int itemValue = sellValues[item.ItemPrefab];
                // check if the store can afford the item and if the item hasn't been removed already
                if (Location.StoreCurrentBalance < itemValue || item.Removed) { continue; }
                // Server determines the items that are sold from the sub in multiplayer
                if (item.Origin == SoldItem.SellOrigin.Submarine && item.ID == Entity.NullEntityID && !item.Removed)
                {
                    var matchingItem = sellableItemsInSub.FirstOrDefault(i => !i.Removed && i.Prefab == item.ItemPrefab &&
                        itemsToSell.None(itemToSell => itemToSell.ItemPrefab == i.Prefab && itemToSell.ID == i.ID));
                    // This is a failsafe for scenarios where a client is trying to sell more items than there's available on the sub
                    if (matchingItem == null) { continue; }
                    item.SetItemId(matchingItem.ID);
                }
                if (!item.Removed && canAddToRemoveQueue && Entity.FindEntityByID(item.ID) is Item entity)
                {
                    item.Removed = true;
                    Entity.Spawner.AddItemToRemoveQueue(entity);
                }
                SoldItems.Add(item);
                Location.StoreCurrentBalance -= itemValue;
                campaign.Bank.Give(itemValue);
                GameAnalyticsManager.AddMoneyGainedEvent(itemValue, GameAnalyticsManager.MoneySource.Store, item.ItemPrefab.Identifier.Value);
            }
            OnSoldItemsChanged?.Invoke();
        }

        public void ClearSoldItemsProjSpecific()
        {
            SoldItems.Clear();
        }
    }
}
