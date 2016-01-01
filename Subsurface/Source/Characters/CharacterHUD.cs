using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class CharacterHUD
    {

        private static Sprite statusIcons;

        private static GUIProgressBar drowningBar, healthBar;

        private static float pressureTimer;

        public static void TakeDamage()
        {
            healthBar.Flash();
        }

        public static void Update(float deltaTime, Character character)
        {
            if (drowningBar != null)
            {
                drowningBar.Update(deltaTime);
                if (character.Oxygen < 10.0f) drowningBar.Flash();
            }
            if (healthBar != null) healthBar.Update(deltaTime);

            pressureTimer += ((character.AnimController.CurrentHull == null) ?
                100.0f : character.AnimController.CurrentHull.LethalPressure)*deltaTime;
        }

        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (statusIcons==null)
            {
                statusIcons = new Sprite("Content/UI/statusIcons.png", Vector2.Zero);
            }

            DrawStatusIcons(spriteBatch, character);

            if (character.Inventory != null) character.Inventory.DrawOwn(spriteBatch);

            if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory!=null)
            {
                character.SelectedCharacter.Inventory.Draw(spriteBatch);

                //if (Vector2.Distance(selectedCharacter.SimPosition, SimPosition) > 2.0f) selectedCharacter = null;
            }

            if (character.ClosestCharacter != null && (character.ClosestCharacter.IsDead || character.ClosestCharacter.Stun > 0.0f))
            {
                Vector2 startPos = character.DrawPosition + (character.ClosestCharacter.DrawPosition - character.DrawPosition) * 0.7f;
                startPos = cam.WorldToScreen(startPos);

                Vector2 textPos = startPos;
                textPos -= new Vector2(GUI.Font.MeasureString(character.ClosestCharacter.Info.Name).X / 2, 20);

                GUI.DrawString(spriteBatch, textPos, character.ClosestCharacter.Info.Name, Color.Orange, Color.Black, 2);

                //spriteBatch.DrawString(GUI.Font, character.ClosestCharacter.Info.Name, textPos, Color.Black);
                //spriteBatch.DrawString(GUI.Font, character.ClosestCharacter.Info.Name, textPos + new Vector2(1, -1), Color.Orange);
            }
            else if (character.SelectedCharacter == null && character.ClosestItem != null && character.SelectedConstruction == null)
            {

                Vector2 startPos = character.DrawPosition + (character.ClosestItem.DrawPosition - character.DrawPosition) * 0.7f;
                startPos = cam.WorldToScreen(startPos);

                Vector2 textPos = startPos;
                textPos -= new Vector2(GUI.Font.MeasureString(character.ClosestItem.Name).X / 2, 20);
                //spriteBatch.DrawString(GUI.Font, character.ClosestItem.Prefab.Name, textPos, Color.Black);
                //GUI.DrawRectangle(spriteBatch, textPos-Vector2.One*2.0f, textSize+Vector2.One*4.0f, Color.Black * 0.7f, true);
                //spriteBatch.DrawString(GUI.Font, character.ClosestItem.Prefab.Name, textPos, Color.Orange);

                GUI.DrawString(spriteBatch, textPos, character.ClosestItem.Name, Color.Orange, Color.Black * 0.7f, 2);


                textPos.Y += 50.0f;
                foreach (ColoredText coloredText in character.ClosestItem.GetHUDTexts(character))
                {
                    textPos.X = startPos.X - GUI.Font.MeasureString(coloredText.Text).X / 2;

                    GUI.DrawString(spriteBatch, textPos, coloredText.Text, coloredText.Color, Color.Black*0.7f, 2);

                    //spriteBatch.DrawString(GUI.Font, coloredText.Text, textPos, Color.Black);
                    //GUI.DrawRectangle(spriteBatch, textPos - Vector2.One * 2.0f, textSize + Vector2.One * 4.0f, Color.Black * 0.7f, true);
                    //spriteBatch.DrawString(GUI.Font, coloredText.Text, textPos, coloredText.Color);

                    textPos.Y += 25;
                }
            }
        }

        private static void DrawStatusIcons(SpriteBatch spriteBatch, Character character)
        {
            if (drowningBar == null)
            {
                int width = 100, height = 20;

                drowningBar = new GUIProgressBar(new Rectangle(30, GameMain.GraphicsHeight - 200, width, height), Color.Blue, GUI.Style, 1.0f, Alignment.TopLeft);
                new GUIImage(new Rectangle(-27, -7, 20, 20), new Rectangle(17, 0, 20, 24), statusIcons, Alignment.TopLeft, drowningBar);

                healthBar = new GUIProgressBar(new Rectangle(30, GameMain.GraphicsHeight - 230, width, height), Color.Red, GUI.Style, 1.0f, Alignment.TopLeft);
                new GUIImage(new Rectangle(-26, -7, 20, 20), new Rectangle(0, 0, 13, 24), statusIcons, Alignment.TopLeft, healthBar);
            }

            drowningBar.BarSize = character.Oxygen / 100.0f;
            if (drowningBar.BarSize < 0.99f)
            {
                drowningBar.Draw(spriteBatch);
            }

            healthBar.BarSize = character.Health / character.MaxHealth;
            if (healthBar.BarSize < 1.0f)
            {
                healthBar.Draw(spriteBatch);
            }

            int bloodDropCount = (int)Math.Floor(character.Bleeding);
            bloodDropCount = MathHelper.Clamp(bloodDropCount, 0, 5);
            for (int i = 1; i < bloodDropCount; i++)
            {
                spriteBatch.Draw(statusIcons.Texture, new Vector2(5.0f + 20 * i, healthBar.Rect.Y - 20.0f), new Rectangle(39, 3, 15, 19), Color.White * 0.8f);
            }

            float pressureFactor = (character.AnimController.CurrentHull == null) ?
                100.0f : Math.Min(character.AnimController.CurrentHull.LethalPressure,100.0f);
            if (character.PressureProtection > 0.0f) pressureFactor = 0.0f;

            if (pressureFactor>0.0f)
            {
                float indicatorAlpha = ((float)Math.Sin(pressureTimer * 0.1f) + 1.0f) * 0.5f;

                indicatorAlpha = MathHelper.Clamp(indicatorAlpha, 0.1f, pressureFactor/100.0f);

                spriteBatch.Draw(statusIcons.Texture, new Vector2(10.0f, healthBar.Rect.Y - 60.0f), new Rectangle(0, 24, 24, 25), Color.White * indicatorAlpha);
            
            }
        }
    }
}
