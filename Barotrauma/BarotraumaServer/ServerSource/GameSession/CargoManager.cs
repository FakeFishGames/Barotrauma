using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using System.Text;

namespace Barotrauma
{
    partial class CargoManager
    {
        public void BuyBackSoldItems(Identifier storeIdentifier, List<SoldItem> itemsToBuy, Client client)
        {
            var store = Location.GetStore(storeIdentifier);
            if (store == null) { return; }
            var storeSpecificItems = SoldItems.GetValueOrDefault(storeIdentifier);
            // Check all the prices before starting the transaction to make sure the modifiers stay the same for the whole transaction
            var sellValues = GetSellValuesAtCurrentLocation(storeIdentifier, itemsToBuy.Select(i => i.ItemPrefab));
            foreach (var item in itemsToBuy)
            {
                int itemValue = sellValues[item.ItemPrefab];
                if (store.Balance < itemValue || item.Removed) { continue; }
                store.Balance += itemValue;
                campaign.TryPurchase(client, itemValue);
                storeSpecificItems.Remove(item);
            }
        }

        public void SellItems(Identifier storeIdentifier, List<SoldItem> itemsToSell, Client client)
        {
            var store = Location.GetStore(storeIdentifier);
            if (store == null) { return; }
            bool canAddToRemoveQueue = (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer) && Entity.Spawner != null;
            IEnumerable<Item> sellableItemsInSub = Enumerable.Empty<Item>();
            if (canAddToRemoveQueue && itemsToSell.Any(i => i.Origin == SoldItem.SellOrigin.Submarine && i.ID == Entity.NullEntityID && !i.Removed))
            {
                sellableItemsInSub = GetSellableItemsFromSub();
            }
            var itemsSoldAtStore = SoldItems.GetValueOrDefault(storeIdentifier);
            // Check all the prices before starting the transaction to make sure the modifiers stay the same for the whole transaction
            var sellValues = GetSellValuesAtCurrentLocation(storeIdentifier, itemsToSell.Select(i => i.ItemPrefab));
            foreach (var item in itemsToSell)
            {
                int itemValue = sellValues[item.ItemPrefab];
                // check if the store can afford the item and if the item hasn't been removed already
                if (store.Balance < itemValue || item.Removed) { continue; }
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
                itemsSoldAtStore?.Add(item);
                store.Balance -= itemValue;
                campaign.GetWallet(client).Give(itemValue);
                GameAnalyticsManager.AddMoneyGainedEvent(itemValue, GameAnalyticsManager.MoneySource.Store, item.ItemPrefab.Identifier.Value);
            }
            OnSoldItemsChanged?.Invoke(this);
        }

        public void LogNewItemPurchases(Identifier storeIdentifier, List<PurchasedItem> newItems, Client client)
        {
            StringBuilder sb = new StringBuilder();
            int price = 0;
            Dictionary<ItemPrefab, int> buyValues = GetBuyValuesAtCurrentLocation(storeIdentifier, newItems.Select(i => i.ItemPrefab));
            foreach (PurchasedItem item in newItems)
            {
                int itemValue = item.Quantity * buyValues[item.ItemPrefab];
                GameAnalyticsManager.AddMoneySpentEvent(itemValue, GameAnalyticsManager.MoneySink.Store, item.ItemPrefab.Identifier.Value);
                sb.Append($"\n - {item.ItemPrefab.Name} x{item.Quantity}");
                price += itemValue;
            }
            GameServer.Log($"{NetworkMember.ClientLogName(client, client?.Name ?? "Unknown")} purchased {newItems.Count} item(s) for {TextManager.FormatCurrency(price)}{sb.ToString()}", ServerLog.MessageType.Money);
        }

        public void ClearSoldItemsProjSpecific()
        {
            SoldItems.Clear();
        }
    }
}
