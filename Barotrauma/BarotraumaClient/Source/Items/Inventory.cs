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

        public Vector2 DrawOffset;
        
        public Color Color;

        public Color BorderHighlightColor;
        private CoroutineHandle BorderHighlightCoroutine;
        
        public Sprite SlotSprite;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }
        
        public GUIComponent.ComponentState EquipButtonState;
        public Rectangle EquipButtonRect
        {
            get
            {
                int buttonDir = Math.Sign(GameMain.GraphicsHeight / 2 - Rect.Center.Y);

                Vector2 equipIndicatorPos = new Vector2(
                    Rect.Center.X - Inventory.equipIndicator.size.X / 2 * Inventory.UIScale,
                    Rect.Center.Y + (Rect.Height / 2 + 20 * Inventory.UIScale) * buttonDir - Inventory.equipIndicator.size.Y / 2 * Inventory.UIScale);
                equipIndicatorPos += DrawOffset;

                return new Rectangle(
                    (int)(equipIndicatorPos.X), (int)(equipIndicatorPos.Y),
                    (int)(Inventory.equipIndicator.size.X * Inventory.UIScale), (int)(Inventory.equipIndicator.size.Y * Inventory.UIScale));
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

        public bool MouseOn()
        {
            Rectangle rect = InteractRect;
            rect.Location += DrawOffset.ToPoint();
            return rect.Contains(PlayerInput.MousePosition);
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

            BorderHighlightColor = Color.Transparent;

            yield return CoroutineStatus.Success;
        }
    }

    partial class Inventory
    {
        public static float UIScale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.0f; }
        }

        protected static Sprite slotSpriteSmall, slotSpriteHorizontal, slotSpriteVertical;
        public static Sprite equipIndicator, equipIndicatorOn;

        public class SlotReference
        {
            public readonly Inventory Inventory;
            public readonly InventorySlot Slot;
            public readonly int SlotIndex;

            public bool IsSubSlot;

            public SlotReference(Inventory inventory, InventorySlot slot, int slotIndex, bool isSubSlot)
            {
                Inventory = inventory;
                Slot = slot;
                SlotIndex = slotIndex;
                IsSubSlot = isSubSlot;
            }
        }

        public static InventorySlot draggingSlot;
        public static Item draggingItem;

        public static Item doubleClickedItem;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected static SlotReference highlightedSubInventorySlot;
        protected static Inventory highlightedSubInventory;

        protected static SlotReference selectedSlot;

        public InventorySlot[] slots;

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
        
        public static SlotReference SelectedSlot
        {
            get { return selectedSlot; }
        }
        
        protected virtual void CreateSlots()
        {
            slots = new InventorySlot[capacity];

            int rectWidth = (int)(60 * UIScale), rectHeight = (int)(60 * UIScale);
            int spacing = (int)(10 * UIScale);

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);
            int columns = Math.Min(slotsPerRow, capacity);

            int startX = (int)centerPos.X - (rectWidth * columns + spacing * (columns - 1)) / 2;
            int startY = (int)centerPos.Y - (rows * (spacing + rectHeight)) / 2;

            Rectangle slotRect = new Rectangle(startX, startY, rectWidth, rectHeight);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = startX + (rectWidth + spacing) * (i % slotsPerRow);
                slotRect.Y = startY + (rectHeight + spacing) * ((int)Math.Floor((double)i / slotsPerRow));

                slots[i] = new InventorySlot(slotRect);
            }

            if (selectedSlot != null && selectedSlot.Inventory == this)
            {
                selectedSlot = new SlotReference(this, slots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot);
            }
        }

        protected virtual bool HideSlot(int i)
        {
            return slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null);
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
                if (HideSlot(i)) continue;
                UpdateSlot(slots[i], i, Items[i], subInventory);
            }
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            Rectangle interactRect = slot.InteractRect;
            interactRect.Location += slot.DrawOffset.ToPoint();
            bool mouseOn = interactRect.Contains(PlayerInput.MousePosition) && !Locked;
            
            slot.State = GUIComponent.ComponentState.None;
            
            if (mouseOn &&
                (draggingItem != null || selectedSlot == null || selectedSlot.Slot == slot) &&
                (highlightedSubInventory == null || highlightedSubInventory == this || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
            {
                slot.State = GUIComponent.ComponentState.Hover;

                if (selectedSlot == null || (!selectedSlot.IsSubSlot && isSubSlot))
                {
                    selectedSlot = new SlotReference(this, slot, slotIndex, isSubSlot);
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
                }               
            }
        }

        protected Inventory GetSubInventory(int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return null;

            var container = item.GetComponent<ItemContainer>();
            if (container == null) return null;

            return container.Inventory;
        }

        public void UpdateSubInventory(float deltaTime, int slotIndex)
        {
            Inventory subInventory = GetSubInventory(slotIndex);
            if (subInventory == null) return;

            if (subInventory.slots == null) subInventory.CreateSlots();

            int itemCapacity = subInventory.Items.Length;
            var slot = slots[slotIndex];
            int dir = Math.Sign(slot.Rect.Y - GameMain.GraphicsHeight / 2);

            Rectangle subRect = slot.Rect;
            subRect.Y = dir > 0 ? slot.EquipButtonRect.Y : slot.EquipButtonRect.Bottom;
            int slotHeight = (int)(60 * UIScale);
            subRect.Height = slotHeight;

            for (int i = 0; i < itemCapacity; i++)
            {
                subRect.Y = subRect.Y - (subRect.Height + (int)(10 * UIScale)) * dir;
                subInventory.slots[i].Rect = subRect;
                subInventory.slots[i].InteractRect = subRect;
                subInventory.slots[i].InteractRect.Inflate((int)(5 * UIScale), (int)(5 * UIScale));

                if (Math.Sign((subRect.Y - slot.DrawOffset.Y) - GameMain.GraphicsHeight * 0.5f) != dir)
                {
                    subRect = slot.Rect;
                    subRect.X = subInventory.slots[i].Rect.Right + (int)(10 * UIScale);
                    subRect.Y = dir > 0 ? slot.EquipButtonRect.Y : slot.EquipButtonRect.Bottom;
                    subRect.Height = slotHeight;
                }
            }

            subInventory.isSubInventory = true;
            
            slots[slotIndex].State = GUIComponent.ComponentState.Hover;

            subInventory.Update(deltaTime, true);
        }


        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots == null || isSubInventory != subInventory) return;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].IsHighlighted;

                DrawSlot(spriteBatch, slots[i], Items[i], drawItem);
            }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                if (slots[i].MouseOn() && Items[i] != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = Items[i].ToString();
                    }
                    else
                    {
                        string description = Items[i].Description;
                        if (Items[i].Prefab.NameMatches("ID Card"))
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
                                description = "This belongs to " + idName + (idJob != null ? ", the " + idJob + "." : ".") + description;
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

            Vector2 pos = new Vector2(highlightedSlot.Right, highlightedSlot.Y);
            pos.X = (int)(pos.X + 3);
            pos.Y = (int)pos.Y - Math.Max((pos.Y + rectSize.Y) - GameMain.GraphicsHeight, 0);

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
            Rectangle containerRect = container.Inventory.slots[0].InteractRect;
            for (int i = 1; i< container.Inventory.slots.Length; i++)
            {
                containerRect = Rectangle.Union(containerRect, container.Inventory.slots[i].InteractRect);
            }

            GUI.DrawRectangle(spriteBatch, new Rectangle(containerRect.X, containerRect.Y, containerRect.Width, containerRect.Height - slot.Rect.Height - 5), Color.Black * 0.8f, true);
            GUI.DrawRectangle(spriteBatch, containerRect, Color.White);

            container.Inventory.Draw(spriteBatch, true);
        }

        public static void UpdateDragging()
        {
            if (draggingItem != null && PlayerInput.LeftButtonReleased())
            {
                if (selectedSlot == null)
                {
                    draggingItem.ParentInventory?.CreateNetworkEvent();
                    draggingItem.Drop();
                    GUI.PlayUISound(GUISoundType.DropItem);
                }
                else if (selectedSlot.Inventory.Items[selectedSlot.SlotIndex] != draggingItem)
                {
                    Inventory selectedInventory = selectedSlot.Inventory;
                    int slotIndex = selectedSlot.SlotIndex;
                    if (selectedInventory.TryPutItem(draggingItem, slotIndex, true, true, Character.Controlled))
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                        GUI.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        if (selectedInventory.slots != null) selectedInventory.slots[slotIndex].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                        GUI.PlayUISound(GUISoundType.PickItemFail);
                    }
                    draggingItem = null;
                    draggingSlot = null;
                }

                draggingItem = null;
            }
            
            if (selectedSlot != null && !selectedSlot.Slot.MouseOn())
            {
                selectedSlot = null;
            }
        }

        public static void DrawDragging(SpriteBatch spriteBatch)
        {
            if (draggingItem == null) return;

            if (draggingSlot == null || (!draggingSlot.MouseOn()))
            {
                Rectangle dragRect = new Rectangle(
                    (int)(PlayerInput.MousePosition.X - 10 * UIScale),
                    (int)(PlayerInput.MousePosition.Y - 10 * UIScale),
                    (int)(80 * UIScale), (int)(80 * UIScale));

                DrawSlot(spriteBatch, new InventorySlot(dragRect), draggingItem);
            }
        }

        public static void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();

            Sprite slotSprite = slot.SlotSprite ?? slotSpriteSmall;
            spriteBatch.Draw(slotSprite.Texture, rect, slotSprite.SourceRect, slot.IsHighlighted ? Color.White : Color.White * 0.8f);

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

            if (slot.BorderHighlightColor != Color.Transparent)
            {
                Rectangle highlightRect = rect;
                highlightRect.Inflate(3, 3);

                GUI.DrawRectangle(spriteBatch, highlightRect, slot.BorderHighlightColor, false, 0, 5);
            }

            if (item == null || !drawItem) return;

            float scale = Math.Min(Math.Min((rect.Width - 10) / item.Sprite.size.X, (rect.Height - 10) / item.Sprite.size.Y), 2.0f);
            Vector2 itemPos = rect.Center.ToVector2();
            if (itemPos.Y > GameMain.GraphicsHeight)
            {
                itemPos.Y -= Math.Min(
                    (itemPos.Y + item.Sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                    (itemPos.Y - item.Sprite.size.Y / 2 * scale) - rect.Y);
            }

            item.Sprite.Draw(spriteBatch, itemPos, item.GetSpriteColor(), 0, scale);
        }
    }
}
