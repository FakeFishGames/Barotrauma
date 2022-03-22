using Barotrauma.Extensions;
using Barotrauma.Items.Components;
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
                /// <summary>
                /// Entity sold in SP. Or, entity sold by client and confirmed by server in MP.
                /// </summary>
                Confirmed,
                /// <summary>
                /// Entity sold by client in MP. Client has received at least one update from server after selling, but this entity wasn't yet confirmed.
                /// </summary>
                Unconfirmed,
                /// <summary>
                /// Entity sold by client in MP. Client hasn't yet received an update from server after selling.
                /// </summary>
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

        public IEnumerable<Item> GetSellableItemsFromSub()
        {
            if (Submarine.MainSub == null) { return new List<Item>(); }
            var confirmedSoldEntities = GetConfirmedSoldEntities();
            return Submarine.MainSub.GetItems(true).FindAll(item =>
            {
                if (!IsItemSellable(item, confirmedSoldEntities)) { return false; }
                if (item.GetRootInventoryOwner() is Character) { return false; }
                if (!item.Components.All(c => !(c is Holdable h) || !h.Attachable || !h.Attached)) { return false; }
                if (!item.Components.All(c => !(c is Wire w) || w.Connections.All(c => c == null))) { return false; }
                if (!ItemAndAllContainersInteractable(item)) { return false; }
                return true;
            }).Distinct();

            static bool ItemAndAllContainersInteractable(Item item)
            {
                do
                {
                    if (!item.IsPlayerTeamInteractable) { return false; }
                    item = item.Container;
                } while (item != null);
                return true;
            }
        }

        private IEnumerable<SoldEntity> GetConfirmedSoldEntities()
        {
            // Only consider items which have been:
            // a) sold in singleplayer or confirmed by server (SellStatus.Confirmed); or
            // b) sold locally in multiplayer (SellStatus.Local), but the client has not received a campaing state update yet after selling them
            return SoldEntities.Where(se => se.Status != SoldEntity.SellStatus.Unconfirmed);
        }

        private bool IsItemSellable(Item item, IEnumerable<SoldEntity> confirmedSoldEntities)
        {
            if (!item.Prefab.CanBeSold) { return false; }
            if (item.SpawnedInCurrentOutpost) { return false; }
            if (!item.Prefab.AllowSellingWhenBroken && item.ConditionPercentage < 90.0f) { return false; }
            if (confirmedSoldEntities.Any(it => it.Item == item)) { return false; }
            if (item.OwnInventory?.Container is ItemContainer itemContainer)
            {
                var containedItems = item.ContainedItems;
                if (containedItems.None()) { return true; }
                // Allow selling the item if contained items are unsellable and set to be removed on deconstruct
                if (itemContainer.RemoveContainedItemsOnDeconstruct && containedItems.All(it => !it.Prefab.CanBeSold)) { return true; }
                // Otherwise there must be no contained items or the contained items must be confirmed as sold
                if (!containedItems.All(it => confirmedSoldEntities.Any(se => se.Item == it))) { return false; }
            }
            return true;
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
            var sellableItems = sellingMode switch
            {
                Store.StoreTab.Sell => GetSellableItems(Character.Controlled),
                Store.StoreTab.SellFromSub => GetSellableItemsFromSub(),
                _ => throw new System.NotImplementedException(),
            }; 
            bool canAddToRemoveQueue = campaign.IsSinglePlayer && Entity.Spawner != null;
            var sellerId = GameMain.Client?.ID ?? 0;

            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> sellValues = GetSellValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));

            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = item.Quantity * sellValues[item.ItemPrefab];

                // check if the store can afford the item
                if (Location.StoreCurrentBalance < itemValue) { continue; }

                // TODO: Write logic for prioritizing certain items over others (e.g. lone Battery Cell should be preferred over one inside a Stun Baton)
                var matchingItems = sellableItems.Where(i => i.Prefab == item.ItemPrefab);
                if (matchingItems.Count() <= item.Quantity)
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
                        var matchingItem = matchingItems.ElementAt(i);
                        SoldItems.Add(new SoldItem(matchingItem.Prefab, matchingItem.ID, canAddToRemoveQueue, sellerId));
                        SoldEntities.Add(campaign.IsSinglePlayer ? SoldEntity.CreateInSinglePlayer(matchingItem) : SoldEntity.CreateInMultiPlayer(matchingItem));
                        if (canAddToRemoveQueue) { Entity.Spawner.AddToRemoveQueue(matchingItem); }
                    }
                }

                // Exchange money
                Location.StoreCurrentBalance -= itemValue;
                campaign.Money += itemValue;

                // Remove from the sell crate
                // TODO: Simplify duplicate logic?
                if (sellingMode == Store.StoreTab.Sell && ItemsInSellCrate.Find(pi => pi.ItemPrefab == item.ItemPrefab) is { } inventoryItem)
                {
                    inventoryItem.Quantity -= item.Quantity;
                    if (inventoryItem.Quantity < 1)
                    {
                        ItemsInSellCrate.Remove(inventoryItem);
                    }
                }
                else if(sellingMode == Store.StoreTab.SellFromSub && ItemsInSellFromSubCrate.Find(pi => pi.ItemPrefab == item.ItemPrefab) is { } subItem)
                {
                    subItem.Quantity -= item.Quantity;
                    if (subItem.Quantity < 1)
                    {
                        ItemsInSellFromSubCrate.Remove(subItem);
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
