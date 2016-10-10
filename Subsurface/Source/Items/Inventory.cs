using System.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class InventorySlot
    {
        public Rectangle Rect;

        public GUIComponent.ComponentState State;

        public bool Disabled;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public Color Color;

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;

            State = GUIComponent.ComponentState.None;

            Color = Color.White * 0.4f;
        }
    }

    class Inventory
    {
        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        public readonly Entity Owner;

        protected float lastUpdate;

        private int slotsPerRow;

        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected int capacity;

        private Vector2 centerPos;

        protected int selectedSlot = -1;

        protected InventorySlot[] slots;
        public Item[] Items;

        private bool isSubInventory;

        public bool Locked;

        public Vector2 CenterPos
        {
            get { return centerPos; }
            set 
            { 
                centerPos = value;
                centerPos.X *= GameMain.GraphicsWidth;
                centerPos.Y *= GameMain.GraphicsHeight;
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

            PutItem(item, slot, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool allowSwapping, bool createNetworkEvent)
        {
            if (Owner == null) return false;
            if (CanBePut(item,i))
            {
                PutItem(item, i, createNetworkEvent);
                return true;
            }
            else
            {
                return false;
            }
        }

        protected virtual void PutItem(Item item, int i, bool createNetworkEvent, bool removeItem = true)
        {
            if (Owner == null) return;

            if (removeItem)
            {
                item.Drop(null, false);
                if (item.ParentInventory != null) item.ParentInventory.RemoveItem(item);
            }

            Items[i] = item;
            item.ParentInventory = this;
            if (item.body != null)
            {
                item.body.Enabled = false;
            }

            if (createNetworkEvent) new NetworkEvent(NetworkEventType.InventoryUpdate, Owner.ID, true, true);            
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

        protected virtual void DropItem(Item item)
        {
            item.Drop(null, false);
            return;
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
                    if (Owner != null)
                    {
                        new NetworkEvent(NetworkEventType.InventoryUpdate, Owner.ID, true);
                    }

                    DropItem(draggingItem);
                }
            }

        }

        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            string toolTip = "";

            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Disabled) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].IsHighlighted;

                DrawSlot(spriteBatch, slots[i], Items[i], drawItem);
            }
            
            if (draggingItem != null &&
                (draggingSlot == null || (!draggingSlot.Rect.Contains(PlayerInput.MousePosition) && draggingItem.ParentInventory == this)))
            {
                Rectangle dragRect = new Rectangle(
                    (int)PlayerInput.MousePosition.X - 10,
                    (int)PlayerInput.MousePosition.Y - 10,
                    40, 40);

                DrawSlot(spriteBatch, new InventorySlot(dragRect), draggingItem);
            }

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].IsHighlighted && !slots[i].Disabled)
                {
                    DrawToolTip(spriteBatch, toolTip, slots[i].Rect);
                    break;
                }
            }
        }

        protected void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot)     
        {
            int maxWidth = 300;

            toolTip = ToolBox.WrapText(toolTip, maxWidth, GUI.Font);
            
            Vector2 textSize = GUI.Font.MeasureString(toolTip);
            Vector2 rectSize = textSize * 1.2f;

            Vector2 pos = new Vector2(highlightedSlot.Right, highlightedSlot.Y-rectSize.Y);
            pos.X = (int)pos.X;
            pos.Y = (int)pos.Y;
            
            GUI.DrawRectangle(spriteBatch, pos, rectSize, Color.Black * 0.8f, true);
            spriteBatch.DrawString(GUI.Font, toolTip,
                new Vector2((int)(pos.X + rectSize.X * 0.5f), (int)(pos.Y + rectSize.Y * 0.5f)),
                Color.White, 0.0f,
                new Vector2((int)(textSize.X * 0.5f), (int)(textSize.Y * 0.5f)),
                1.0f, SpriteEffects.None, 0.0f);
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            bool mouseOn = slot.Rect.Contains(PlayerInput.MousePosition) && !Locked;

            slot.State = GUIComponent.ComponentState.None;

            if (!(this is CharacterInventory) && !mouseOn && selectedSlot==slotIndex)
            {
                selectedSlot = -1;
            }

            if (mouseOn && 
                (draggingItem!=null || selectedSlot==slotIndex || selectedSlot==-1))
            {
                slot.State = GUIComponent.ComponentState.Hover;

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

                    //selectedSlot = slotIndex;
                    TryPutItem(draggingItem, slotIndex, true, true);
                    draggingItem = null;
                    draggingSlot = null;
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
            Rectangle containerRect = new Rectangle(slot.Rect.X - 5, slot.Rect.Y - (40 + 10) * itemCapacity - 5,
                    slot.Rect.Width + 10, slot.Rect.Height + (40 + 10) * itemCapacity + 10);

            Rectangle subRect = slot.Rect;
            subRect.Height = 40;

            for (int i = 0; i < itemCapacity; i++)
            {
                subRect.Y = subRect.Y - subRect.Height - 10;
                container.Inventory.slots[i].Rect = subRect;
            }

            container.Inventory.isSubInventory = true;

            slots[slotIndex].State = GUIComponent.ComponentState.Hover;

            container.Inventory.Update(deltaTime, true);
        }

        public void DrawSubInventory(SpriteBatch spriteBatch, int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return;

            if (container.Inventory.slots == null || !container.Inventory.isSubInventory) return;            

            int itemCapacity = container.Capacity;

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < Items.Length);
#else
            if (slotIndex < 0 || slotIndex >= Items.Length) return;
#endif

            var slot = slots[slotIndex];
            Rectangle containerRect = new Rectangle(slot.Rect.X - 5, slot.Rect.Y - (40 + 10) * itemCapacity - 5,
                    slot.Rect.Width + 10, slot.Rect.Height + (40 + 10) * itemCapacity + 10);
            
            GUI.DrawRectangle(spriteBatch, new Rectangle(containerRect.X, containerRect.Y, containerRect.Width, containerRect.Height - slot.Rect.Height - 5), Color.Black * 0.8f, true);
            GUI.DrawRectangle(spriteBatch, containerRect, Color.White);

            container.Inventory.Draw(spriteBatch, true);

            if (!containerRect.Contains(PlayerInput.MousePosition))
            {
                if (draggingItem == null || draggingItem.Container != item) selectedSlot = -1;
            }         
        }

        protected void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, Item item, bool drawItem=true)
        {
            Rectangle rect = slot.Rect;

            GUI.DrawRectangle(spriteBatch, rect, (slot.IsHighlighted ? Color.Red * 0.4f : slot.Color), true);
            
            if (item != null && drawItem)
            {
                if (item.Condition < 100.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black*0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f)*0.8f, true);
                }

                var containedItems = item.ContainedItems;
                if (containedItems != null && containedItems.Length == 1 && containedItems[0].Condition < 100.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 8), Color.Black*0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Y, (int)(rect.Width * containedItems[0].Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, containedItems[0].Condition / 100.0f)*0.8f, true);
                }                
            }

            GUI.DrawRectangle(spriteBatch, rect, (slot.IsHighlighted ? Color.Red * 0.4f : slot.Color), false);

            if (item == null || !drawItem) return;

            item.Sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), item.Color);
        }

        public virtual bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            for (int i = 0; i < capacity; i++)
            {
                message.Write((ushort)(Items[i]==null ? 0 : Items[i].ID));
            }

            return true;
        }

        public virtual void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime)
        {
            if (sendingTime < lastUpdate) return;

            //List<ushort> newItemIDs = new List<ushort>();
            //List<Item> droppedItems = new List<Item>();
            List<Item> prevItems = new List<Item>(Items);
            
            Client sender = GameMain.Server == null ? null : GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
                       
            for (int i = 0; i < capacity; i++)
            {
                ushort itemId = message.ReadUInt16();
                if (itemId == 0)
                {
                    if (Items[i] != null) Items[i].Drop();
                }
                else
                {
                    var item = Entity.FindEntityByID(itemId) as Item;
                    if (item == null) continue;

                    //if the item is in the inventory of some other character and said character isn't dead/unconscious,
                    //don't let this character pick it up
                    if (GameMain.Server != null)
                    {
                        if (sender == null || sender.Character == null) return;

                        if (!sender.Character.CanAccessItem(item)) continue;                        
                    }

                    TryPutItem(item, i, true, false);
                }
            }
            
            lastUpdate = sendingTime;

            if (GameMain.Server == null) return;
            if (sender != null && sender.Character != null)
            {
                foreach (Item item in Items)
                {
                    if (item == null) continue;
                    if (!prevItems.Contains(item))
                    {
                        GameServer.Log(sender.Character + " placed " + item.Name + " in " + Owner, Color.Orange);
                    }
                }

                foreach (Item item in prevItems)
                {
                    if (item == null) continue;
                    if (!Items.Contains(item))
                    {
                        GameServer.Log(sender.Character + " removed " + item.Name + " from " + Owner.ToString(), Color.Orange);
                    }
                }
            }
        }
    }
}
