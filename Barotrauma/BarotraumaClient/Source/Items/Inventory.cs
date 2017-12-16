using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class InventorySlot
    {
        public Rectangle Rect;

        public Rectangle InteractRect;

        public bool Disabled;

        public GUIComponent.ComponentState State;
        
        public Color Color;

        public Color BorderHighlightColor;
        private CoroutineHandle BorderHighlightCoroutine;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public InventorySlot(Rectangle rect)
        {
            Rect = rect;
            InteractRect = rect;
            InteractRect.Inflate(5, 5);
            State = GUIComponent.ComponentState.None;
            Color = Color.White * 0.4f;
        }

        public void ShowBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            if (BorderHighlightCoroutine != null)
            {
                BorderHighlightCoroutine = null;
            }

            BorderHighlightCoroutine = CoroutineManager.StartCoroutine(UpdateBorderHighlight(color, fadeInDuration, fadeOutDuration));
        }

        private IEnumerable<object> UpdateBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration)
        {
            float t = 0.0f;
            while (t < fadeInDuration + fadeOutDuration)
            {
                BorderHighlightColor = (t < fadeInDuration) ?
                    Color.Lerp(Color.Transparent, color, t / fadeInDuration) :
                    Color.Lerp(color, Color.Transparent, (t - fadeInDuration) / fadeOutDuration);

                t += CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            yield return CoroutineStatus.Success;
        }
    }

    partial class Inventory
    {

        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected int selectedSlot = -1;

        protected InventorySlot[] slots;

        private Vector2 centerPos;

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
                (draggingSlot == null || (!draggingSlot.InteractRect.Contains(PlayerInput.MousePosition) && draggingItem.ParentInventory == this)))
            {
                if (!PlayerInput.LeftButtonHeld())
                {
                    CreateNetworkEvent();
                    draggingItem.Drop();

                    GUI.PlayUISound(GUISoundType.DropItem);
                }
            }

        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            bool mouseOn = slot.InteractRect.Contains(PlayerInput.MousePosition) && !Locked;
            
            slot.State = GUIComponent.ComponentState.None;

            if (!(this is CharacterInventory) && !mouseOn && selectedSlot == slotIndex)
            {
                selectedSlot = -1;
            }

            if (mouseOn &&
                (draggingItem != null || selectedSlot == slotIndex || selectedSlot == -1))
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

                    if (draggingItem != Items[slotIndex])
                    {
                        //selectedSlot = slotIndex;
                        if (TryPutItem(draggingItem, slotIndex, true, Character.Controlled))
                        {
                            if (slots != null) slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                            GUI.PlayUISound(GUISoundType.PickItem);
                        }
                        else
                        {
                            if (slots != null) slots[slotIndex].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                            GUI.PlayUISound(GUISoundType.PickItemFail);
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
                container.Inventory.slots[i].InteractRect = subRect;
                container.Inventory.slots[i].InteractRect.Inflate(5, 5);

            }

            container.Inventory.isSubInventory = true;
            
            slots[slotIndex].State = GUIComponent.ComponentState.Hover;

            container.Inventory.Update(deltaTime, true);
        }


        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Disabled) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].IsHighlighted;

                DrawSlot(spriteBatch, slots[i], Items[i], drawItem);
            }

            if (draggingItem != null &&
                (draggingSlot == null || (!draggingSlot.InteractRect.Contains(PlayerInput.MousePosition) && draggingItem.ParentInventory == this)))
            {
                Rectangle dragRect = new Rectangle(
                    (int)PlayerInput.MousePosition.X - 10,
                    (int)PlayerInput.MousePosition.Y - 10,
                    40, 40);

                DrawSlot(spriteBatch, new InventorySlot(dragRect), draggingItem);
            }

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].InteractRect.Contains(PlayerInput.MousePosition) && !slots[i].Disabled && Items[i] != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = Items[i].ToString();
                    }
                    else
                    {
                        string description = Items[i].Description;
                        if (Items[i].Prefab.Name == "ID Card")
                        {
                            string[] readTags = Items[i].Tags.Split(',');
                            string idName = null;
                            string idJob = null;
                            foreach (string tag in readTags)
                            {
                                string[] s = tag.Split(':');
                                if (s[0] == "name")
                                    idName = s[1];
                                if (s[0] == "job")
                                    idJob = s[1];
                            }
                            if (idName != null)
                                description = "This belongs to " + idName + (idJob != null ? ", the " + idJob + ".\n" : ".\n") + description;
                        }
                        toolTip = string.IsNullOrEmpty(description) ?
                            Items[i].Name :
                            Items[i].Name + '\n' + description;
                    }

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

            Vector2 pos = new Vector2(highlightedSlot.Right, highlightedSlot.Y - rectSize.Y);
            pos.X = (int)(pos.X + 3);
            pos.Y = (int)pos.Y;

            GUI.DrawRectangle(spriteBatch, pos, rectSize, Color.Black * 0.8f, true);
            GUI.Font.DrawString(spriteBatch, toolTip,
                new Vector2((int)(pos.X + rectSize.X * 0.5f), (int)(pos.Y + rectSize.Y * 0.5f)),
                Color.White, 0.0f,
                new Vector2((int)(textSize.X * 0.5f), (int)(textSize.Y * 0.5f)),
                1.0f, SpriteEffects.None, 0.0f);
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

        public static void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;

            GUI.DrawRectangle(spriteBatch, rect, (slot.IsHighlighted ? Color.Red * 0.4f : slot.Color), true);

            if (item != null && drawItem)
            {
                if (item.Condition < item.Prefab.Health)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black * 0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / item.Prefab.Health), 8),
                        Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f) * 0.8f, true);
                }

                var containedItems = item.ContainedItems;
                if (containedItems != null && containedItems.Length == 1 && containedItems[0].Condition < item.Prefab.Health)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 8), Color.Black * 0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Y, (int)(rect.Width * containedItems[0].Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, containedItems[0].Condition / item.Prefab.Health) * 0.8f, true);
                }
            }

            GUI.DrawRectangle(spriteBatch, rect, (slot.IsHighlighted ? Color.Red * 0.4f : slot.Color), false);

            if (slot.BorderHighlightColor != Color.Transparent)
            {
                Rectangle highlightRect = slot.Rect;
                highlightRect.Inflate(3, 3);

                GUI.DrawRectangle(spriteBatch, highlightRect, slot.BorderHighlightColor, false, 0, 5);
            }

            if (item == null || !drawItem) return;

            item.Sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), item.Color);
        }
    }
}
