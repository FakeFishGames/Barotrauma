using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CargoManager
    {
        private List<SoldEntity> SoldEntities { get; } = new List<SoldEntity>();

        // The bag slot is intentionally left out since we want to be able to sell items from there
        private readonly List<InvSlotType> equipmentSlots = new List<InvSlotType>() { InvSlotType.Head, InvSlotType.InnerClothes, InvSlotType.OuterClothes, InvSlotType.Headset, InvSlotType.Card };

        public IEnumerable<Item> GetSellableItems(Character character)
        {
            if (character == null) { return new List<Item>(); }
            var confirmedSoldEntities = GetConfirmedSoldEntities();
            return character.Inventory.FindAllItems(item =>
            {
                if (!IsItemSellable(item, confirmedSoldEntities)) { return false; }
                // Item must be in a non-equipment slot if possible
                if (!item.AllowedSlots.All(s => equipmentSlots.Contains(s)) && IsInEquipmentSlot(item)) { return false; }
                // Item must not be contained inside an item in an equipment slot
                if (item.GetRootContainer() is Item rootContainer && IsInEquipmentSlot(rootContainer)) { return false; }
                return true;
            }, recursive: true).Distinct();

            bool IsInEquipmentSlot(Item item)
            {
                foreach (InvSlotType slot in equipmentSlots)
                {
                    if (character.Inventory.IsInLimbSlot(item, slot)) { return true; }
                }
                return false;
            }
        }

        private IEnumerable<SoldEntity> GetConfirmedSoldEntities()
        {
            // Only consider items which have been:
            // a) sold in singleplayer or confirmed by server (SellStatus.Confirmed); or
            // b) sold locally in multiplayer (SellStatus.Local), but the client has not received a campaing state update yet after selling them
            return SoldEntities.Where(se => se.Status != SoldEntity.SellStatus.Unconfirmed);
        }

        public void SetItemsInBuyCrate(List<PurchasedItem> items)
        {
            ItemsInBuyCrate.Clear();
            ItemsInBuyCrate.AddRange(items);
            OnItemsInBuyCrateChanged?.Invoke();
        }

        public void SetItemsInSubSellCrate(List<PurchasedItem> items)
        {
            ItemsInSellFromSubCrate.Clear();
            ItemsInSellFromSubCrate.AddRange(items);
            OnItemsInSellFromSubCrateChanged?.Invoke();
        }

        public void SetSoldItems(List<SoldItem> items)
        {
            SoldItems.Clear();
            SoldItems.AddRange(items);
            foreach (var se in SoldEntities)
            {
                if (se.Status == SoldEntity.SellStatus.Confirmed) { continue; }
                if (SoldItems.Any(si => Match(si, se, true)))
                {
                    se.Status = SoldEntity.SellStatus.Confirmed;
                }
                else
                {
                    se.Status = SoldEntity.SellStatus.Unconfirmed;
                }
            }
            foreach (var si in SoldItems)
            {
                if (si.Origin != SoldItem.SellOrigin.Submarine) { continue; }
                if (!(SoldEntities.FirstOrDefault(se => se.Item == null && Match(si, se, false)) is SoldEntity soldEntityMatch)) { continue; }
                if (!(Entity.FindEntityByID(si.ID) is Item item)) { continue; }
                soldEntityMatch.SetItem(item);
                soldEntityMatch.Status = SoldEntity.SellStatus.Confirmed;
            }
            OnSoldItemsChanged?.Invoke();

            static bool Match(SoldItem soldItem, SoldEntity soldEntity, bool matchId)
            {
                if (soldItem.ItemPrefab != soldEntity.ItemPrefab) { return false; }
                if (matchId && (soldEntity.Item == null || soldItem.ID != soldEntity.Item.ID)) { return false; }
                if (soldItem.Origin == SoldItem.SellOrigin.Character && GameMain.Client != null && soldItem.SellerID != GameMain.Client.ID) { return false; }
                return true;
            }
        }

        public void ModifyItemQuantityInSellCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            var itemToSell = ItemsInSellCrate.Find(i => i.ItemPrefab == itemPrefab);
            if (itemToSell != null)
            {
                itemToSell.Quantity += changeInQuantity;
                if (itemToSell.Quantity < 1)
                {
                    ItemsInSellCrate.Remove(itemToSell);
                }
            }
            else if (changeInQuantity > 0)
            {
                itemToSell = new PurchasedItem(itemPrefab, changeInQuantity);
                ItemsInSellCrate.Add(itemToSell);
            }
            OnItemsInSellCrateChanged?.Invoke();
        }

        public void ModifyItemQuantityInSellFromSubCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            var itemToSell = ItemsInSellFromSubCrate.Find(i => i.ItemPrefab == itemPrefab);
            if (itemToSell != null)
            {
                itemToSell.Quantity += changeInQuantity;
                if (itemToSell.Quantity < 1)
                {
                    ItemsInSellFromSubCrate.Remove(itemToSell);
                }
            }
            else if (changeInQuantity > 0)
            {
                itemToSell = new PurchasedItem(itemPrefab, changeInQuantity);
                ItemsInSellFromSubCrate.Add(itemToSell);
            }
            OnItemsInSellFromSubCrateChanged?.Invoke();
        }

        public void SellItems(List<PurchasedItem> itemsToSell, Store.StoreTab sellingMode)
        {
            IEnumerable<Item> sellableItems;
            try
            {
                sellableItems = sellingMode switch
                {
                    Store.StoreTab.Sell => GetSellableItems(Character.Controlled),
                    Store.StoreTab.SellSub => GetSellableItemsFromSub(),
                    _ => throw new NotImplementedException()
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error selling items: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
                return;
            }
            bool canAddToRemoveQueue = campaign.IsSinglePlayer && Entity.Spawner != null;
            byte sellerId = GameMain.Client?.ID ?? 0;
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> sellValues = GetSellValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));
            foreach (PurchasedItem item in itemsToSell)
            {
                int itemValue = item.Quantity * sellValues[item.ItemPrefab];
                // check if the store can afford the item
                if (Location.StoreCurrentBalance < itemValue) { continue; }
                // TODO: Write logic for prioritizing certain items over others (e.g. lone Battery Cell should be preferred over one inside a Stun Baton)
                var matchingItems = sellableItems.Where(i => i.Prefab == item.ItemPrefab);
                int count = Math.Min(item.Quantity, matchingItems.Count());
                SoldItem.SellOrigin origin = sellingMode == Store.StoreTab.Sell ? SoldItem.SellOrigin.Character : SoldItem.SellOrigin.Submarine;
                if (origin == SoldItem.SellOrigin.Character || GameMain.IsSingleplayer)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var matchingItem = matchingItems.ElementAt(i);
                        SoldItems.Add(new SoldItem(matchingItem.Prefab, matchingItem.ID, canAddToRemoveQueue, sellerId, origin));
                        SoldEntities.Add(new SoldEntity(matchingItem, campaign.IsSinglePlayer ? SoldEntity.SellStatus.Confirmed : SoldEntity.SellStatus.Local));
                        if (canAddToRemoveQueue) { Entity.Spawner.AddItemToRemoveQueue(matchingItem); }
                    }
                }
                else
                {
                    // When selling from the sub in multiplayer, the server will determine the items that are sold
                    for (int i = 0; i < count; i++)
                    {
                        SoldItems.Add(new SoldItem(item.ItemPrefab, Entity.NullEntityID, canAddToRemoveQueue, sellerId, origin));
                        SoldEntities.Add(new SoldEntity(item.ItemPrefab, SoldEntity.SellStatus.Local));
                    }
                }
                // Exchange money
                Location.StoreCurrentBalance -= itemValue;
                campaign.Bank.Give(itemValue);
                GameAnalyticsManager.AddMoneyGainedEvent(itemValue, GameAnalyticsManager.MoneySource.Store, item.ItemPrefab.Identifier.Value);

                // Remove from the sell crate
                if ((sellingMode == Store.StoreTab.Sell ? ItemsInSellCrate : ItemsInSellFromSubCrate)?.Find(pi => pi.ItemPrefab == item.ItemPrefab) is { } itemToSell)
                {
                    itemToSell.Quantity -= item.Quantity;
                    if (itemToSell.Quantity < 1)
                    {
                        (sellingMode == Store.StoreTab.Sell ? ItemsInSellCrate : ItemsInSellFromSubCrate)?.Remove(itemToSell);
                    }
                }
            }
            OnSoldItemsChanged?.Invoke();
        }

        public void ClearSoldItemsProjSpecific()
        {
            SoldItems.Clear();
            SoldEntities.Clear();
        }
    }
}
