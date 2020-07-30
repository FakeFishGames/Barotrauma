using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CargoManager
    {
        private class SoldEntity
        {
            public enum SellStatus
            {
                Confirmed,
                Unconfirmed,
                Local
            }

            public Item Item { get; }
            public SellStatus Status { get; set; }

            private SoldEntity(Item item, SellStatus status)
            {
                Item = item;
                Status = status;
            }

            public static SoldEntity CreateInSinglePlayer(Item item) => new SoldEntity(item, SellStatus.Confirmed);
            public static SoldEntity CreateInMultiPlayer(Item item) => new SoldEntity(item, SellStatus.Local);
        }

        private List<SoldEntity> SoldEntities { get; } = new List<SoldEntity>();

        public List<Item> GetSellableItems(Character character)
        {
            if (character == null) { return new List<Item>(); }

            // Only consider items which have been:
            // a) sold in singleplayer or confirmed by server (SellStatus.Confirmed); or
            // b) sold locally in multiplier (SellStatus.Local), but the client has not received a campaing state update yet after selling them
            var soldEntities = SoldEntities.Where(se => se.Status != SoldEntity.SellStatus.Unconfirmed);

            var sellables = Item.ItemList.FindAll(i => i?.Prefab != null && !i.Removed &&
                i.GetRootInventoryOwner() == character &&
                !i.SpawnedInOutpost &&
                (i.ContainedItems == null || i.ContainedItems.None() || i.ContainedItems.All(ci => soldEntities.Any(se => se.Item == ci))) &&
                i.IsFullCondition && soldEntities.None(se => se.Item == i));

            // Prevent selling things like battery cells from headsets and oxygen tanks from diving masks
            var slots = new List<InvSlotType>() { InvSlotType.Head, InvSlotType.OuterClothes, InvSlotType.Headset };
            foreach (InvSlotType slot in slots)
            {
                var index = character.Inventory.FindLimbSlot(slot);
                if (character.Inventory.Items[index] is Item item && item.ContainedItems != null)
                {
                    foreach (Item containedItem in item.ContainedItems)
                    {
                        if (containedItem != null)
                        {
                            sellables.Remove(containedItem);
                        }
                    }
                }
            }

            return sellables;
        }

        public void SetItemsInBuyCrate(List<PurchasedItem> items)
        {
            ItemsInBuyCrate.Clear();
            ItemsInBuyCrate.AddRange(items);
            OnItemsInBuyCrateChanged?.Invoke();
        }

        public void SetSoldItems(List<SoldItem> items)
        {
            SoldItems.Clear();
            SoldItems.AddRange(items);

            foreach (SoldEntity se in SoldEntities)
            {
                if (se.Status == SoldEntity.SellStatus.Confirmed) { continue; }
                if (SoldItems.Any(si => si.ID == se.Item.ID && si.ItemPrefab == se.Item.Prefab && (GameMain.Client == null || GameMain.Client.ID == si.SellerID)))
                {
                    se.Status = SoldEntity.SellStatus.Confirmed;
                }
                else
                {
                    se.Status = SoldEntity.SellStatus.Unconfirmed;
                }
            }

            OnSoldItemsChanged?.Invoke();
        }

        public void ModifyItemQuantityInSellCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            PurchasedItem itemToSell = ItemsInSellCrate.Find(i => i.ItemPrefab == itemPrefab);
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

        public void SellItems(List<PurchasedItem> itemsToSell)
        {
            var itemsInInventory = GetSellableItems(Character.Controlled);
            var canAddToRemoveQueue = campaign.IsSinglePlayer && Entity.Spawner != null;
            var sellerId = GameMain.Client?.ID ?? 0;

            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = GetSellValueAtCurrentLocation(item.ItemPrefab, quantity: item.Quantity);

                // check if the store can afford the item
                if (location.StoreCurrentBalance < itemValue) { continue; }

                var matchingItems = itemsInInventory.FindAll(i => i.Prefab == item.ItemPrefab);
                if (matchingItems.Count <= item.Quantity)
                {
                    foreach (Item i in matchingItems)
                    {
                        SoldItems.Add(new SoldItem(i.Prefab, i.ID, canAddToRemoveQueue, sellerId));
                        SoldEntities.Add(campaign.IsSinglePlayer ? SoldEntity.CreateInSinglePlayer(i) : SoldEntity.CreateInMultiPlayer(i));
                        if (canAddToRemoveQueue) { Entity.Spawner.AddToRemoveQueue(i); }
                    }
                }
                else
                {
                    for (int i = 0; i < item.Quantity; i++)
                    {
                        var matchingItem = matchingItems[i];
                        SoldItems.Add(new SoldItem(matchingItem.Prefab, matchingItem.ID, canAddToRemoveQueue, sellerId));
                        SoldEntities.Add(campaign.IsSinglePlayer ? SoldEntity.CreateInSinglePlayer(matchingItem) : SoldEntity.CreateInMultiPlayer(matchingItem));
                        if (canAddToRemoveQueue) { Entity.Spawner.AddToRemoveQueue(matchingItem); }
                    }
                }

                // Exchange money
                campaign.Map.CurrentLocation.StoreCurrentBalance -= itemValue;
                campaign.Money += itemValue;

                // Remove from the sell crate
                if (ItemsInSellCrate.Find(pi => pi.ItemPrefab == item.ItemPrefab) is { } itemToSell)
                {
                    itemToSell.Quantity -= item.Quantity;
                    if (itemToSell.Quantity < 1)
                    {
                        ItemsInSellCrate.Remove(itemToSell);
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
