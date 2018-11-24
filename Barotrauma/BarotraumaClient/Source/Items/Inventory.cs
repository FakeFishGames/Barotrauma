using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
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

        public static SlotReference SelectedSlot
        {
            get { return selectedSlot; }
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

            if (selectedSlot != null && selectedSlot.Inventory == this)
            {
                selectedSlot = new SlotReference(this, slots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot);
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
                UpdateSlot(slots[i], i, Items[i], subInventory);
            }
        }

        protected void UpdateSlot(InventorySlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            bool mouseOn = slot.InteractRect.Contains(PlayerInput.MousePosition) && !Locked;
            
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
            new Rectangle(slot.Rect.X - 5, slot.Rect.Y - (40 + 10) * itemCapacity - 5,
                    slot.Rect.Width + 10, slot.Rect.Height + (40 + 10) * itemCapacity + 10);

            Rectangle subRect = slot.Rect;
            subRect.Height = 40;

            for (int i = 0; i < itemCapacity; i++)
            {
                subRect.Y = subRect.Y - subRect.Height - 10;
                subInventory.slots[i].Rect = subRect;
                subInventory.slots[i].InteractRect = subRect;
                subInventory.slots[i].InteractRect.Inflate(5, 5);

                if (subRect.Y < GameMain.GraphicsHeight * 0.4f)
                {
                    subRect = slot.Rect;
                    subRect.X = subInventory.slots[i].Rect.Right + 10;
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
                if (slots[i].Disabled) continue;

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != Items[i] || slots[i].IsHighlighted;

                DrawSlot(spriteBatch, slots[i], Items[i], drawItem);
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
            
            if (selectedSlot != null && !selectedSlot.Slot.InteractRect.Contains(PlayerInput.MousePosition))
            {
                selectedSlot = null;
            }
        }

        public static void DrawDragging(SpriteBatch spriteBatch)
        {
            if (draggingItem == null) return;

            if (draggingSlot == null || (!draggingSlot.InteractRect.Contains(PlayerInput.MousePosition)))
            {
                Rectangle dragRect = new Rectangle(
                    (int)PlayerInput.MousePosition.X - 10,
                    (int)PlayerInput.MousePosition.Y - 10,
                    40, 40);

                DrawSlot(spriteBatch, new InventorySlot(dragRect), draggingItem);
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

            item.Sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), item.GetSpriteColor());
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            receivedItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                receivedItemIDs[i] = msg.ReadUInt16();
            }

            //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
            //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession

            //also delay if we're still midround syncing, some of the items in the inventory may not exist yet
            if (syncItemsDelay > 0.0f || GameMain.Client.MidRoundSyncing)
            {
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);
                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay());
            }
            else
            {
                if (syncItemsCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(syncItemsCoroutine);
                    syncItemsCoroutine = null;
                }
                ApplyReceivedState();
            }
        }

        private IEnumerable<object> SyncItemsAfterDelay()
        {
            while (syncItemsDelay > 0.0f || (GameMain.Client != null && GameMain.Client.MidRoundSyncing))
            {
                syncItemsDelay = Math.Max((float)(syncItemsDelay - Timing.Step), 0.0f);
                yield return CoroutineStatus.Running;
            }

            if (Owner.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] == 0 || (Entity.FindEntityByID(receivedItemIDs[i]) as Item != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop();
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }
            
            for (int i = 0; i < capacity; i++)
            {
                if (receivedItemIDs[i] > 0)
                {
                    var item = Entity.FindEntityByID(receivedItemIDs[i]) as Item;
                    if (item == null || item == Items[i]) continue;
                    TryPutItem(item, i, true, true, null, false);
                }
            }

            receivedItemIDs = null;
        }
    }
}
