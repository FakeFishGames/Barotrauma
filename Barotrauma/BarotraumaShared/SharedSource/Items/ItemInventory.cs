using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class ItemInventory : Inventory
    {
        private readonly ItemContainer container;
        public ItemContainer Container
        {
            get { return container; }
        }

        public ItemInventory(Item owner, ItemContainer container, int capacity, int slotsPerRow = 5)
            : base(owner, capacity, slotsPerRow)
        {
            this.container = container;
        }

        public override int FindAllowedSlot(Item item, bool ignoreCondition = false)
        {
            if (ItemOwnsSelf(item)) { return -1; }

            //item is already in the inventory!
            if (Contains(item)) { return -1; }
            if (!container.CanBeContained(item)) { return -1; }

            //try to stack first
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Any() && CanBePutInSlot(item, i, ignoreCondition)) { return i; }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (CanBePutInSlot(item, i, ignoreCondition)) { return i; }
            }

            return -1;
        }

        public override bool CanBePutInSlot(Item item, int i, bool ignoreCondition = false)
        {
            if (ItemOwnsSelf(item)) { return false; }
            if (i < 0 || i >= slots.Length) { return false; }
            if (!container.CanBeContained(item, i)) { return false; }
            return item != null && slots[i].CanBePut(item, ignoreCondition) && slots[i].ItemCount < container.GetMaxStackSize(i);
        }

        public override bool CanBePutInSlot(ItemPrefab itemPrefab, int i, float? condition, int? quality = null)
        {
            if (i < 0 || i >= slots.Length) { return false; }
            if (!container.CanBeContained(itemPrefab, i)) { return false; }
            return itemPrefab != null && slots[i].CanBePut(itemPrefab, condition, quality) && slots[i].ItemCount < container.GetMaxStackSize(i);
        }

        public override int HowManyCanBePut(ItemPrefab itemPrefab, int i, float? condition)
        {
            if (itemPrefab == null) { return 0; }
            if (i < 0 || i >= slots.Length) { return 0; }
            if (!container.CanBeContained(itemPrefab, i)) { return 0; }
            return slots[i].HowManyCanBePut(itemPrefab, maxStackSize: Math.Min(itemPrefab.MaxStackSize, container.GetMaxStackSize(i)), condition);
        }

        public override bool IsFull(bool takeStacksIntoAccount = false)
        {
            if (takeStacksIntoAccount)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (!slots[i].Any()) { return false; }
                    var item = slots[i].FirstOrDefault();
                    if (slots[i].ItemCount < Math.Min(item.Prefab.MaxStackSize, container.GetMaxStackSize(i))) { return false; }
                }
            }
            else
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (!slots[i].Any()) { return false; }
                }
            }

            return true;
        }

        public override bool TryPutItem(Item item, Character user, IEnumerable<InvSlotType> allowedSlots = null, bool createNetworkEvent = true, bool ignoreCondition = false)
        {
            bool wasPut = base.TryPutItem(item, user, allowedSlots, createNetworkEvent, ignoreCondition);

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HeldItems.Contains(item)) { continue; }
                    item.Unequip(c);
                    break;
                }

                container.IsActive = true;
                container.OnItemContained(item);
#if SERVER
                GameMain.Server?.KarmaManager?.OnItemContained(item, container.Item, user);
#endif
            }

            return wasPut;
        }

        public override bool TryPutItem(Item item, int i, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true, bool ignoreCondition = false)
        {
            bool wasPut = base.TryPutItem(item, i, allowSwapping, allowCombine, user, createNetworkEvent, ignoreCondition);
            if (wasPut && item.ParentInventory == this)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HeldItems.Contains(item)) { continue; }                    
                    item.Unequip(c);
                    break;                    
                }

                container.IsActive = true;
                container.OnItemContained(item);
#if SERVER
                GameMain.Server?.KarmaManager?.OnItemContained(item, container.Item, user);
#endif
            }

            return wasPut;
        }

        public override void CreateNetworkEvent()
        {
            if (!Item.ItemList.Contains(container.Item))
            {
                string errorMsg = "Attempted to create a network event for an item (" + container.Item.Name + ") that hasn't been fully initialized yet.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "ItemInventory.CreateServerEvent:EventForUninitializedItem" + container.Item.Name + container.Item.ID,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            int componentIndex = container.Item.GetComponentIndex(container);
            if (componentIndex == -1)
            {
                DebugConsole.Log("Creating a network event for the item \"" + container.Item + "\" failed, ItemContainer not found in components");
                return;
            }
            
            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetworkMember.IsClient) { syncItemsDelay = 1.0f; }
                GameMain.NetworkMember.CreateEntityEvent(Owner as INetSerializable, new object[] { NetEntityEvent.Type.InventoryState, componentIndex });
            }
        }    

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            container.OnItemRemoved(item);
        }
    }
}
