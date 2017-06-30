using System.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class InventorySlot
    {
        public Rectangle Rect;
        
        public bool Disabled;

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;

#if CLIENT
            State = GUIComponent.ComponentState.None;

            Color = Color.White * 0.4f;
#endif
        }

    }

    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        public readonly Entity Owner;
        
        private int slotsPerRow;

        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected int capacity;

        protected int selectedSlot = -1;

        protected InventorySlot[] slots;
        public Item[] Items;

        private bool isSubInventory;

        public bool Locked;

        private ushort[] receivedItemIDs;
        private float syncItemsDelay;
        private CoroutineHandle syncItemsCoroutine;

        private Vector2 centerPos;

        public Vector2 CenterPos
        {
            get { return centerPos; }
            set 
            { 
                centerPos = value;
#if CLIENT
                centerPos.X *= GameMain.GraphicsWidth;
                centerPos.Y *= GameMain.GraphicsHeight;
#endif
            }
        }

        private Vector2 drawOffset;
        public Vector2 DrawOffset
        {
            get
            {
                return drawOffset;
            }

            set
            {
                if (value == drawOffset) return;

                drawOffset = value;
                CreateSlots();
            } 
        }

        public Inventory(Entity owner, int capacity, Vector2? centerPos = null, int slotsPerRow=5)
        {
            this.capacity = capacity;

            this.Owner = owner;

            this.slotsPerRow = slotsPerRow;

            Items = new Item[capacity];

            CenterPos = (centerPos==null) ? new Vector2(0.5f, 0.5f) : (Vector2)centerPos;
        }

        public int FindIndex(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == item) return i;
            }
            return -1;
        }

        public virtual int FindAllowedSlot(Item item)
        {
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
            if (i < 0 || i >= Items.Length) return false;
            return (Items[i] == null);            
        }
        
        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public virtual bool TryPutItem(Item item, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            int slot = FindAllowedSlot(item);
            if (slot < 0) return false;

            PutItem(item, slot, true, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool allowSwapping, bool createNetworkEvent = true)
        {
            if (Owner == null) return false;
            if (CanBePut(item,i))
            {
                PutItem(item, i, true, createNetworkEvent);
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

        protected virtual void PutItem(Item item, int i, bool removeItem = true, bool createNetworkEvent = true)
        {
            if (Owner == null) return;

            if (removeItem)
            {
                item.Drop(null);
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

            return Items.FirstOrDefault(i => i != null && (i.Name == itemName || i.HasTag(itemName)));
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
                
        protected virtual void CreateSlots()
        {
            slots = new InventorySlot[capacity];

            int rectWidth = 40, rectHeight = 40;
            int spacing = 10;

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);

            int startX = (int)centerPos.X - (rectWidth * slotsPerRow + spacing * (slotsPerRow - 1)) / 2;
            int startY = (int)centerPos.Y - rows * (spacing + rectHeight);

            Rectangle slotRect = new Rectangle(startX, startY, rectWidth, rectHeight);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = startX + (rectWidth + spacing) * (i % slotsPerRow) + (int)DrawOffset.X;
                slotRect.Y = startY + (rectHeight + spacing) * ((int)Math.Floor((double)i / slotsPerRow)) + (int)DrawOffset.Y;

                slots[i] = new InventorySlot(slotRect);
            }
        }

        public virtual void Update(float deltaTime, bool subInventory = false)
        {
            syncItemsDelay = Math.Max(syncItemsDelay - deltaTime, 0.0f);

            if (slots == null || isSubInventory != subInventory)
            {
                CreateSlots();
                isSubInventory = subInventory;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Disabled) continue;
                UpdateSlot(slots[i], i, Items[i], false);
            }


            if (draggingItem != null &&
                (draggingSlot == null || (!draggingSlot.Rect.Contains(PlayerInput.MousePosition) && draggingItem.ParentInventory == this)))
            {
                if (!PlayerInput.LeftButtonHeld())
                {
                    CreateNetworkEvent();

                    draggingItem.Drop();
                }
            }

        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            bool mouseOn = slot.Rect.Contains(PlayerInput.MousePosition) && !Locked;

#if CLIENT
            slot.State = GUIComponent.ComponentState.None;
#endif

            if (!(this is CharacterInventory) && !mouseOn && selectedSlot==slotIndex)
            {
                selectedSlot = -1;
            }

            if (mouseOn && 
                (draggingItem!=null || selectedSlot==slotIndex || selectedSlot==-1))
            {
#if CLIENT
                slot.State = GUIComponent.ComponentState.Hover;
#endif

                if (!isSubSlot && selectedSlot == -1)
                {
                    selectedSlot = slotIndex;
                }

                if (draggingItem == null)
                {
                    if (PlayerInput.LeftButtonHeld())
                    {
                        draggingItem = Items[slotIndex];
                        draggingSlot = slot;
                    }  
                }
                else if (PlayerInput.LeftButtonReleased())
                {
                    if (PlayerInput.DoubleClicked())
                    {
                        doubleClickedItem = item;
                    }

                    if (draggingItem != Items[slotIndex])
                    {
                        //selectedSlot = slotIndex;
                        if (TryPutItem(draggingItem, slotIndex, true))
                        {
#if CLIENT
                            if (slots != null) slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
#endif
                        }
                        else
                        {
#if CLIENT
                            if (slots != null) slots[slotIndex].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
#endif
                        }
                        draggingItem = null;
                        draggingSlot = null;
                    }
                }
            }
        }

        public void UpdateSubInventory(float deltaTime, int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return;

            if (container.Inventory.slots == null) container.Inventory.CreateSlots();

            int itemCapacity = container.Capacity;

            var slot = slots[slotIndex];
            new Rectangle(slot.Rect.X - 5, slot.Rect.Y - (40 + 10) * itemCapacity - 5,
                    slot.Rect.Width + 10, slot.Rect.Height + (40 + 10) * itemCapacity + 10);

            Rectangle subRect = slot.Rect;
            subRect.Height = 40;

            for (int i = 0; i < itemCapacity; i++)
            {
                subRect.Y = subRect.Y - subRect.Height - 10;
                container.Inventory.slots[i].Rect = subRect;
            }
            
            container.Inventory.isSubInventory = true;

#if CLIENT
            slots[slotIndex].State = GUIComponent.ComponentState.Hover;
#endif

            container.Inventory.Update(deltaTime, true);
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
                    TryPutItem(item, i, true, false);
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
                        GameServer.Log(c.Character + " picked up " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character + " placed " + item.Name + " in " + Owner, ServerLog.MessageType.Inventory);
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
                        GameServer.Log(c.Character + " dropped " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character + " removed " + item.Name + " from " + Owner, ServerLog.MessageType.Inventory);
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

                    TryPutItem(item, i, true, false);
                }
            }

            receivedItemIDs = null;
        }
    }
}
