using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class StatusHUD : ItemComponent
    {
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character == null) return;
            
            GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                Color.Green * 0.1f, true);

            if (character.FocusedCharacter == null) return;

            var target = character.FocusedCharacter;

            Vector2 hudPos = GameMain.GameScreen.Cam.WorldToScreen(target.WorldPosition);
            hudPos += Vector2.UnitX * 50.0f;

            List<string> texts = new List<string>();

            texts.Add(target.Name);

            if (target.IsDead)
            {
                texts.Add("Deceased");
            }
            else
            {
                if (target.IsUnconscious) texts.Add("Unconscious");
                if (target.Stun > 0.01f) texts.Add("Stunned");

                int healthTextIndex = target.Health > 95.0f ? 0 :
                    MathHelper.Clamp((int)Math.Ceiling((1.0f - (target.Health / 200.0f + 0.5f)) * HealthTexts.Length), 0, HealthTexts.Length - 1);

                texts.Add(HealthTexts[healthTextIndex]);

                int oxygenTextIndex = MathHelper.Clamp((int)Math.Floor((1.0f - (target.Oxygen / 200.0f + 0.5f)) * OxygenTexts.Length), 0, OxygenTexts.Length - 1);
                texts.Add(OxygenTexts[oxygenTextIndex]);

                if (target.Bleeding > 0.0f)
                {
                    int bleedingTextIndex = MathHelper.Clamp((int)Math.Floor(target.Bleeding / 4.0f) * BleedingTexts.Length, 0, BleedingTexts.Length - 1);
                    texts.Add(BleedingTexts[bleedingTextIndex]);
                }

                if (target.huskInfection != null)
                {
                    if (target.huskInfection.State != HuskInfection.InfectionState.Transition)
                    {
                        texts.Add("Velonaceps calyx infection");
                    }
                    else if (target.huskInfection.State != HuskInfection.InfectionState.Active)
                    {
                        texts.Add("Advanced Velonaceps calyx infection");
                    }
                }
            }

            foreach (string text in texts)
            {
                GUI.DrawString(spriteBatch, hudPos, text, Color.LightGreen, Color.Black * 0.7f, 2);
                hudPos.Y += 24.0f;
            }
        }
    }
}
