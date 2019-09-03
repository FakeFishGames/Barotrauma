using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public readonly Entity Owner;

        protected readonly int capacity;

        public Item[] Items;
        protected bool[] hideEmptySlot;
        
        public bool Locked;

        protected float syncItemsDelay;

        public int Capacity
        {
            get { return capacity; }
        }

        public Inventory(Entity owner, int capacity, int slotsPerRow = 5)
        {
            this.capacity = capacity;

            this.Owner = owner;

            Items = new Item[capacity];
            hideEmptySlot = new bool[capacity];

#if CLIENT
            this.slotsPerRow = slotsPerRow;

            if (slotSpriteSmall == null)
            {
                //TODO: define these in xml
                slotSpriteSmall = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(532, 395, 75, 71), null, 0);
                slotSpriteVertical = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(672, 218, 75, 144), null, 0);
                slotSpriteHorizontal = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(476, 186, 160, 75), null, 0);
                slotSpriteRound = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(681, 373, 58, 64), null, 0);

                EquipIndicator = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(673, 182, 73, 27), new Vector2(0.5f, 0.5f), 0);
                EquipIndicatorHighlight = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(679, 108, 67, 21), new Vector2(0.5f, 0.5f), 0);
                DropIndicator = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(870, 55, 73, 66), new Vector2(0.5f, 0.75f), 0);
                DropIndicatorHighlight = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(946, 54, 73, 66), new Vector2(0.5f, 0.75f), 0);
            }
#endif
        }

        public int FindIndex(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == item) return i;
            }
            return -1;
        }
        
        /// Returns true if the item owns any of the parent inventories
        public virtual bool ItemOwnsSelf(Item item)
        {
            if (Owner == null) return false;
            if (!(Owner is Item)) return false;
            Item ownerItem = Owner as Item;
            if (ownerItem == item) return true;
            if (ownerItem.ParentInventory == null) return false;
            return ownerItem.ParentInventory.ItemOwnsSelf(item);
        }

        public virtual int FindAllowedSlot(Item item)
        {
            if (ItemOwnsSelf(item)) return -1;

            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (Items[i] == item) return -1;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == null) return i;                   
            }

            return -1;
        }

        public bool CanBePut(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (CanBePut(item, i)) { return true; }
            }
            return false;
        }

        public virtual bool CanBePut(Item item, int i)
        {
            if (ItemOwnsSelf(item)) return false;
            if (i < 0 || i >= Items.Length) return false;
            return (Items[i] == null);            
        }
        
        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public virtual bool TryPutItem(Item item, Character user, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            int slot = FindAllowedSlot(item);
            if (slot < 0) return false;

            PutItem(item, slot, user, true, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true)
        {
            if (i < 0 || i >= Items.Length)
            {
                string errorMsg = "Inventory.TryPutItem failed: index was out of range(" + i + ").\n" + Environment.StackTrace;
                GameAnalyticsManager.AddErrorEventOnce("Inventory.TryPutItem:IndexOutOfRange", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return false;
            }

            if (Owner == null) return false;
            //there's already an item in the slot
            if (Items[i] != null && allowCombine)
            {
                if (Items[i].Combine(item))
                {
                    System.Diagnostics.Debug.Assert(Items[i] != null);
                    return true;
                }
            }
            if (Items[i] != null && item.ParentInventory != null && allowSwapping)
            {
                return TrySwapping(i, item, user, createNetworkEvent);
            }
            else if (CanBePut(item, i))
            {
                PutItem(item, i, user, true, createNetworkEvent);
                return true;
            }
            else
            {
#if CLIENT
                if (slots != null && createNetworkEvent) slots[i].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
#endif
                return false;
            }
        }

        protected virtual void PutItem(Item item, int i, Character user, bool removeItem = true, bool createNetworkEvent = true)
        {
            if (i < 0 || i >= Items.Length)
            {
                string errorMsg = "Inventory.PutItem failed: index was out of range(" + i + ").\n" + Environment.StackTrace;
                GameAnalyticsManager.AddErrorEventOnce("Inventory.PutItem:IndexOutOfRange", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            if (Owner == null) return;

            Inventory prevInventory = item.ParentInventory;

            if (createNetworkEvent)
            {
                CreateNetworkEvent();
                //also delay syncing the inventory the item was inside
                if (prevInventory != null && prevInventory != this) prevInventory.syncItemsDelay = 1.0f;
            }

            if (removeItem)
            {
                item.Drop(user);
                if (item.ParentInventory != null) item.ParentInventory.RemoveItem(item);
            }

            Items[i] = item;
            item.ParentInventory = this;

#if CLIENT
            if (slots != null) slots[i].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
#endif

            if (item.body != null)
            {
                item.body.Enabled = false;
            }
        }

        public bool IsEmpty()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] != null) return false;
            }

            return true;
        }

        public bool IsFull()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == null) return false;
            }

            return true;
        }

        protected bool TrySwapping(int index, Item item, Character user, bool createNetworkEvent)
        {
            if (item?.ParentInventory == null || Items[index] == null) return false;

            //swap to InvSlotType.Any if possible
            Inventory otherInventory = item.ParentInventory;
            bool otherIsEquipped = false;
            int otherIndex = -1;
            for (int i = 0; i < otherInventory.Items.Length; i++)
            {
                if (otherInventory.Items[i] != item) continue;
                if (otherInventory is CharacterInventory characterInventory)
                {
                    if (characterInventory.SlotTypes[i] == InvSlotType.Any)
                    {
                        otherIndex = i;
                        break;
                    }
                    else
                    {
                        otherIsEquipped = true;
                    }
                }
            }

            if (otherIndex == -1) otherIndex = Array.IndexOf(otherInventory.Items, item);
            Item existingItem = Items[index];

            for (int j = 0; j < otherInventory.capacity; j++)
            {
                if (otherInventory.Items[j] == item) otherInventory.Items[j] = null;
            }
            for (int j = 0; j < capacity; j++)
            {
                if (Items[j] == existingItem) Items[j] = null;
            }

            bool swapSuccessful = false;
            if (otherIsEquipped)
            {
                swapSuccessful =
                    TryPutItem(item, index, false, false, user, createNetworkEvent) && 
                    otherInventory.TryPutItem(existingItem, otherIndex, false, false, user, createNetworkEvent);
            }
            else
            {
                swapSuccessful = 
                    otherInventory.TryPutItem(existingItem, otherIndex, false, false, user, createNetworkEvent) &&
                    TryPutItem(item, index, false, false, user, createNetworkEvent);
            }

            //if the item in the slot can be moved to the slot of the moved item
            if (swapSuccessful)
            {
                System.Diagnostics.Debug.Assert(Items[index] == item, "Something when wrong when swapping items, item is not present in the inventory.");
                System.Diagnostics.Debug.Assert(otherInventory.Items[otherIndex] == existingItem, "Something when wrong when swapping items, item is not present in the other inventory.");
#if CLIENT
                if (slots != null)
                {
                    for (int j = 0; j < capacity; j++)
                    {
                        if (Items[j] == item) slots[j].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);                            
                    }
                    for (int j = 0; j < otherInventory.capacity; j++)
                    {
                        if (otherInventory.Items[j] == existingItem) otherInventory.slots[j].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);                            
                    }
                }
