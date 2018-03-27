using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

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
                    Rect.Center.X - Inventory.EquipIndicator.size.X / 2 * Inventory.UIScale,
                    Rect.Center.Y + (Rect.Height / 2 + 20 * Inventory.UIScale) * buttonDir - Inventory.EquipIndicator.size.Y / 2 * Inventory.UIScale);
                equipIndicatorPos += DrawOffset;

                return new Rectangle(
                    (int)(equipIndicatorPos.X), (int)(equipIndicatorPos.Y),
                    (int)(Inventory.EquipIndicator.size.X * Inventory.UIScale), (int)(Inventory.EquipIndicator.size.Y * Inventory.UIScale));
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
        public static Sprite EquipIndicator, EquipIndicatorOn;

        public float HideTimer;

        public class SlotReference
        {
            public readonly Inventory ParentInventory;
            public readonly InventorySlot Slot;
            public readonly int SlotIndex;

            public Inventory Inventory;

            public bool IsSubSlot;

            public SlotReference(Inventory parentInventory, InventorySlot slot, int slotIndex, bool isSubSlot, Inventory subInventory = null)
            {
                ParentInventory = parentInventory;
                Slot = slot;
                SlotIndex = slotIndex;
                Inventory = subInventory;
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

        protected static HashSet<SlotReference> highlightedSubInventorySlots = new HashSet<SlotReference>();
        //protected static List<Inventory> highlightedSubInventories = new List<Inventory>();

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

            if (selectedSlot != null && selectedSlot.ParentInventory == this)
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

            if (selectedSlot != null && selectedSlot.Slot != slot)
            {
                //subinventory slot highlighted -> don't allow highlighting this one
                if (selectedSlot.IsSubSlot && !isSubSlot)
                {
                    mouseOn = false;
                }
                else if (!selectedSlot.IsSubSlot && isSubSlot && mouseOn)
                {
                    selectedSlot = null;
                }
            }

            
            slot.State = GUIComponent.ComponentState.None;
            
            if (mouseOn && (draggingItem != null || selectedSlot == null || selectedSlot.Slot == slot))  
                // &&
                //(highlightedSubInventories.Count == 0 || highlightedSubInventories.Contains(this) || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
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

        float openState;

        public void UpdateSubInventory(float deltaTime, int slotIndex)
        {
            Inventory subInventory = GetSubInventory(slotIndex);
            if (subInventory == null) return;

            if (subInventory.slots == null) subInventory.CreateSlots();

            int itemCapacity = subInventory.Items.Length;
            var slot = slots[slotIndex];
            int dir = Math.Sign(slot.Rect.Y - GameMain.GraphicsHeight / 2);

            Rectangle subRect = slot.Rect;
            subRect.Width = slots[slotIndex].SlotSprite == null ? (int)(60 * UIScale) : (int)(slots[slotIndex].SlotSprite.size.X * UIScale);
            subRect.Height = (int)(60 * UIScale);

            int spacing = (int)(10 * UIScale);

            int columns = slot.Rect.Width / subRect.Width;
            while (itemCapacity / columns * (subRect.Height + spacing) > GameMain.GraphicsHeight * 0.5f)
            {
                columns++;
            }

            int startX = slot.Rect.Center.X - (int)(subRect.Width * (columns / 2.0f) + spacing * ((columns - 1) / 2.0f));
            subRect.X = startX;
            int startY = dir > 0 ? slot.Rect.Y - subRect.Height - (int)(10 * UIScale) : slot.Rect.Bottom + (int)(40 * UIScale);
            subRect.Y = startY;

            float totalHeight = itemCapacity / columns * (subRect.Height + spacing);

            openState = Math.Min(openState + deltaTime, 1.0f);

            for (int i = 0; i < itemCapacity; i++)
            { 
                subInventory.slots[i].Rect = subRect;
                subInventory.slots[i].Rect.Location += new Point(0, (int)totalHeight * dir);

                subInventory.slots[i].DrawOffset = Vector2.Lerp(subInventory.slots[i].DrawOffset, 
                    subInventory.HideTimer >= 0.5f ? new Vector2(0, -totalHeight * dir) : new Vector2(0, 50 * dir), 
                    deltaTime * 10.0f);

                subInventory.slots[i].InteractRect = subInventory.slots[i].Rect;
                subInventory.slots[i].InteractRect.Inflate((int)(5 * UIScale), (int)(5 * UIScale));

                if ((i + 1) % columns == 0)
                {
                    subRect.X = startX;
                    subRect.Y -= subRect.Height * dir;
                    subRect.Y -= spacing * dir;
                }
                else
                {
                    subRect.X = subInventory.slots[i].Rect.Right + spacing;
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
        }

        protected static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot)
        {
            int maxWidth = 300;

            toolTip = ToolBox.WrapText(toolTip, maxWidth, GUI.Font);

            Vector2 textSize = GUI.Font.MeasureString(toolTip);
            Vector2 rectSize = textSize * 1.2f;

            Vector2 pos = new Vector2(highlightedSlot.Right, highlightedSlot.Y);
            pos.X = (int)(pos.X + 3);
            pos.Y = (int)pos.Y - Math.Max((pos.Y + rectSize.Y) - GameMain.GraphicsHeight, 0);

            if (pos.X + rectSize.X > GameMain.GraphicsWidth) pos.X -= rectSize.X + highlightedSlot.Width;

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



            /*var slot = slots[slotIndex];
            Rectangle containerRect = container.Inventory.slots[0].InteractRect;
            for (int i = 1; i< container.Inventory.slots.Length; i++)
            {
                containerRect = Rectangle.Union(containerRect, container.Inventory.slots[i].InteractRect);
            }

            GUI.DrawRectangle(spriteBatch, new Rectangle(containerRect.X, containerRect.Y, containerRect.Width, containerRect.Height - slot.Rect.Height - 5), Color.Black * 0.8f, true);
            GUI.DrawRectangle(spriteBatch, containerRect, Color.White);*/

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;

            Point topLeft =
                container.Inventory.slots[0].Rect.Location +
                container.Inventory.slots[0].DrawOffset.ToPoint();
            Point bottomRight = 
                container.Inventory.slots[0].Rect.Location + 
                container.Inventory.slots[0].Rect.Size +
                container.Inventory.slots[0].DrawOffset.ToPoint();

            for (int i = 1; i < container.Inventory.slots.Length; i++)
            {
                topLeft.X = (int)Math.Min(topLeft.X, container.Inventory.slots[i].Rect.X + container.Inventory.slots[i].DrawOffset.X);
                topLeft.Y = (int)Math.Min(topLeft.Y, container.Inventory.slots[i].Rect.Y + container.Inventory.slots[i].DrawOffset.Y);
                bottomRight.X = (int)Math.Max(bottomRight.X, container.Inventory.slots[i].Rect.Right + container.Inventory.slots[i].DrawOffset.X);
                bottomRight.Y = (int)Math.Min(bottomRight.Y, container.Inventory.slots[i].Rect.Bottom + container.Inventory.slots[i].DrawOffset.Y);
            }

            if (container.InventoryTopSprite != null)
            {
                topLeft.Y -= (int)container.InventoryTopSprite.Origin.Y;
            }

            int dir = Math.Sign(GameMain.GraphicsHeight * 0.5f - slots[slotIndex].Rect.Center.Y);

            if (dir > 0)
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    new Point(slots[slotIndex].Rect.X, slots[slotIndex].Rect.Bottom),
                    new Point(bottomRight.X - topLeft.X, (int)Math.Max(bottomRight.Y - slots[slotIndex].Rect.Bottom, 0)));
            }
            else
            {
                spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                    topLeft,
                    new Point(bottomRight.X - topLeft.X, (int)Math.Max((slots[slotIndex].Rect.Y + slots[slotIndex].DrawOffset.Y) - topLeft.Y, 0)));
            }

            container.Inventory.Draw(spriteBatch, true);
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;

            container.InventoryBottomSprite?.Draw(spriteBatch,
                new Vector2(slots[slotIndex].Rect.Center.X, slots[slotIndex].Rect.Y) + slots[slotIndex].DrawOffset,
                0.0f, UIScale);

            container.InventoryTopSprite?.Draw(spriteBatch,
                new Vector2(
                    slots[slotIndex].Rect.Center.X, 
                    container.Inventory.slots[container.Inventory.slots.Length - 1].Rect.Y) + container.Inventory.slots[container.Inventory.slots.Length - 1].DrawOffset,
                0.0f, UIScale);

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
                else if (selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex] != draggingItem)
                {
                    Inventory selectedInventory = selectedSlot.ParentInventory;
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

        public static void DrawFront(SpriteBatch spriteBatch)
        {
            if (GameMain.GameSession?.CrewManager?.CrewCommander != null &&
                GameMain.GameSession.CrewManager.CrewCommander.IsOpen)
            {
                return;
            }

            foreach (var slot in highlightedSubInventorySlots)
            {
                int slotIndex = Array.IndexOf(slot.ParentInventory.slots, slot.Slot);
                if (slotIndex > 0 && slotIndex < slot.ParentInventory.slots.Length)
                {
                    slot.ParentInventory.DrawSubInventory(spriteBatch, slotIndex);                    
                }
            }            

            if (draggingItem != null)
            {
                if (draggingSlot == null || (!draggingSlot.MouseOn()))
                {
                    Rectangle dragRect = new Rectangle(
                        (int)(PlayerInput.MousePosition.X - 10 * UIScale),
                        (int)(PlayerInput.MousePosition.Y - 10 * UIScale),
                        (int)(80 * UIScale), (int)(80 * UIScale));

                    DrawSlot(spriteBatch, new InventorySlot(dragRect), draggingItem);
                }
            }

            if (selectedSlot != null)
            {
                Item item = selectedSlot.ParentInventory.Items[selectedSlot.SlotIndex];
                if (item != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = item.ToString();
                    }
                    else
                    {
                        string description = item.Description;
                        if (item.Prefab.NameMatches("ID Card"))
                        {
                            string[] readTags = item.Tags.Split(',');
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
                            item.Name :
                            item.Name + '\n' + description;
                    }

                    Rectangle slotRect = selectedSlot.Slot.Rect;
                    slotRect.Location += selectedSlot.Slot.DrawOffset.ToPoint();
                    DrawToolTip(spriteBatch, toolTip, slotRect);
                }
            }

        }

        public static void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();

            var itemContainer = item?.GetComponent<ItemContainer>();
            if (itemContainer != null && (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null))
            {
                if (!highlightedSubInventorySlots.Any(s => s.Slot == slot))
                {
                    itemContainer.InventoryBottomSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                    itemContainer.InventoryTopSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                }

                drawItem = false;
            }
            else
            {
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
            }

            if (GameMain.DebugDraw) GUI.DrawRectangle(spriteBatch, rect, Color.White, false, 0, 1);

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
