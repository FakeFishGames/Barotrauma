using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered, IDrawableComponent
    {
        private float spriteIndex;
        
        public float AnimSpeed
        {
            get;
            private set;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);

            GUI.Font.DrawString(spriteBatch, TextManager.Get("Force") + ": " + (int)(targetForce) + " %", new Vector2(GuiFrame.Rect.X + 30, GuiFrame.Rect.Y + 30), Color.White);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }

        partial void UpdateAnimation(float deltaTime)
        {
            if (propellerSprite == null) return;

            spriteIndex += (force / 100.0f) * AnimSpeed * deltaTime;
            if (spriteIndex < 0) spriteIndex = propellerSprite.FrameCount;
            if (spriteIndex >= propellerSprite.FrameCount) spriteIndex = 0.0f;
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (propellerSprite != null)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += PropellerPos;
                drawPos.Y = -drawPos.Y;

                propellerSprite.Draw(spriteBatch, (int)Math.Floor(spriteIndex), drawPos, Color.White, propellerSprite.Origin, 0.0f, Vector2.One);
            }
        }
    }
}
