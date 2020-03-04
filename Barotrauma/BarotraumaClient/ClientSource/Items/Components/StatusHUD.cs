using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

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

        [Serialize(500.0f, false, description: "How close to a target the user must be to see their health data (in pixels).")]
        public float Range
        {
            get;
            private set;
        }

        [Serialize(50.0f, false, description: "The range within which the health info texts fades out.")]
        public float FadeOutRange
        {
            get;
            private set;
        }

        private readonly List<Character> visibleCharacters = new List<Character>();

        private const float UpdateInterval = 0.5f;
        private float updateTimer;

        private Character equipper;

        public IEnumerable<Character> VisibleCharacters
        {
            get 
            {
                if (equipper == null || equipper.Removed) { return Enumerable.Empty<Character>(); }
                return visibleCharacters; 
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (equipper == null || equipper.Removed)
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
                if (c == equipper || !c.Enabled || c.Removed) { continue; }

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
            if (character == null) { return; }

            GUI.UIGlow.Draw(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                Color.LightGreen * 0.5f);

            Character closestCharacter = null;
            float closestDist = float.PositiveInfinity;
            foreach (Character c in visibleCharacters)
            {
                if (c == character || !c.Enabled || c.Removed) { continue; }

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
            Vector2 hudPos = GameMain.GameScreen.Cam.WorldToScreen(target.DrawPosition);
            hudPos += Vector2.UnitX * 50.0f;

            List<string> texts = new List<string>();
            List<Color> textColors = new List<Color>();

            if (target.Info != null)
            {
                texts.Add(target.Name);
                textColors.Add(GUI.Style.TextColor);
            }
            
            if (target.IsDead)
            {
                texts.Add(TextManager.Get("Deceased"));
                textColors.Add(GUI.Style.Red);
                texts.Add(
                    target.CauseOfDeath.Affliction?.CauseOfDeathDescription ??
                    TextManager.AddPunctuation(':', TextManager.Get("CauseOfDeath"), TextManager.Get("CauseOfDeath." + target.CauseOfDeath.Type.ToString())));
                textColors.Add(GUI.Style.Red);
            }
            else
            {
                if (target.IsUnconscious)
                {
                    texts.Add(TextManager.Get("Unconscious"));
                    textColors.Add(GUI.Style.Orange);
                }
                if (target.Stun > 0.01f)
                {
                    texts.Add(TextManager.Get("Stunned"));
                    textColors.Add(GUI.Style.Orange);
                }
                
                int oxygenTextIndex = MathHelper.Clamp((int)Math.Floor((1.0f - (target.Oxygen / 100.0f)) * OxygenTexts.Length), 0, OxygenTexts.Length - 1);
                texts.Add(OxygenTexts[oxygenTextIndex]);
                textColors.Add(Color.Lerp(GUI.Style.Red, GUI.Style.Green, target.Oxygen / 100.0f));

                if (target.Bleeding > 0.0f)
                {
                    int bleedingTextIndex = MathHelper.Clamp((int)Math.Floor(target.Bleeding / 100.0f) * BleedingTexts.Length, 0, BleedingTexts.Length - 1);
                    texts.Add(BleedingTexts[bleedingTextIndex]);
                    textColors.Add(Color.Lerp(GUI.Style.Orange, GUI.Style.Red, target.Bleeding / 100.0f));
                }

                var allAfflictions = target.CharacterHealth.GetAllAfflictions();
                Dictionary<AfflictionPrefab, float> combinedAfflictionStrengths = new Dictionary<AfflictionPrefab, float>();
                foreach (Affliction affliction in allAfflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ShowInHealthScannerThreshold || affliction.Strength <= 0.0f) continue;
                    if (combinedAfflictionStrengths.ContainsKey(affliction.Prefab))
                    {
                        combinedAfflictionStrengths[affliction.Prefab] += affliction.Strength;
                    }
                    else
                    {
                        combinedAfflictionStrengths[affliction.Prefab] = affliction.Strength;
                    }
                }

                foreach (AfflictionPrefab affliction in combinedAfflictionStrengths.Keys)
                {
                    texts.Add(TextManager.AddPunctuation(':', affliction.Name, ((int)combinedAfflictionStrengths[affliction]).ToString() + " %"));
                    textColors.Add(Color.Lerp(GUI.Style.Orange, GUI.Style.Red, combinedAfflictionStrengths[affliction] / affliction.MaxStrength));
                }
            }

            GUI.DrawString(spriteBatch, hudPos, texts[0], textColors[0] * alpha, Color.Black * 0.7f * alpha, 2, GUI.SubHeadingFont);
            hudPos.X += 5.0f;
            hudPos.Y += 24.0f;

            hudPos.X = (int)hudPos.X;
            hudPos.Y = (int)hudPos.Y;

            for (int i = 1; i < texts.Count; i++)
            {
                GUI.DrawString(spriteBatch, hudPos, texts[i], textColors[i] * alpha, Color.Black * 0.7f * alpha, 2, GUI.SmallFont);
                hudPos.Y += 18.0f;
            }
        }
    }
}
