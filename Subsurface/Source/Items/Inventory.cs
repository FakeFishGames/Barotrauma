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

        private int slotsPerRow;

        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected int capacity;

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

        private Vector2 centerPos;

        protected int selectedSlot;

        public Item[] items;

        public Inventory(Entity owner, int capacity, Vector2? centerPos = null, int slotsPerRow=5)
        {
            this.capacity = capacity;

            this.Owner = owner;

            this.slotsPerRow = slotsPerRow;

            items = new Item[capacity];

            CenterPos = (centerPos==null) ? new Vector2(0.5f, 0.5f) : (Vector2)centerPos;
        }

        public int FindIndex(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (items[i] == item) return i;
            }
            return -1;
        }

        public virtual int FindAllowedSlot(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (items[i] == item) return -1;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (items[i] == null) return i;                   
            }
            
            return -1;
        }

        public virtual bool CanBePut(Item item, int i)
        {
            if (i < 0 || i >= items.Length) return false;
            return (items[i] == null);            
        }
        
        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public virtual bool TryPutItem(Item item, List<LimbSlot> allowedSlots = null, bool createNetworkEvent = true)
        {
            int slot = FindAllowedSlot(item);
            if (slot < 0) return false;

            PutItem(item, slot, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool createNetworkEvent = true)
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

            if (item.inventory != null && removeItem)
            {
                item.Drop(null, false);
                if (item.inventory != null) item.inventory.RemoveItem(item);
            }

            items[i] = item;
            item.inventory = this;
            if (item.body != null)
            {
                item.body.Enabled = false;
            }

            if (createNetworkEvent) new NetworkEvent(NetworkEventType.InventoryUpdate, Owner.ID, true, true);            
        }

        public void RemoveItem(Item item)
        {
            //go through the inventory and remove the item from all slots
            for (int n = 0; n < capacity; n++)
            {
                if (items[n] != item) continue;
                items[n] = null;
                item.inventory = null;
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

            int rectWidth = 40, rectHeight = 40;

            int spacing = 10;
            
            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);

            int startX = (int)centerPos.X - (rectWidth * slotsPerRow + spacing * (slotsPerRow - 1)) / 2;
            int startY = (int)centerPos.Y - rows * (spacing + rectHeight);

            Rectangle slotRect = new Rectangle(startX, startY, rectWidth, rectHeight);
            Rectangle draggingItemSlot = slotRect;

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = startX + (rectWidth + spacing) * (i % slotsPerRow);
                slotRect.Y = startY + (rectHeight + spacing) * ((int)Math.Floor((double)i / slotsPerRow));

                if (draggingItem == items[i]) draggingItemSlot = slotRect;

                UpdateSlot(spriteBatch, slotRect, i, items[i], false);                
            }

            if (draggingItem != null && !draggingItemSlot.Contains(PlayerInput.MousePosition) && draggingItem.container == this.Owner)
            {
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                {
                    slotRect.X = PlayerInput.GetMouseState.X - slotRect.Width / 2;
                    slotRect.Y = PlayerInput.GetMouseState.Y - slotRect.Height / 2;
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
        }

        protected void UpdateSlot(SpriteBatch spriteBatch, Rectangle rect, int slotIndex, Item item, bool isSubSlot, bool drawItem=true)
        {
            bool mouseOn = rect.Contains(PlayerInput.MousePosition);

            if (mouseOn)
            {
                if (draggingItem == null)
                {
                    if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                    {
                        draggingItem = item;
                    }  
                }
                else if (PlayerInput.GetMouseState.LeftButton == ButtonState.Released)
                {
                    if (PlayerInput.DoubleClicked())
                    {
                        doubleClickedItem = item;
                    }

                    //selectedSlot = slotIndex;
                    TryPutItem(draggingItem, slotIndex);
                    draggingItem = null;
                }

                if (!isSubSlot && selectedSlot == -1)
                {
                    System.Diagnostics.Debug.WriteLine("DSFG");
                    selectedSlot = slotIndex;
                }
            }

            if (selectedSlot == slotIndex && !isSubSlot)
            {
                selectedSlot = -1;

                int itemCapacity = item==null ? 0 : item.Capacity;
                if (itemCapacity > 0)
                {

#if DEBUG
                    System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < items.Length);
#else
                if (slotIndex<0 || slotIndex>=items.Length) return;
#endif

                    Rectangle containerRect = new Rectangle(rect.X - 5, rect.Y - (40 + 10) * itemCapacity - 5,
                            rect.Width + 10, rect.Height + (40 + 10) * itemCapacity + 10);

                    Rectangle subRect = rect;
                    subRect.Height = 40;


                    selectedSlot = containerRect.Contains(PlayerInput.MousePosition) ? slotIndex : -1;
                    System.Diagnostics.Debug.WriteLine(selectedSlot);

                    GUI.DrawRectangle(spriteBatch, containerRect, Color.Black * 0.8f, true);
                    GUI.DrawRectangle(spriteBatch, containerRect, Color.White);

                    Item[] containedItems = null;
                    if (items[slotIndex] != null) containedItems = items[slotIndex].ContainedItems;

                    if (containedItems != null)
                    {
                        for (int i = 0; i < itemCapacity; i++)
                        {
                            subRect.Y = subRect.Y - subRect.Height - 10;
                            UpdateSlot(spriteBatch, subRect, selectedSlot, i < containedItems.Count() ? containedItems[i] : null, true);
                        }
                    }
                }



            }

            DrawSlot(spriteBatch, rect, (draggingItem == item && !mouseOn) ? null : item, mouseOn, isSubSlot, drawItem);

        }

        protected void DrawSlot(SpriteBatch spriteBatch, Rectangle rect, Item item, bool isHighLighted, bool isSubSlot, bool drawItem=true)
        {
            GUI.DrawRectangle(spriteBatch, rect, (isHighLighted ? Color.Red : Color.White) * ((isSubSlot) ? 0.1f : 0.3f), true);
            GUI.DrawRectangle(spriteBatch, rect, (isHighLighted ? Color.Red : Color.White) * ((isSubSlot) ? 0.2f : 0.4f), false);

            if (item == null || !drawItem) return;

            item.Sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), item.Color);

            if (isHighLighted)
            {
                Vector2 pos = new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height + 20) - GUI.Font.MeasureString(item.Name) * 0.5f;
                pos.X = (int)pos.X;
                pos.Y = (int)pos.Y;
