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
        private ItemContainer container;
        public ItemContainer Container
        {
            get { return container; }
        }

        public ItemInventory(Item owner, ItemContainer container, int capacity, int slotsPerRow = 5)
            : base(owner, capacity, slotsPerRow)
        {
            this.container = container;
        }

        public override int FindAllowedSlot(Item item)
        {
            if (ItemOwnsSelf(item)) { return -1; }

            //item is already in the inventory!
            if (Contains(item)) { return -1; }
            if (!container.CanBeContained(item)) { return -1; }

            //try to stack first
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Any() && CanBePut(item, i)) { return i; }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (CanBePut(item, i)) { return i; }
            }

            return -1;
        }

        public override bool CanBePut(Item item, int i)
        {
            if (ItemOwnsSelf(item)) { return false; }
            if (i < 0 || i >= slots.Length) { return false; }
            if (!container.CanBeContained(item)) { return false; }
            return item != null && slots[i].CanBePut(item) && slots[i].ItemCount < container.MaxStackSize;
        }

        public override bool CanBePut(ItemPrefab itemPrefab, int i)
        {
            if (i < 0 || i >= slots.Length) { return false; }
            if (!container.CanBeContained(itemPrefab)) { return false; }
            return itemPrefab != null && slots[i].CanBePut(itemPrefab) && slots[i].ItemCount < container.MaxStackSize;
        }

        public override int HowManyCanBePut(ItemPrefab itemPrefab, int i)
        {
            if (itemPrefab == null) { return 0; }
            if (i < 0 || i >= slots.Length) { return 0; }
            if (!container.CanBeContained(itemPrefab)) { return 0; }
            return slots[i].HowManyCanBePut(itemPrefab, maxStackSize: Math.Min(itemPrefab.MaxStackSize, container.MaxStackSize));
        }

        public override bool IsFull(bool takeStacksIntoAccount = false)
        {
            if (takeStacksIntoAccount)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (!slots[i].Any()) { return false; }
                    var item = slots[i].FirstOrDefault();
                    if (slots[i].ItemCount < Math.Min(item.Prefab.MaxStackSize, container.MaxStackSize)) { return false; }
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

        public override bool TryPutItem(Item item, Character user, IEnumerable<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, user, allowedSlots, createNetworkEvent);

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
            }

            return wasPut;
        }

        public override bool TryPutItem(Item item, int i, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, i, allowSwapping, allowCombine, user, createNetworkEvent);
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
