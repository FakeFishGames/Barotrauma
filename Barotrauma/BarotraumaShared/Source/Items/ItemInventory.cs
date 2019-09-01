using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class ItemInventory : Inventory
    {
        private ItemContainer container;
        public ItemContainer Container
        {
            get { return container; }
        }

        public ItemInventory(Item owner, ItemContainer container, int capacity, Vector2? centerPos = null, int slotsPerRow = 5)
            : base(owner, capacity, centerPos, slotsPerRow)
        {
            this.container = container;
        }

        public override int FindAllowedSlot(Item item)
        {
            if (ItemOwnsSelf(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (Items[i] == item) return -1;
            }

            if (!container.CanBeContained(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == null) return i;
            }

            return -1;
        }

        public override bool CanBePut(Item item, int i)
        {
            if (ItemOwnsSelf(item)) return false;
            if (i < 0 || i >= Items.Length) return false;
            return (item != null && Items[i] == null && container.CanBeContained(item));
        }

        public override bool TryPutItem(Item item, Character user, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            bool wasPut = base.TryPutItem(item, user, allowedSlots, createNetworkEvent);

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;

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

            if (wasPut)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.HasSelectedItem(item)) continue;
                    
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
                string errorMsg = "Attempted to create a network event for an item (" + container.Item.Name + ") that hasn't been fully initialized yet.\n" + Environment.StackTrace;
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
