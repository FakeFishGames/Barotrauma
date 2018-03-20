using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                for (int i = 1; i<capacity; i++)
                {
                    backgroundFrame = Rectangle.Union(backgroundFrame, slots[i].Rect);
                }
                backgroundFrame.Inflate(10,20);
                backgroundFrame.Location -= new Point(0, 10);

                GUI.DrawRectangle(spriteBatch, backgroundFrame, Color.Black * 0.8f, true);

                Item item = Owner as Item;
                if (item != null)
                {
                    GUI.DrawString(spriteBatch, 
                        new Vector2((int)(backgroundFrame.Center.X - GUI.Font.MeasureString(item.Name).X / 2), (int)backgroundFrame.Y + 5), 
                        item.Name, Color.White * 0.9f);
                }
            }

            base.Draw(spriteBatch, subInventory);
        }
    }
}
