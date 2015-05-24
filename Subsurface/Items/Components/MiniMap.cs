using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Items.Components
{
    class MiniMap : Powered
    {

        public MiniMap(Item item, XElement element)
            : base(item, element)
        {
            isActive = true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            currPowerConsumption = powerConsumption;
            
            voltage = 0.0f;
        }
        
        public override bool Pick(Character picker)
        {
            if (picker == null) return false;

            picker.SelectedConstruction = item;

            return true;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int width = 500, height = 400;
            int x = Game1.GraphicsWidth / 2 - width / 2;
            int y = Game1.GraphicsHeight / 2 - height / 2;


            GUI.DrawRectangle(spriteBatch, new Rectangle(x,y,width,height), Color.Black, true);

            Rectangle miniMap = new Rectangle(x + 20, y + 40, width - 40, height - 60);

            float size = Math.Min((float)miniMap.Width / (float)Map.Borders.Width, (float)miniMap.Height / (float)Map.Borders.Height);
            foreach (Hull hull in Hull.hullList)
            {
                Rectangle hullRect = new Rectangle(
                    miniMap.X + (int)((hull.Rect.X - Map.Borders.X) * size),
                    miniMap.Y - (int)((hull.Rect.Y - Map.Borders.Y) * size),
                    (int)(hull.Rect.Width * size), 
                    (int)(hull.Rect.Height * size));

                float waterAmount = Math.Min(hull.Volume / hull.FullVolume, 1.0f);

                if (hullRect.Height * waterAmount > 1.0f)
                {
                    Rectangle waterRect = new Rectangle(
                        hullRect.X, 
                        (int)(hullRect.Y + hullRect.Height * (1.0f - waterAmount)),
                        hullRect.Width, 
                        (int)(hullRect.Height * waterAmount));

                    GUI.DrawRectangle(spriteBatch, waterRect, Color.DarkBlue, true);
                }

                GUI.DrawRectangle(spriteBatch, hullRect, Color.White);
            }

            foreach (Character c in Character.characterList)
            {
                if (c.animController.CurrentHull!=null) continue;

                Rectangle characterRect = new Rectangle(
                    miniMap.X + (int)((c.Position.X - Map.Borders.X) * size),
                    miniMap.Y - (int)((c.Position.Y - Map.Borders.Y) * size),
                    5, 5);

                GUI.DrawRectangle(spriteBatch, characterRect, Color.White, true);
            }
        }

    }
}
