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

        public IEnumerable<Item> GetSellableItems(Character character)
        {
            if (character == null) { return new List<Item>(); }
            // Only consider items which have been:
            // a) sold in singleplayer or confirmed by server (SellStatus.Confirmed); or
            // b) sold locally in multiplayer (SellStatus.Local), but the client has not received a campaing state update yet after selling them
            var confirmedSoldEntities = SoldEntities.Where(se => se.Status != SoldEntity.SellStatus.Unconfirmed);
            // The bag slot is intentionally left out since we want to be able to sell items from there
            var equipmentSlots = new List<InvSlotType>() { InvSlotType.Head, InvSlotType.InnerClothes, InvSlotType.OuterClothes, InvSlotType.Headset, InvSlotType.Card };
            return character.Inventory.FindAllItems(item =>
            {
                if (item.SpawnedInOutpost) { return false; }
                if (!item.Prefab.AllowSellingWhenBroken && item.ConditionPercentage < 90.0f) { return false; }
                if (confirmedSoldEntities.Any(it => it.Item == item)) { return false; }
                // There must be no contained items or the contained items must be confirmed as sold
                if (!item.ContainedItems.All(it => confirmedSoldEntities.Any(se => se.Item == it))) { return false; }
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

            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> sellValues = GetSellValuesAtCurrentLocation(itemsToSell.Select(i => i.ItemPrefab));

            foreach (PurchasedItem item in itemsToSell)
            {
                var itemValue = item.Quantity * sellValues[item.ItemPrefab];

                // check if the store can afford the item
                if (Location.StoreCurrentBalance < itemValue) { continue; }

                // TODO: Write logic for prioritizing certain items over others (e.g. lone Battery Cell should be preferred over one inside a Stun Baton)
                var matchingItems = itemsInInventory.Where(i => i.Prefab == item.ItemPrefab);
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