#if DEBUG
                spriteBatch.DrawString(GUI.Font, item.Name+" - "+item.ID, pos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, item.Name+" - "+item.ID, pos, Color.White);
#else
                spriteBatch.DrawString(GUI.Font, item.Name, pos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, item.Name, pos, Color.White);
#endif

            }

            if (item.Condition < 100.0f)
                spriteBatch.DrawString(GUI.Font, (int)item.Condition + " %", new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), Color.Red);
        }

        public virtual bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            var foundItems = Array.FindAll(items, i => i != null);
            message.Write((byte)foundItems.Count());
            foreach (Item item in foundItems)
            {
                message.Write((ushort)item.ID);
            }

            return true;
        }

        public virtual void ReadNetworkData(NetworkEventType type, NetBuffer message)
        {
            List<ushort> newItemIDs = new List<ushort>();

            byte count = message.ReadByte();
            for (int i = 0; i<count; i++)
            {            
                newItemIDs.Add(message.ReadUInt16());
            }
           
            for (int i = 0; i < capacity; i++)
            {
                if (items[i] == null) continue;
                if (!newItemIDs.Contains(items[i].ID))
                {
                    items[i].Drop(null, false);
                    continue;
                }
            }
            foreach (ushort itemId in newItemIDs)
            {
                Item item = Entity.FindEntityByID(itemId) as Item;
                if (item == null) continue;

                TryPutItem(item, item.AllowedSlots, false);
            }
        }
    }
}
