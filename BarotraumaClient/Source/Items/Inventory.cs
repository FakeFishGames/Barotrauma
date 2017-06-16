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
    partial class InventorySlot
    {
        public GUIComponent.ComponentState State;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public Color Color;

        public Color BorderHighlightColor;
        private CoroutineHandle BorderHighlightCoroutine;

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
                if (slots[i].IsHighlighted && !slots[i].Disabled && Items[i] != null)
                {
                    string toolTip = "";
                    if (GameMain.DebugDraw)
                    {
                        toolTip = Items[i].ToString();
                    }
                    else
                    {
                        toolTip = string.IsNullOrEmpty(Items[i].Description) ?
                            Items[i].Name :
                            Items[i].Name + '\n' + Items[i].Description;
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

        protected void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, Item item, bool drawItem = true)
        {
            Rectangle rect = slot.Rect;

            GUI.DrawRectangle(spriteBatch, rect, (slot.IsHighlighted ? Color.Red * 0.4f : slot.Color), true);

            if (item != null && drawItem)
            {
                if (item.Condition < 100.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Bottom - 8, rect.Width, 8), Color.Black * 0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Bottom - 8, (int)(rect.Width * item.Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, item.Condition / 100.0f) * 0.8f, true);
                }

                var containedItems = item.ContainedItems;
                if (containedItems != null && containedItems.Length == 1 && containedItems[0].Condition < 100.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, 8), Color.Black * 0.8f, true);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(rect.X, rect.Y, (int)(rect.Width * containedItems[0].Condition / 100.0f), 8),
                        Color.Lerp(Color.Red, Color.Green, containedItems[0].Condition / 100.0f) * 0.8f, true);
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