#endif
                return true;
            }
            else
            {
                for (int j = 0; j < capacity; j++)
                {
                    if (Items[j] == item) Items[j] = null;
                }
                for (int j = 0; j < otherInventory.capacity; j++)
                {
                    if (otherInventory.Items[j] == existingItem) otherInventory.Items[j] = null;
                }

                if (otherIsEquipped)
                {
                    TryPutItem(existingItem, index, false, false, user, createNetworkEvent);
                    otherInventory.TryPutItem(item, otherIndex, false, false, user, createNetworkEvent);
                }
                else
                {
                    otherInventory.TryPutItem(item, otherIndex, false, false, user, createNetworkEvent);
                    TryPutItem(existingItem, index, false, false, user, createNetworkEvent);
                }

                //swapping the items failed -> move them back to where they were
                //otherInventory.TryPutItem(item, otherIndex, false, false, user, createNetworkEvent);
                //TryPutItem(existingItem, index, false, false, user, createNetworkEvent);
#if CLIENT                
                if (slots != null)
                {
                    for (int j = 0; j < capacity; j++)
                    {
                        if (Items[j] == existingItem)
                        {
                            slots[j].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                        }
                    }
                }
#endif
                return false;
            }
        }

        public virtual void CreateNetworkEvent()
        {
            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetworkMember.IsClient) { syncItemsDelay = 1.0f; }
                GameMain.NetworkMember.CreateEntityEvent(Owner as INetSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
        }

        public Item FindItem(Func<Item, bool> predicate, bool recursive)
        {
            Item match = Items.FirstOrDefault(i => i != null && predicate(i));
            if (match == null && recursive)
            {
                foreach (var item in Items)
                {
                    if (item == null) { continue; }
                    if (item.OwnInventory != null)
                    {
                        match = item.OwnInventory.FindItem(predicate, true);
                        if (match != null)
                        {
                            return match;
                        }
                    }
                }
            }
            return match;
        }

        public Item FindItemByTag(string tag, bool recursive = false)
        {
            if (tag == null) { return null; }
            return FindItem(i => i.HasTag(tag), recursive);
        }

        public Item FindItemByIdentifier(string identifier, bool recursive = false)
        {
            if (identifier == null) return null;
            return FindItem(i => i.Prefab.Identifier == identifier, recursive);
        }

        public virtual void RemoveItem(Item item)
        {
            if (item == null) return;

            //go through the inventory and remove the item from all slots
            for (int n = 0; n < capacity; n++)
            {
                if (Items[n] != item) continue;
                
                Items[n] = null;
                item.ParentInventory = null;                
            }
        }

        public void SharedWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write((byte)capacity);
            for (int i = 0; i < capacity; i++)
            {
                msg.Write((ushort)(Items[i] == null ? 0 : Items[i].ID));
            }
        }
        
        /// <summary>
        /// Deletes all items inside the inventory (and also recursively all items inside the items)
        /// </summary>
        public void DeleteAllItems()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == null) continue;
                foreach (ItemContainer itemContainer in Items[i].GetComponents<ItemContainer>())
                {
                    itemContainer.Inventory.DeleteAllItems();
                }
                Items[i].Remove();
            }
        }        
    }
}
