using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace Barotrauma
{
    partial class ItemInventory : Inventory
    {
        private string uiLabel;

        protected override void ControlInput(Camera cam)
        {
            base.ControlInput(cam);
            cam.OffsetAmount = 0;
            //if this is used, we need to implement syncing this inventory with the server
            /*Character.DisableControls = true;
            if (Character.Controlled != null)
            {
                if (PlayerInput.KeyHit(InputType.Select))
                {
                    Character.Controlled.SelectedConstruction = null;
                }
            }*/            
        }

        protected override void CalculateBackgroundFrame()
        {
            var firstSlot = slots.FirstOrDefault();
            if (firstSlot == null) { return; }
            Rectangle frame = firstSlot.Rect;
            frame.Location += firstSlot.DrawOffset.ToPoint();
            for (int i = 1; i < capacity; i++)
            {
                Rectangle slotRect = slots[i].Rect;
                slotRect.Location += slots[i].DrawOffset.ToPoint();
                frame = Rectangle.Union(frame, slotRect);
            }
            BackgroundFrame = new Rectangle(
                frame.X - (int)padding.X,
                frame.Y - (int)padding.Y,
                frame.Width + (int)(padding.X + padding.Z),
                frame.Height + (int)(padding.Y + padding.W));
        }

        public override bool IsInventoryHoverAvailable(Character owner)
        {
            return !container.KeepOpenWhenEquipped || !owner.HasEquippedItem(container.Item);
        }

        public override void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots != null && slots.Length > 0)
            {
                CalculateBackgroundFrame();
                if (container.InventoryBackSprite == null)
                {
                    //draw a black baground for item inventories that don't have a RectTransform
                    //(= ItemContainers that have no GUIFrame or aren't a part of another component's UI)
                    if (RectTransform == null)
                    {
                        GUI.DrawRectangle(spriteBatch, BackgroundFrame, Color.Black * 0.8f, true);
                    }
                }
                else
                {
                    container.InventoryBackSprite.Draw(
                        spriteBatch, BackgroundFrame.Location.ToVector2(), 
                        Color.White, Vector2.Zero, 0, 
                        new Vector2(
                            BackgroundFrame.Width / container.InventoryBackSprite.size.X,
                            BackgroundFrame.Height / container.InventoryBackSprite.size.Y));
                }

                base.Draw(spriteBatch, subInventory);

                if (container.InventoryBottomSprite != null && !subInventory)
                {
                    container.InventoryBottomSprite.Draw(spriteBatch, 
                        new Vector2(BackgroundFrame.Center.X, BackgroundFrame.Bottom) + slots[0].DrawOffset, 
                        0.0f, UIScale);
                }

                if (container.InventoryTopSprite == null)
                {
                    if (uiLabel == null || uiLabel == string.Empty)
                    {
                        if (container.UILabel != null && container.UILabel.Length > 0)
                        {
                            uiLabel = TextManager.Get("UILabel." + container.UILabel);
                        }
                        else
                        {
                            Item item = Owner as Item;
                            uiLabel = item?.Name;
                        }
                    }

                    if (!string.IsNullOrEmpty(uiLabel) && !subInventory)
                    {
                        uiLabel = ToolBox.WrapText(uiLabel, BackgroundFrame.Width, GUI.Font, 1);
                        GUI.DrawString(spriteBatch,
                            new Vector2((int)(BackgroundFrame.Center.X - GUI.Font.MeasureString(uiLabel).X / 2), (int)BackgroundFrame.Y + 5),
                            uiLabel, Color.White * 0.9f);
                    }
                }
                else if (!subInventory)
                {
                    container.InventoryTopSprite.Draw(spriteBatch, new Vector2(BackgroundFrame.Center.X, BackgroundFrame.Y), 0.0f, UIScale);
                }
            }
            else
            {
                base.Draw(spriteBatch, subInventory);
            }
        }
    }
}
