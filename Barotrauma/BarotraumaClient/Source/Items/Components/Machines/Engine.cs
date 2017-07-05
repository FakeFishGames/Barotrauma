using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered
    {
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //isActive = true;
            GuiFrame.Draw(spriteBatch);

            //int width = 300, height = 300;
            //int x = Game1.GraphicsWidth / 2 - width / 2;
            //int y = Game1.GraphicsHeight / 2 - height / 2 - 50;

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            GUI.Font.DrawString(spriteBatch, "Force: " + (int)(targetForce) + " %", new Vector2(GuiFrame.Rect.X + 30, GuiFrame.Rect.Y + 30), Color.White);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }
    }
}
