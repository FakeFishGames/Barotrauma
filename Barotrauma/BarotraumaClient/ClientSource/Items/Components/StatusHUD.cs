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
        private static readonly LocalizedString[] BleedingTexts = 
        {
            TextManager.Get("MinorBleeding"),
            TextManager.Get("Bleeding"),
            TextManager.Get("HeavyBleeding"),
            TextManager.Get("CatastrophicBleeding")
        };

        private static readonly LocalizedString[] OxygenTexts = 
        {
            TextManager.Get("OxygenNormal"),
            TextManager.Get("OxygenReduced"),
            TextManager.Get("OxygenLow"),
            TextManager.Get("NotBreathing")
        };

        [Serialize(500.0f, IsPropertySaveable.No, description: "How close to a target the user must be to see their health data (in pixels).")]
        public float Range
        {
            get;
            private set;
        }

        [Serialize(50.0f, IsPropertySaveable.No, description: "The range within which the health info texts fades out.")]
        public float FadeOutRange
        {
            get;
            private set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool ThermalGoggles
        {
            get;
            private set;
        }

        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowDeadCharacters
        {
            get;
            private set;
        }

        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowTexts
        {
            get;
            private set;
        }

        [Serialize("72,119,72,120", IsPropertySaveable.No)]
        public Color OverlayColor
        {
            get;
            private set;
        }

        private readonly List<Character> visibleCharacters = new List<Character>();

        private const float UpdateInterval = 0.5f;
        private float updateTimer;

        private Character equipper;

        private bool isEquippable;

        private float thermalEffectState;

        public IEnumerable<Character> VisibleCharacters
        {
            get 
            {
                if (equipper == null || equipper.Removed) { return Enumerable.Empty<Character>(); }
                return visibleCharacters; 
            }
        }

        public override void OnItemLoaded()
        {
            isEquippable = item.GetComponent<Pickable>() != null;
            if (!isEquippable) { IsActive = true; }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            Entity refEntity = equipper;
            if (isEquippable)
            {
                if (equipper == null || equipper.Removed)
                {
                    IsActive = false;
                    return;
                }
            }
            else
            {
                refEntity = item;
            }

            thermalEffectState += deltaTime;
            thermalEffectState %= 10000.0f;

            if (updateTimer > 0.0f)
            {
                updateTimer -= deltaTime;
                return;
            }

            visibleCharacters.Clear();
            foreach (Character c in Character.CharacterList)
            {
                if (c == equipper || !c.Enabled || c.Removed) { continue; }
                if (!ShowDeadCharacters && c.IsDead) { continue; }

                float dist = Vector2.DistanceSquared(refEntity.WorldPosition, c.WorldPosition);
                if (dist < Range * Range)
                {
                    Vector2 diff = c.WorldPosition - refEntity.WorldPosition;
                    if (Submarine.CheckVisibility(refEntity.SimPosition, refEntity.SimPosition + ConvertUnits.ToSimUnits(diff)) == null)
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

            if (OverlayColor.A > 0)
            {
                GUIStyle.UIGlow.Draw(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    OverlayColor);
            }

            if (ShowTexts)
            {
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

            if (ThermalGoggles)
            {
                spriteBatch.End();
                GameMain.LightManager.SolidColorEffect.Parameters["color"].SetValue(Color.Red.ToVector4() * (0.3f + MathF.Sin(thermalEffectState) * 0.05f));
                GameMain.LightManager.SolidColorEffect.CurrentTechnique = GameMain.LightManager.SolidColorEffect.Techniques["SolidColorBlur"];
                GameMain.LightManager.SolidColorEffect.Parameters["blurDistance"].SetValue(0.01f + MathF.Sin(thermalEffectState) * 0.005f);
                GameMain.LightManager.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: Screen.Selected.Cam.Transform, effect: GameMain.LightManager.SolidColorEffect);

                Entity refEntity = equipper;
                if (!isEquippable || refEntity == null)
                {
                    refEntity = item;
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (c == character || !c.Enabled || c.Removed || c.Params.HideInThermalGoggles) { continue; }
                    if (!ShowDeadCharacters && c.IsDead) { continue; }

                    float dist = Vector2.DistanceSquared(refEntity.WorldPosition, c.WorldPosition);
                    if (dist > Range * Range) { continue; }

                    Sprite pingCircle = GUIStyle.UIThermalGlow.Value.Sprite;
                    foreach (Limb limb in c.AnimController.Limbs)
                    {
                        if (limb.Mass < 1.0f) { continue; }
                        float noise1 = PerlinNoise.GetPerlin((thermalEffectState + limb.Params.ID + c.ID) * 0.01f, (thermalEffectState + limb.Params.ID + c.ID) * 0.02f);
                        float noise2 = PerlinNoise.GetPerlin((thermalEffectState + limb.Params.ID + c.ID) * 0.01f, (thermalEffectState + limb.Params.ID + c.ID) * 0.008f);
                        Vector2 spriteScale = ConvertUnits.ToDisplayUnits(limb.body.GetSize()) / pingCircle.size * (noise1 * 0.5f + 2f);
                        Vector2 drawPos = new Vector2(limb.body.DrawPosition.X + (noise1 - 0.5f) * 100, -limb.body.DrawPosition.Y + (noise2 - 0.5f) * 100);
                        pingCircle.Draw(spriteBatch, drawPos, 0.0f, scale: Math.Max(spriteScale.X, spriteScale.Y));
                    }
                }

                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
            }
        }

        private void DrawCharacterInfo(SpriteBatch spriteBatch, Character target, float alpha = 1.0f)
        {
            Vector2 hudPos = GameMain.GameScreen.Cam.WorldToScreen(target.DrawPosition);
            hudPos += Vector2.UnitX * 50.0f;

            List<LocalizedString> texts = new List<LocalizedString>();
            List<Color> textColors = new List<Color>();
            texts.Add(target.Info == null ? target.DisplayName : target.Info.DisplayName);
            Color nameColor = GUIStyle.TextColorNormal;
            if (Character.Controlled != null && target.TeamID != Character.Controlled.TeamID)
            {
                nameColor = target.TeamID == CharacterTeamType.FriendlyNPC ? Color.SkyBlue : GUIStyle.Red;
            }
            textColors.Add(nameColor);
            
            if (target.IsDead)
            {
                texts.Add(TextManager.Get("Deceased"));
                textColors.Add(GUIStyle.Red);
                if (target.CauseOfDeath != null)
                {
                    texts.Add(
                        target.CauseOfDeath.Affliction?.CauseOfDeathDescription ??
                        TextManager.AddPunctuation(':', TextManager.Get("CauseOfDeath"), TextManager.Get("CauseOfDeath." + target.CauseOfDeath.Type.ToString())));
                    textColors.Add(GUIStyle.Red);
                }
            }
            else
            {
                if (!target.CustomInteractHUDText.IsNullOrEmpty() && target.AllowCustomInteract)
                {
                    texts.Add(target.CustomInteractHUDText);
                    textColors.Add(GUIStyle.Green);
                }
                if (!target.IsIncapacitated && target.IsPet)
                {
                    texts.Add(CharacterHUD.GetCachedHudText("PlayHint", InputType.Use));
                    textColors.Add(GUIStyle.Green);
                }
                if (target.CharacterHealth.UseHealthWindow && !target.DisableHealthWindow && equipper?.FocusedCharacter == target && equipper.CanInteractWith(target, 160f, false))
                {
                    texts.Add(CharacterHUD.GetCachedHudText("HealHint", InputType.Health));
                    textColors.Add(GUIStyle.Green);
                }
                if (target.CanBeDragged)
                {
                    texts.Add(CharacterHUD.GetCachedHudText("GrabHint", InputType.Grab));
                    textColors.Add(GUIStyle.Green);
                }

                if (target.IsUnconscious)
                {
                    texts.Add(TextManager.Get("Unconscious"));
                    textColors.Add(GUIStyle.Orange);
                }
                if (target.Stun > 0.01f)
                {
                    texts.Add(TextManager.Get("Stunned"));
                    textColors.Add(GUIStyle.Orange);
                }

                int oxygenTextIndex = MathHelper.Clamp((int)Math.Floor((1.0f - (target.Oxygen / 100.0f)) * OxygenTexts.Length), 0, OxygenTexts.Length - 1);
                texts.Add(OxygenTexts[oxygenTextIndex]);
                textColors.Add(Color.Lerp(GUIStyle.Red, GUIStyle.Green, target.Oxygen / 100.0f));

                if (target.Bleeding > 0.0f)
                {
                    int bleedingTextIndex = MathHelper.Clamp((int)Math.Floor(target.Bleeding / 100.0f * BleedingTexts.Length), 0, BleedingTexts.Length - 1);
                    texts.Add(BleedingTexts[bleedingTextIndex]);
                    textColors.Add(Color.Lerp(GUIStyle.Orange, GUIStyle.Red, target.Bleeding / 100.0f));
                }

                var allAfflictions = target.CharacterHealth.GetAllAfflictions();
                Dictionary<AfflictionPrefab, float> combinedAfflictionStrengths = new Dictionary<AfflictionPrefab, float>();
                foreach (Affliction affliction in allAfflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ShowInHealthScannerThreshold || affliction.Strength <= 0.0f) { continue; }
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
                    texts.Add(TextManager.AddPunctuation(':', affliction.Name, Math.Max((int)combinedAfflictionStrengths[affliction], 1).ToString() + " %"));
                    textColors.Add(Color.Lerp(GUIStyle.Orange, GUIStyle.Red, combinedAfflictionStrengths[affliction] / affliction.MaxStrength));
                }
            }

            GUI.DrawString(spriteBatch, hudPos, texts[0].Value, textColors[0] * alpha, Color.Black * 0.7f * alpha, 2, GUIStyle.SubHeadingFont, ForceUpperCase.No);
            hudPos.X += 5.0f;
            hudPos.Y += 24.0f * GameSettings.CurrentConfig.Graphics.TextScale;

            hudPos.X = (int)hudPos.X;
            hudPos.Y = (int)hudPos.Y;

            for (int i = 1; i < texts.Count; i++)
            {
                GUI.DrawString(spriteBatch, hudPos, texts[i], textColors[i] * alpha, Color.Black * 0.7f * alpha, 2, GUIStyle.SmallFont);
                hudPos.Y += (int)(18.0f * GameSettings.CurrentConfig.Graphics.TextScale);
            }
        }
    }
}
