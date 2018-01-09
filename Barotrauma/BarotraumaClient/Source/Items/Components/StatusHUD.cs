using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class StatusHUD : ItemComponent
    {
        private static readonly string[] BleedingTexts = { "Minor bleeding", "Bleeding", "Bleeding heavily", "Catastrophic Bleeding" };

        private static readonly string[] HealthTexts = { "No visible injuries", "Minor injuries", "Injured", "Major injuries", "Critically injured" };

        private static readonly string[] OxygenTexts = { "Oxygen level normal", "Gasping for air", "Signs of oxygen deprivation", "Not breathing" };

        [Serialize(500.0f, false)]
        public float Range
        {
            get;
            private set;
        }

        [Serialize(50.0f, false)]
        public float FadeOutRange
        {
            get;
            private set;
        }

        private List<Character> visibleCharacters = new List<Character>();

        private const float UpdateInterval = 0.5f;
        private float updateTimer;

        private Character equipper;

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (equipper == null)
            {
                IsActive = false;
                return;
            }
            
            if (updateTimer > 0.0f)
            {
                updateTimer -= deltaTime;
                return;
            }

            visibleCharacters.Clear();
            foreach (Character c in Character.CharacterList)
            {
                if (c == equipper) continue;

                float dist = Vector2.DistanceSquared(equipper.WorldPosition, c.WorldPosition);
                if (dist < Range * Range)
                {
                    Vector2 diff = c.WorldPosition - equipper.WorldPosition;
                    if (Submarine.CheckVisibility(equipper.SimPosition, equipper.SimPosition + ConvertUnits.ToSimUnits(diff)) == null)
                    {
                        visibleCharacters.Add(c);
                    }
                }
            }

            updateTimer = UpdateInterval;
        }
        
        public override void Equip(Character character)
        {
            updateTimer = 0.0f;
            equipper = character;
            IsActive = true;
        }

        public override void Unequip(Character character)
        {
            equipper = null;
            IsActive = false;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character == null) return;
            
            GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                Color.Green * 0.1f, true);

            Character closestCharacter = null;
            float closestDist = float.PositiveInfinity;

            foreach (Character c in visibleCharacters)
            {
                if (c == character) continue;

                float dist = Vector2.DistanceSquared(GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), c.WorldPosition);
                if (dist < closestDist)
                {
                    closestCharacter = c;
                    closestDist = dist;
                }              
            }

            if (closestCharacter != null)
            {
                float dist = Vector2.Distance(GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), closestCharacter.WorldPosition);
                DrawCharacterInfo(spriteBatch, closestCharacter, 1.0f - MathHelper.Max((dist - (Range - FadeOutRange)) / FadeOutRange, 0.0f));
            }
        }

        private void DrawCharacterInfo(SpriteBatch spriteBatch, Character target, float alpha = 1.0f)
        {
            Vector2 hudPos = GameMain.GameScreen.Cam.WorldToScreen(target.WorldPosition);
            hudPos += Vector2.UnitX * 50.0f;

            List<string> texts = new List<string>();

            if (target.Info != null)
            {
                texts.Add(target.Name);
            }

            if (target.IsDead)
            {
                texts.Add("Deceased");
                texts.Add("Cause of Death: " + target.CauseOfDeath.ToString());
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
                GUI.DrawString(spriteBatch, hudPos, text, Color.LightGreen * alpha, Color.Black * 0.7f * alpha, 2);
                hudPos.Y += 24.0f;
            }
        }
    }
}
