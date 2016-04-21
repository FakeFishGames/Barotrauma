using System.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class Inventory
    {
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

        public Item[] Items;

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

        protected void PutItem(Item item, int i, bool createNetworkEvent, bool removeItem = true)
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

        public void RemoveItem(Item item)
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
        //public void DropItem(int i)
        //{
        //    items[i].Drop();
        //    items[i] = null;
        //}

        public virtual void Draw(SpriteBatch spriteBatch)
        {
            doubleClickedItem = null;

            string toolTip = "";

            int rectWidth = 40, rectHeight = 40;

            Rectangle highlightedSlot = Rectangle.Empty;
            int spacing = 10;
            
            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);

            int startX = (int)centerPos.X - (rectWidth * slotsPerRow + spacing * (slotsPerRow - 1)) / 2;
            int startY = (int)centerPos.Y - rows * (spacing + rectHeight);

            Rectangle slotRect = new Rectangle(startX, startY, rectWidth, rectHeight);
            Rectangle draggingItemSlot = slotRect;

            selectedSlot = -1;

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = startX + (rectWidth + spacing) * (i % slotsPerRow);
                slotRect.Y = startY + (rectHeight + spacing) * ((int)Math.Floor((double)i / slotsPerRow));

                if (draggingItem == Items[i]) draggingItemSlot = slotRect;

                UpdateSlot(spriteBatch, slotRect, i, Items[i], false);
                if (slotRect.Contains(PlayerInput.MousePosition) && Items[i] != null)
                {
                    highlightedSlot = slotRect;
                    toolTip = GameMain.DebugDraw ? Items[i].ToString() : Items[i].Name;
                }
            }

            if (draggingItem != null && !draggingItemSlot.Contains(PlayerInput.MousePosition) && draggingItem.Container == this.Owner)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    slotRect.X = (int)PlayerInput.MousePosition.X - slotRect.Width / 2;
                    slotRect.Y = (int)PlayerInput.MousePosition.Y - slotRect.Height / 2;
                    //GUI.DrawRectangle(spriteBatch, rect, Color.White, true);
                    //draggingItem.sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), Color.White);

                    DrawSlot(spriteBatch, slotRect, draggingItem, false, false);
                }
                else
                {
                    if (Owner!=null)
                    {
                        new NetworkEvent(NetworkEventType.InventoryUpdate, Owner.ID, true);
                    }

                    DropItem(draggingItem);
                    //draggingItem = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(toolTip))
            {
                DrawToolTip(spriteBatch, toolTip, highlightedSlot);
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

        protected void UpdateSlot(SpriteBatch spriteBatch, Rectangle rect, int slotIndex, Item item, bool isSubSlot, float alpha = 0.4f, bool drawItem=true)
        {
            bool mouseOn = rect.Contains(PlayerInput.MousePosition) && !Locked;

            if (mouseOn)
            {

                if (!isSubSlot && selectedSlot == -1)
                {
                    selectedSlot = slotIndex;
                }

                if (draggingItem == null)
                {
                    if (PlayerInput.LeftButtonHeld() && selectedSlot == slotIndex)
                    {
                        draggingItem = item;
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
                }

            }

            DrawSlot(spriteBatch, rect, (draggingItem == item && !mouseOn) ? null : item, mouseOn && selectedSlot == slotIndex, isSubSlot, alpha, drawItem);

        }

        public void DrawSubInventory(SpriteBatch spriteBatch, Rectangle rect, int slotIndex)
        {
            var item = Items[slotIndex];

            selectedSlot = -1;

            int itemCapacity = item == null ? 0 : item.Capacity;
            if (itemCapacity == 0) return;            

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < Items.Length);
#else
            if (slotIndex < 0 || slotIndex >= Items.Length) return;
#endif

            Rectangle containerRect = new Rectangle(rect.X - 5, rect.Y - (40 + 10) * itemCapacity - 5,
                    rect.Width + 10, rect.Height + (40 + 10) * itemCapacity + 10);

            Rectangle subRect = rect;
            subRect.Height = 40;

            selectedSlot = containerRect.Contains(PlayerInput.MousePosition) && !Locked ? slotIndex : -1;

            GUI.DrawRectangle(spriteBatch, containerRect, Color.Black * 0.8f, true);
            GUI.DrawRectangle(spriteBatch, containerRect, Color.White);

            Item[] containedItems = null;
            if (Items[slotIndex] != null) containedItems = Items[slotIndex].ContainedItems;

            if (containedItems != null)
            {
                for (int i = 0; i < itemCapacity; i++)
                {
                    subRect.Y = subRect.Y - subRect.Height - 10;
                    UpdateSlot(spriteBatch, subRect, selectedSlot, i < containedItems.Count() ? containedItems[i] : null, true);
                }
            }
            

            
        }

        protected void DrawSlot(SpriteBatch spriteBatch, Rectangle rect, Item item, bool isHighLighted, bool isSubSlot, float alpha=0.4f, bool drawItem=true)
        {
            GUI.DrawRectangle(spriteBatch, rect, (isHighLighted ? Color.Red : Color.White) * alpha*0.75f, true);
            
            if (item != null)
            {
                if (item.Condition < 100.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black*0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f)*0.8f, true);
                }

                if (!isHighLighted)
                {
                    var containedItems = item.ContainedItems;
                    if (containedItems != null && containedItems.Length == 1 && containedItems[0].Condition < 100.0f)
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 8), Color.Black*0.8f, true);
                        GUI.DrawRectangle(spriteBatch,
                            new Rectangle(rect.X, rect.Y, (int)(rect.Width * containedItems[0].Condition / 100.0f), 8),
                            Color.Lerp(Color.Red, Color.Green, containedItems[0].Condition / 100.0f)*0.8f, true);
                    }
                }
            }


            GUI.DrawRectangle(spriteBatch, rect, (isHighLighted ? Color.Red : Color.White) * alpha, false);

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
                       
            for (int i = 0; i < capacity; i++)
            {
                ushort itemId = message.ReadUInt16();
                if (itemId==0)
                {
                    if (Items[i] != null) Items[i].Drop();
                }
                else
                {
                    var item = Entity.FindEntityByID(itemId) as Item;
                    if (item == null) continue;

                    TryPutItem(item, i, true, false);
                }
            }
            
            lastUpdate = sendingTime;

            if (GameMain.Server == null) return;
            var sender = GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
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
