using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class ItemInventory : Inventory
    {
        public override void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (container.InventoryTopSprite != null)
            {

            }

            if (slots != null && slots.Length > 0)
            {
                Rectangle backgroundFrame = slots[0].Rect;
                for (int i = 1; i < capacity; i++)
                {
                    backgroundFrame = Rectangle.Union(backgroundFrame, slots[i].Rect);
                }

                //if no top sprite the top of the frame simply shows the name of the item -> make some room for that
                if (container.InventoryTopSprite == null)
                {
                    backgroundFrame.Inflate(10, 20);
                    backgroundFrame.Location -= new Point(0, 10);
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

                if (container.InventoryTopSprite == null)
                {
                    Item item = Owner as Item;
                    if (item != null)
                    {
                        GUI.DrawString(spriteBatch,
                            new Vector2((int)(backgroundFrame.Center.X - GUI.Font.MeasureString(item.Name).X / 2), (int)backgroundFrame.Y + 5),
                            item.Name, Color.White * 0.9f);
                    }
                }
                else
                {
                    container.InventoryTopSprite.Draw(spriteBatch, new Vector2(backgroundFrame.Center.X, backgroundFrame.Y), 0.0f, UIScale);
                }

                if (container.InventoryBottomSprite != null)
                {
                    container.InventoryBottomSprite.Draw(spriteBatch, new Vector2(backgroundFrame.Center.X, backgroundFrame.Bottom), 0.0f, UIScale);
                }
            }

            base.Draw(spriteBatch, subInventory);
        }
    }
}
