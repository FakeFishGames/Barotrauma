using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public readonly Entity Owner;

        protected int capacity;

        public Item[] Items;

        private bool isSubInventory;

        public bool Locked;

        private ushort[] receivedItemIDs;
        private float syncItemsDelay;
        private CoroutineHandle syncItemsCoroutine;

        public Inventory(Entity owner, int capacity, Vector2? centerPos = null, int slotsPerRow=5)
        {
            this.capacity = capacity;

            this.Owner = owner;


            Items = new Item[capacity];

#if CLIENT
            this.slotsPerRow = slotsPerRow;
            CenterPos = (centerPos==null) ? new Vector2(0.5f, 0.5f) : (Vector2)centerPos;
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
            if (CanBePut(item, i))
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
            }
        }

        private void CreateNetworkEvent()
        {
            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(Owner as IServerSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
#if CLIENT
            else if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(Owner as IClientSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
#endif
        }

        public Item FindItem(string itemName)
        {
            if (itemName == null) return null;
            return Items.FirstOrDefault(i => i != null && (i.Prefab.NameMatches(itemName) || i.HasTag(itemName)));
        }

        public Item FindItem(string[] itemNames)
        {
            if (itemNames == null) return null;

            foreach (string itemName in itemNames)
            {
                var item = FindItem(itemName);
                if (item != null) return item;
            }
            return null;
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
            
        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            ServerWrite(msg, null);

            syncItemsDelay = 1.0f;
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Barotrauma.Networking.Client c)
        {
            List<Item> prevItems = new List<Item>(Items);
            ushort[] newItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                newItemIDs[i] = msg.ReadUInt16();
            }

            if (c == null || c.Character == null || !c.Character.CanAccessInventory(this))
            {
                return;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (newItemIDs[i] == 0)
                {
                    if (Items[i] != null) Items[i].Drop(c.Character);
                    System.Diagnostics.Debug.Assert(Items[i]==null);
                }
                else
                {
                    var item = Entity.FindEntityByID(newItemIDs[i]) as Item;
                    if (item == null || item == Items[i]) continue;

                    if (GameMain.Server != null)
                    {
                        if (!item.CanClientAccess(c)) continue;
                    }
                    TryPutItem(item, i, true, true, c.Character, false);
                }
            }

            GameMain.Server.CreateEntityEvent(Owner as IServerSerializable, new object[] { NetEntityEvent.Type.InventoryState });

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

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            receivedItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                receivedItemIDs[i] = msg.ReadUInt16();
            }

            if (syncItemsDelay > 0.0f)
            {
                //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
                //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);

                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay());
            }
            else
            {
                ApplyReceivedState();
            }
        }

        private IEnumerable<object> SyncItemsAfterDelay()
        {
            while (syncItemsDelay > 0.0f)
            {
                syncItemsDelay -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            if (receivedItemIDs == null) return;

            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] == 0)
                {
                    if (Items[i] != null) Items[i].Drop();
                }
                else
                {
                    var item = Entity.FindEntityByID(receivedItemIDs[i]) as Item;
                    if (item == null) continue;

                    TryPutItem(item, i, true, true, null, false);
                }
            }

            receivedItemIDs = null;
        }
    }
}
