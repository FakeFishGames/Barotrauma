using System.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Networking;
using System;

namespace Subsurface
{
    class Inventory : Entity
    {
        public static Item draggingItem;
        public static Item doubleClickedItem;

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

        public Inventory(int capacity, Vector2? centerPos = null, int slotsPerRow=5)
        {
            this.capacity = capacity;

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
        public virtual bool TryPutItem(Item item, LimbSlot allowedSlots = 0, bool createNetworkEvent = true)
        {
            int slot = FindAllowedSlot(item);
            if (slot < 0) return false;

            PutItem(item, slot, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool createNetworkEvent = true)
        {
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
            if (item.inventory != null && removeItem) item.inventory.RemoveItem(item);
            items[i] = item;
            item.inventory = this;
            if (item.body!=null)
            {
                item.body.Enabled = false;
            }


            if (createNetworkEvent)
            {
                int[] data = { item.ID, i };
                new NetworkEvent(NetworkEventType.InventoryUpdate, ID, true, data);
            }
        }

        public void RemoveItem(Item item)
        {
            //go through the inventory and remove the item from all slots
            for (int n = 0; n < capacity; n++)
            {
                if (items[n] != item) continue;
                items[n] = null;
            }
        }

        protected virtual void DropItem(Item item)
        {

            item.Drop(null, false);
            return;
        }
        public void DropItem(int i)
        {
            items[i].Drop();
            items[i] = null;
        }

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

            if (draggingItem != null && !draggingItemSlot.Contains(PlayerInput.MousePosition))
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
                    int[] data = { draggingItem.ID, -1 };
                    new NetworkEvent(NetworkEventType.InventoryUpdate, ID, true, data);

                    DropItem(draggingItem);
                }
            }                       
        }

        protected void UpdateSlot(SpriteBatch spriteBatch, Rectangle rect, int slotIndex, Item item, bool isSubSlot)
        {
            bool mouseOn = rect.Contains(PlayerInput.MousePosition);

            DrawSlot(spriteBatch, rect, (draggingItem == item && !mouseOn) ? null : item, mouseOn, isSubSlot);

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

                    selectedSlot = slotIndex;
                    TryPutItem(draggingItem, slotIndex);
                    draggingItem = null;
                }

                if (!isSubSlot) selectedSlot = slotIndex;
            }

            if (selectedSlot == slotIndex)
            {
                if (item == null) return;

                int itemCapacity = item.Capacity;
                if (itemCapacity == 0) return;

                Rectangle containerRect = new Rectangle(rect.X - 5, rect.Y - (rect.Height + 10) * itemCapacity - 5,
                        rect.Width + 10, rect.Height + (rect.Height + 10) * itemCapacity + 10);

                selectedSlot = containerRect.Contains(PlayerInput.MousePosition) ? slotIndex : -1;

                GUI.DrawRectangle(spriteBatch, containerRect, Color.White, false);

                Item[] containedItems = null;
                if (items[slotIndex] != null) containedItems = items[slotIndex].ContainedItems;

                if (containedItems == null || containedItems.Count() == 0) return;

                for (int i = 0; i < itemCapacity; i++)
                {
                    rect.Y = rect.Y - rect.Height - 10;
                    UpdateSlot(spriteBatch, rect, selectedSlot, (i < containedItems.Count()) ? containedItems[i] : null, true);
                }
            }
        }

        protected void DrawSlot(SpriteBatch spriteBatch, Rectangle rect, Item item, bool isHighLighted, bool isSubSlot)
        {
            GUI.DrawRectangle(spriteBatch, rect, (isHighLighted ? Color.Red : Color.White)*((isSubSlot) ? 0.1f : 0.3f), true);

            if (item == null) return;

            item.sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), item.Color);

            if (isHighLighted)
            {
                Vector2 pos = new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height + 20) - GUI.Font.MeasureString(item.Name) * 0.5f;
                pos.X = (int)pos.X;
                pos.Y = (int)pos.Y;
                spriteBatch.DrawString(GUI.Font, item.Name + " - " + item.ID, pos - new Vector2(1.0f, 1.0f), Color.Black);
                spriteBatch.DrawString(GUI.Font, item.Name + " - " + item.ID, pos, Color.White);
            }

            if (item.Condition < 100.0f)
                spriteBatch.DrawString(GUI.Font, (int)item.Condition + " %", new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), Color.Red);
        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            int[] dataArray = data as int[];
            if (dataArray == null || dataArray.Length<2)
            {
                message.Write(-1);
                return;
            }
            //item id
            message.Write(dataArray[0]);
            //index of the slot which the item was moved to
            message.Write(dataArray[1]);
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            int itemId=-1, slotIndex = -1;

            try
            {
                itemId = message.ReadInt32();
                slotIndex = message.ReadInt32();
            }
            catch
            {
                return;
            }

            if (itemId == -1) return;

            Item item = FindEntityByID(itemId) as Item;
            if (item == null) return;

            System.Diagnostics.Debug.WriteLine("Inventory update: "+itemId+"  -  "+slotIndex);

            if (slotIndex==-1)
            {
                if (item.inventory == this) item.Drop();
            }
            else
            {
                TryPutItem(item, slotIndex, false);
            }


        }
    }
}
