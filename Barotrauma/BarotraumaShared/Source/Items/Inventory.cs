using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public readonly Entity Owner;

        protected int capacity;

        public Item[] Items;
        protected bool[] hideEmptySlot;
        
        public bool Locked;

        private ushort[] receivedItemIDs;
        protected float syncItemsDelay;
        private CoroutineHandle syncItemsCoroutine;

        public int Capacity
        {
            get { return capacity; }
        }

        public Inventory(Entity owner, int capacity, Vector2? centerPos = null, int slotsPerRow = 5)
        {
            this.capacity = capacity;

            this.Owner = owner;

            Items = new Item[capacity];
            hideEmptySlot = new bool[capacity];

#if CLIENT
            this.slotsPerRow = slotsPerRow;

            if (slotSpriteSmall == null)
            {
                slotSpriteSmall = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(532, 395, 75, 71), null, 0);
                slotSpriteVertical = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(672, 218, 75, 144), null, 0);
                slotSpriteHorizontal = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(476, 186, 160, 75), null, 0);
                slotSpriteRound = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(681, 373, 58, 64), null, 0);
                EquipIndicator = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(673, 182, 73, 27), null, 0);
                EquipIndicatorOn = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(679, 108, 67, 21), null, 0);
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
            if (Owner == null) return;

            Inventory prevInventory = item.ParentInventory;

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

            if (createNetworkEvent)
            {
                CreateNetworkEvent();
                //also delay syncing the inventory the item was inside
                if (prevInventory != null && prevInventory != this) prevInventory.syncItemsDelay = 1.0f;
            }
        }

        protected bool TrySwapping(int index, Item item, Character user, bool createNetworkEvent)
        {
            if (item?.ParentInventory == null || Items[index] == null) return false;

            //swap to InvSlotType.Any if possible
            Inventory otherInventory = item.ParentInventory;
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

            //if the item in the slot can be moved to the slot of the moved item
            if (otherInventory.TryPutItem(existingItem, otherIndex, false, false, user, createNetworkEvent) &&
                TryPutItem(item, index, false, false, user, createNetworkEvent))
            {
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

                //swapping the items failed -> move them back to where they were
                otherInventory.TryPutItem(item, otherIndex, false, false, user, createNetworkEvent);
                TryPutItem(existingItem, index, false, false, user, createNetworkEvent);
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

            /*
                    if (character.HasEquippedItem(existingItem) && existingItem.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (Items[i] == existingItem && SlotTypes[i] != InvSlotType.Any)
                            {
                                Items[i] = null;
                            }
                        }
                    }*/

        }

        protected virtual void CreateNetworkEvent()
        {
            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(Owner as IServerSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
#if CLIENT
            else if (GameMain.Client != null)
            {            
                syncItemsDelay = 1.0f;
                GameMain.Client.CreateEntityEvent(Owner as IClientSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
#endif
        }

        public Item FindItemByTag(string tag)
        {
            if (tag == null) return null;
            return Items.FirstOrDefault(i => i != null && i.HasTag(tag));
        }

        public Item FindItemByIdentifier(string identifier)
        {
            if (identifier == null) return null;
            return Items.FirstOrDefault(i => i != null && i.Prefab.Identifier == identifier);
        }

        /*public Item FindItem(string[] itemNames)
        {
            if (itemNames == null) return null;

            foreach (string itemName in itemNames)
            {
                var item = FindItem(itemName);
                if (item != null) return item;
            }
            return null;
        }*/

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
            
        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            ServerWrite(msg, null);
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            List<Item> prevItems = new List<Item>(Items);
            ushort[] newItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                newItemIDs[i] = msg.ReadUInt16();
            }

            
            if (c == null || c.Character == null) return;

            bool accessible = c.Character.CanAccessInventory(this);
            if (this is CharacterInventory && accessible)
            {
                if (Owner == null || !(Owner is Character))
                {
                    accessible = false;
                }
                else if (!((CharacterInventory)this).AccessibleWhenAlive && !((Character)Owner).IsDead)
                {
                    accessible = false;
                }
            }

            if (!accessible)
            {
                //create a network event to correct the client's inventory state
                //otherwise they may have an item in their inventory they shouldn't have been able to pick up,
                //and receiving an event for that inventory later will cause the item to be dropped
                CreateNetworkEvent();
                for (int i = 0; i < capacity; i++)
                {
                    var item = Entity.FindEntityByID(newItemIDs[i]) as Item;
                    if (item == null) continue;
                    if (item.ParentInventory != null && item.ParentInventory != this)
                    {
                        item.ParentInventory.CreateNetworkEvent();
                    }
                }
                return;
            }
            
            List<Inventory> prevItemInventories = new List<Inventory>(Items.Select(i => i?.ParentInventory));

            for (int i = 0; i < capacity; i++)
            {
                Item newItem = newItemIDs[i] == 0 ? null : Entity.FindEntityByID(newItemIDs[i]) as Item;
                prevItemInventories.Add(newItem?.ParentInventory);

                if (newItemIDs[i] == 0 || (newItem != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop();
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (newItemIDs[i] > 0)
                {
                    var item = Entity.FindEntityByID(newItemIDs[i]) as Item;
                    if (item == null || item == Items[i]) continue;

                    if (GameMain.Server != null)
                    {
                        var holdable = item.GetComponent<Holdable>();
                        if (holdable != null && !holdable.CanBeDeattached()) continue;

                        if (!item.CanClientAccess(c)) continue;
                    }
                    TryPutItem(item, i, true, true, c.Character, false);
                }
            }

            CreateNetworkEvent();
            foreach (Inventory prevInventory in prevItemInventories.Distinct())
            {
                if (prevInventory != this) prevInventory?.CreateNetworkEvent();
            }

            foreach (Item item in Items.Distinct())
            {
                if (item == null) continue;
                if (!prevItems.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(c.Character.LogName+ " picked up " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character.LogName + " placed " + item.Name + " in " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
            foreach (Item item in prevItems.Distinct())
            {
                if (item == null) continue;
                if (!Items.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(c.Character.LogName + " dropped " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character.LogName + " removed " + item.Name + " from " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            for (int i = 0; i < capacity; i++)
            {
                msg.Write((ushort)(Items[i] == null ? 0 : Items[i].ID));
            }
        }
    }
}
