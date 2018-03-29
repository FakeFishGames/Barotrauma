using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class ItemInventory : Inventory
    {
        public override void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (slots != null && slots.Length > 0)
            {
                Rectangle backgroundFrame = slots[0].Rect;
                backgroundFrame.Location += slots[0].DrawOffset.ToPoint();
                for (int i = 1; i < capacity; i++)
                {
                    Rectangle slotRect = slots[i].Rect;
                    slotRect.Location += slots[i].DrawOffset.ToPoint();
                    backgroundFrame = Rectangle.Union(backgroundFrame, slotRect);
                }

                //if no top sprite the top of the frame simply shows the name of the item -> make some room for that
                if (container.InventoryTopSprite == null)
                {
                    if (!subInventory)
                    {
                        backgroundFrame.Inflate(10, 20);
                        backgroundFrame.Location -= new Point(0, 10);
                    }
                }

                if (container.InventoryBackSprite == null)
                {
                    GUI.DrawRectangle(spriteBatch, backgroundFrame, Color.Black * 0.8f, true);
                }
                else
                {
                    container.InventoryBackSprite.Draw(
                        spriteBatch, backgroundFrame.Location.ToVector2(), 
                        Color.White, Vector2.Zero, 0, 
                        new Vector2(
                            backgroundFrame.Width / container.InventoryBackSprite.size.X, 
                            backgroundFrame.Height / container.InventoryBackSprite.size.Y));
                }

                base.Draw(spriteBatch, subInventory);

                if (container.InventoryBottomSprite != null && !subInventory)
                {
                    container.InventoryBottomSprite.Draw(spriteBatch, 
                        new Vector2(backgroundFrame.Center.X, backgroundFrame.Bottom) + slots[0].DrawOffset, 
                        0.0f, UIScale);
                }

                if (container.InventoryTopSprite == null)
                {
                    Item item = Owner as Item;
                    if (item != null && !subInventory)
                    {
                        GUI.DrawString(spriteBatch,
                            new Vector2((int)(backgroundFrame.Center.X - GUI.Font.MeasureString(item.Name).X / 2), (int)backgroundFrame.Y + 5),
                            item.Name, Color.White * 0.9f);
                    }
                }
                else if (!subInventory)
                {
                    container.InventoryTopSprite.Draw(spriteBatch, new Vector2(backgroundFrame.Center.X, backgroundFrame.Y), 0.0f, UIScale);
                }
            }
            else
            {
                base.Draw(spriteBatch, subInventory);
            }
        }
    }
}
