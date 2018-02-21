using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class StatusHUD : ItemComponent
    {
        private static readonly string[] BleedingTexts = 
        {
            TextManager.Get("MinorBleeding"),
            TextManager.Get("Bleeding"),
            TextManager.Get("HeavyBleeding"),
            TextManager.Get("CatastrophicBleeding")
        };

        private static readonly string[] HealthTexts = 
        {
            TextManager.Get("NoInjuries"),
            TextManager.Get("MinorInjuries"),
            TextManager.Get("Injuries"),
            TextManager.Get("MajorInjuries"),
            TextManager.Get("CriticalInjuries")
        };

        private static readonly string[] OxygenTexts = 
        {
            TextManager.Get("OxygenNormal"),
            TextManager.Get("OxygenReduced"),
            TextManager.Get("OxygenLow"),
            TextManager.Get("NotBreathing")
        };

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

            //TODO: reimplement
            /*if (target.IsDead)
            {
                texts.Add(TextManager.Get("Deceased"));
                texts.Add(TextManager.Get("CauseOfDeath") + ": " + TextManager.Get("CauseOfDeath." + target.CauseOfDeath.ToString()));
            }
            else
            {
                if (target.IsUnconscious) texts.Add(TextManager.Get("Unconscious"));
                if (target.Stun > 0.01f) texts.Add(TextManager.Get("Stunned"));

                int healthTextIndex = target.Vitality > 0.9f ? 0 :
                    MathHelper.Clamp((int)Math.Ceiling((1.0f - (target.Health + 0.5f)) * HealthTexts.Length), 0, HealthTexts.Length - 1);

                texts.Add(HealthTexts[healthTextIndex]);

                int oxygenTextIndex = MathHelper.Clamp((int)Math.Floor((1.0f - (target.Oxygen / 100.0f)) * OxygenTexts.Length), 0, OxygenTexts.Length - 1);
                texts.Add(OxygenTexts[oxygenTextIndex]);

                if (target.Bleeding > 0.0f)
                {
                    int bleedingTextIndex = MathHelper.Clamp((int)Math.Floor(target.Bleeding / 4.0f) * BleedingTexts.Length, 0, BleedingTexts.Length - 1);
                    texts.Add(BleedingTexts[bleedingTextIndex]);
                }

                if (target.huskInfection != null)
                {
                    if (target.huskInfection.State == HuskInfection.InfectionState.Transition)
                    {
                        texts.Add(TextManager.Get("HuskInfectionTransition"));
                    }
                    else if (target.huskInfection.State == HuskInfection.InfectionState.Active)
                    {
                        texts.Add(TextManager.Get("HuskInfectionActive"));
                    }
                }
            }*/

            foreach (string text in texts)
            {
                GUI.DrawString(spriteBatch, hudPos, text, Color.LightGreen * alpha, Color.Black * 0.7f * alpha, 2);
                hudPos.Y += 24.0f;
            }
        }
    }
}
