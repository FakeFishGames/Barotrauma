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

        [Serialize(false, IsPropertySaveable.No)]
        public bool DebugWiring
        {
            get;
            private set;
        }
        
        [Serialize(false, IsPropertySaveable.No)]
        public bool ShowPlantInfo
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

        [Serialize(true, IsPropertySaveable.No, description: "Shows the detailed character health state info.")]
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

            if (equipper != null && equipper == Character.Controlled && DebugWiring)
            {
                ConnectionPanel.DebugWiringEnabledUntil = Timing.TotalTimeUnpaused + 0.5;
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
                if (c.InDetectable) { continue; }

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

        public override void Drop(Character dropper, bool setTransform = true)
        {
            Unequip(dropper);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character == null) { return; }

            base.DrawHUD(spriteBatch, character);

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
                    DrawCharacterHealthStateInfo(spriteBatch, closestCharacter, 1.0f - MathHelper.Max((dist - (Range - FadeOutRange)) / FadeOutRange, 0.0f));
                }
            }

            if (ThermalGoggles)
            {
                Entity refEntity = equipper;
                if (!isEquippable || refEntity == null)
                {
                    refEntity = item;
                }
                DrawThermalOverlay(spriteBatch, refEntity, character, OverlayColor, Range, thermalEffectState, ShowDeadCharacters);
            }
        }

        public static void DrawThermalOverlay(SpriteBatch spriteBatch, Entity refEntity, Character user, Color overlayColor, float range, float effectState, bool showDeadCharacters)
        {
            spriteBatch.End();
            float colorIntensityBase = 0.5f; //Multiplies the overlay color by this amount, the higher the value, the more bright/vibrant the color.
            float colorIntensityVariance = 0.05f; //The variance of the pulse effect affecting the color's brightness/vibrance 
            GameMain.LightManager.SolidColorEffect.Parameters["color"].SetValue(overlayColor.ToVector4() * (colorIntensityBase + MathF.Sin(effectState) * colorIntensityVariance));
            GameMain.LightManager.SolidColorEffect.CurrentTechnique = GameMain.LightManager.SolidColorEffect.Techniques["SolidColorBlur"];
            GameMain.LightManager.SolidColorEffect.Parameters["blurDistance"].SetValue(0.01f + MathF.Sin(effectState) * 0.005f);
            GameMain.LightManager.SolidColorEffect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: Screen.Selected.Cam.Transform, effect: GameMain.LightManager.SolidColorEffect);

            foreach (Character c in Character.CharacterList)
            {
                if (c == user || !c.Enabled || c.Removed || c.Params.HideInThermalGoggles) { continue; }
                if (!showDeadCharacters && c.IsDead) { continue; }

                float dist = Vector2.DistanceSquared(refEntity.WorldPosition, c.WorldPosition);
                if (dist > range * range) { continue; }

                Sprite pingCircle = GUIStyle.UIThermalGlow.Value.Sprite;
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.Mass < 0.5f && limb != c.AnimController.MainLimb) { continue; }
                    float noise1 = PerlinNoise.GetPerlin((effectState + limb.Params.ID + c.ID) * 0.01f, (effectState + limb.Params.ID + c.ID) * 0.02f);
                    float noise2 = PerlinNoise.GetPerlin((effectState + limb.Params.ID + c.ID) * 0.01f, (effectState + limb.Params.ID + c.ID) * 0.008f);
                    Vector2 spriteScale = ConvertUnits.ToDisplayUnits(limb.body.GetSize()) / pingCircle.size * (noise1 * 0.5f + 2f);
                    Vector2 drawPos = new Vector2(limb.body.DrawPosition.X + (noise1 - 0.5f) * 100, -limb.body.DrawPosition.Y + (noise2 - 0.5f) * 100);
                    pingCircle.Draw(spriteBatch, drawPos, 0.0f, scale: Math.Max(spriteScale.X, spriteScale.Y));
                }
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
        }

        private void DrawCharacterHealthStateInfo(SpriteBatch spriteBatch, Character target, float alpha = 1.0f)
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
                if (target.ShouldShowCustomInteractText)
                {
                    texts.Add(target.CustomInteractHUDText);
                    textColors.Add(GUIStyle.Green);
                }
                if (equipper?.FocusedCharacter == target)
                {
                    if (!target.IsIncapacitated && target.IsPet &&
                        target.AIController is EnemyAIController enemyAI && enemyAI.PetBehavior.CanPlayWith(Character.Controlled))
                    {
                        texts.Add(CharacterHUD.GetCachedHudText("PlayHint", InputType.Use));
                        textColors.Add(GUIStyle.Green);
                    }
                    if (target.CanBeHealedBy(equipper, checkFriendlyTeam: false))
                    {
                        texts.Add(CharacterHUD.GetCachedHudText("HealHint", InputType.Health));
                        textColors.Add(GUIStyle.Green);
                    }
                    if (target.CanBeDraggedBy(Character.Controlled))
                    {
                        texts.Add(CharacterHUD.GetCachedHudText("GrabHint", InputType.Grab));
                        textColors.Add(GUIStyle.Green);
                    }
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

                if (target.NeedsOxygen)
                {
                    int oxygenTextIndex = MathHelper.Clamp((int)Math.Floor((1.0f - (target.Oxygen / 100.0f)) * OxygenTexts.Length), 0, OxygenTexts.Length - 1);
                    texts.Add(OxygenTexts[oxygenTextIndex]);
                    textColors.Add(Color.Lerp(GUIStyle.Red, GUIStyle.Green, target.Oxygen / 100.0f));
                }

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
                    if (affliction.Strength <= 0f) { continue; }
                    if (affliction.Strength < affliction.Prefab.ShowInHealthScannerThreshold)
                    {
                        if (target.IsHuman || target.IsOnPlayerTeam || (affliction.Prefab.AfflictionType != AfflictionPrefab.PoisonType && affliction.Prefab.AfflictionType != AfflictionPrefab.ParalysisType))
                        {
                            // Always show the poisons on monsters, because poisoning bigger monsters require multiple doses.
                            // The solution is hacky, but didn't want to introduce an extra property for this.
                            // We also want to have a relatively high thershold for showing the poisons on the scanner on humans, so that it's not instantly clear that a target is poisoned and especially not which poison was used.
                            // Paralysis is treated like a poison but isn't technically a poison, so that we can have multiple afflictions that still are treated the same.
                            continue;
                        }
                    }
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
            hudPos.X += 5.0f * GUI.Scale;
            hudPos.Y += GUIStyle.SubHeadingFont.MeasureString(texts[0].Value).Y;

            hudPos.X = (int)hudPos.X;
            hudPos.Y = (int)hudPos.Y;

            for (int i = 1; i < texts.Count; i++)
            {
                GUI.DrawString(spriteBatch, hudPos, texts[i], textColors[i] * alpha, Color.Black * 0.7f * alpha, 2, GUIStyle.SmallFont);
                hudPos.Y += (int)(GUIStyle.SubHeadingFont.MeasureString(texts[i].Value).Y);
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (ShowPlantInfo)
            {
                // If a panel already exists and the mouse is over it, keep showing the panel regardless of what else is under the mouse, to prevent flickering issues
                if (currentPlanterInfoPanel != null &&
                    (GUI.IsMouseOn(currentPlanterInfoPanel)) &&
                    lastFocusedPlanter != null)
                {
                    CreatePlanterInfo(lastFocusedPlanter);
                    return;
                }
                // Otherwise, if the mouse is over a planter just open a new panel
                else if ((character?.FocusedItem?.GetComponent<Planter>() ?? character?.SelectedItem?.GetComponent<Planter>())
                         is { HasPower: true } hoveredPlanter) // unpowered planters can't transmit data to the goggles
                {
                    CreatePlanterInfo(hoveredPlanter);
                    lastFocusedPlanter = hoveredPlanter;
                    return;
                }
            }
            currentPlanterInfoPanel = null;
            lastFocusedPlanter = null;
        }

        private GUIComponent currentPlanterInfoPanel;
        private Planter lastFocusedPlanter;
        private int lastPlantCount = -1; // Track plant count for super simple cache invalidation when plants get planted/uprooted
        private Dictionary<GUIComponent, Func<bool>> elementVisibilityChecks = new Dictionary<GUIComponent, Func<bool>>();

        public override void AddToGUIUpdateList(int order = 0)
        {
            base.AddToGUIUpdateList(order);

            if (currentPlanterInfoPanel != null && Character.Controlled?.SelectedItem == null)
            {
                currentPlanterInfoPanel.AddToGUIUpdateList();

                //check if any of the situation-specific elements (e.g. amount of damage currently taken from overwatering) should be enabled/disabled
                bool elementVisibilityChanged = false;
                foreach ((GUIComponent element, Func<bool> elementVisibilityCheck) in elementVisibilityChecks)
                {
                    bool shouldBeVisible = elementVisibilityCheck();
                    if (shouldBeVisible != element.Visible)
                    {
                        element.Visible = shouldBeVisible;
                        element.IgnoreLayoutGroups = !element.Visible;
                        elementVisibilityChanged = true;
                    }
                }
                if (elementVisibilityChanged)
                {
                    RefreshPlanterInfoPanelSize(currentPlanterInfoPanel);
                }
            }
        }

        private void CreatePlanterInfo(Planter planter)
        {
            int currentPlantCount = planter.GrowableSeeds.Count(s => s != null);
            
            if (currentPlanterInfoPanel == null || // no panel yet
                currentPlanterInfoPanel?.UserData != planter || // panel isn't for this planter
                currentPlantCount != lastPlantCount) // planter's contents have changed
            {
                currentPlanterInfoPanel = CreatePlanterInfoPanel(planter);
                lastPlantCount = currentPlantCount;
            }
            else
            {
                Vector2 screenPos = Screen.Selected.Cam.WorldToScreen(planter.Item.DrawPosition);
                if (screenPos.X < GameMain.GraphicsWidth / 2)
                {
                    screenPos = new Vector2(GameMain.GraphicsWidth * 0.05f, GameMain.GraphicsHeight * 0.5f);
                    currentPlanterInfoPanel.RectTransform.Pivot = Pivot.CenterLeft;
                }
                else
                {
                    screenPos = new Vector2(GameMain.GraphicsWidth * 0.95f, GameMain.GraphicsHeight * 0.5f);
                    currentPlanterInfoPanel.RectTransform.Pivot = Pivot.CenterRight;
                }
                currentPlanterInfoPanel.RectTransform.AbsoluteOffset = screenPos.ToPoint();
            }
        }

        private GUIComponent CreatePlanterInfoPanel(Planter planter)
        {
            elementVisibilityChecks.Clear();

            Growable growable = planter.GrowableSeeds.FirstOrDefault();

            GUIComponent panel = new GUIFrame(new RectTransform(new Point(GUI.IntScale(growable == null ? 400 : 700), 100), GUI.Canvas, anchor: Anchor.TopLeft, pivot: Pivot.Center), style: "GUIFrameListBox")
            {
                UserData = planter
            };

            int elementHeight = GUI.IntScale(20);
            int spacing = GUI.IntScale(15);

            var horizontalLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), panel.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = growable == null ? 0 : spacing * 2,
                CanBeFocused = true
            };
            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), horizontalLayout.RectTransform), isHorizontal: false)
            {
                AbsoluteSpacing = spacing,
            };
            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(growable == null ? 0.0f : 0.5f, 1.0f), horizontalLayout.RectTransform), isHorizontal: false)
            {
                AbsoluteSpacing = spacing,
            };

            CreateMeter("planter.info.water",
                () => $"{Math.Round(100f * planter.NormalizedWaterLevel, 1)}%",
                () => planter.NormalizedWaterLevel,
                elementHeight,
                leftColumn,
                () => planter.GetWaterMeterColor(growable),
                minThreshold: growable?.TooDryThreshold,
                maxThreshold: growable?.OverwaterThreshold);

            CreateMeter("planter.info.fertilizer",
                () => $"{Math.Round(100f * planter.NormalizedFertilizerLevel, 1)}%",
                () => planter.NormalizedFertilizerLevel,
                elementHeight,
                leftColumn,
                () => planter.GetFertilizerMeterColor(growable),
                minThreshold: growable?.UnderfertilizedThreshold,
                maxThreshold: growable?.OverfertilizedThreshold);

            CreateMeter("planter.info.salinity",
                () => $"{Math.Round(planter.Salinity, 1)}%",
                () => planter.Salinity / Planter.SeawaterSalinity,
                elementHeight,
                leftColumn,
                () => Color.LightSeaGreen);

            if (planter.Item.CurrentHull != null)
            {
                CreateMeter("planter.info.light",
                    () => $"{Math.Round(planter.PlantLightAmount, 1)}%",
                    () => planter.PlantLightAmount / 100.0f,
                    elementHeight,
                    leftColumn,
                    () => growable != null && (planter.PlantLightAmount < growable.NotEnoughLightThreshold || planter.PlantLightAmount > growable.TooMuchLightThreshold) ? GUIStyle.Red : Color.LightYellow,
                    minThreshold: growable?.NotEnoughLightThreshold / 100.0f,
                    maxThreshold: growable?.TooMuchLightThreshold / 100.0f);
            }

            // If the planter has a growable plant, add its info
            if (growable != null)
            {
                Func<Color> healthColor = () => ToolBox.GradientLerp(growable.Health / growable.MaxHealth, Color.DarkRed, GUIStyle.Red, GUIStyle.Orange, GUIStyle.Yellow, GUIStyle.Green);
                CreateMeter("planter.info.health",
                    () => $"{Math.Round(100f * (growable.Health / growable.MaxHealth), 1)}%",
                    () => growable.Health / growable.MaxHealth,
                    elementHeight,
                    leftColumn,
                    healthColor,
                    centerTextGetter: () => $"{Math.Round(growable.Health, 1)} / {Math.Round(growable.MaxHealth, 1)}",
                    textColorGetter: healthColor);

                CreateLabeledValue("planter.info.status",
                    () => TextManager.Get(growable.Decayed ? "planter.info.status.decayed" :
                            !growable.HasOptimalConditions(planter) ? "planter.info.status.notgrowing" :
                                 growable.FullyGrown ? "planter.info.status.fullygrown" : "planter.info.status.growing"),
                    elementHeight, leftColumn);

                CreateLabel(growable.GrownPlantName ?? "Unknown",
                    elementHeight, rightColumn, GUIStyle.Green, font: GUIStyle.Font);

                List<(LocalizedString text, float value, bool isPositive, Func<bool> shouldBeActive)> effectRows = [];

                float salinityEffect = planter.Salinity > 0 ? MathF.Round(growable.CurrentSalinityEffect(planter), 2) : 0;
                if (!MathUtils.NearlyEqual(salinityEffect, 0.0f))
                {
                    effectRows.Add(("planter.info.salinitybonus", salinityEffect, isPositive: true, () => salinityEffect > 0));                 
                    effectRows.Add(("planter.info.salinitydamage", salinityEffect, isPositive: false, () => salinityEffect < 0));
                }
                effectRows.Add(($"planter.info.underfertilized", -growable.UnderfertilizedDamage, isPositive: false, 
                    () => planter.NormalizedFertilizerLevel < growable.UnderfertilizedThreshold && !MathUtils.NearlyEqual(growable.UnderfertilizedDamage, 0.0f)));

                effectRows.Add(($"planter.info.overfertilized", -growable.OverfertilizedDamage, isPositive: false,
                    () => planter.NormalizedFertilizerLevel > growable.OverfertilizedThreshold && !MathUtils.NearlyEqual(growable.OverfertilizedDamage, 0.0f)));
                
                effectRows.Add(("planter.info.toodry", -growable.DrynessDamage, isPositive: false,
                    () => planter.NormalizedWaterLevel < growable.TooDryThreshold && !MathUtils.NearlyEqual(growable.DrynessDamage, 0.0f)));
          
                effectRows.Add(($"planter.info.overwatered", -growable.OverwaterDamage, isPositive: false,
                    () => planter.NormalizedWaterLevel > growable.OverwaterThreshold && !MathUtils.NearlyEqual(growable.OverwaterDamage, 0.0f)));

                effectRows.Add(("planter.info.notenoughlight", -growable.NotEnoughLightDamage, isPositive: false,
                    () => planter.PlantLightAmount < growable.NotEnoughLightThreshold && !MathUtils.NearlyEqual(growable.NotEnoughLightDamage, 0.0f)));

                effectRows.Add(($"planter.info.toomuchlight", -growable.TooMuchLightDamage, isPositive: false,
                    () => planter.PlantLightAmount > growable.TooMuchLightThreshold && !MathUtils.NearlyEqual(growable.TooMuchLightDamage, 0.0f)));
             
                effectRows.Add(($"planter.info.optimal", growable.OptimalHealthRecovery, isPositive: true,
                    () => Growable.HasOptimalConditions(growable, planter)));

                var totalHealthDisplay = CreateLabeledValue("planter.info.status",
                    () => TextManager.GetWithVariable("planter.info.healthchange", "[amount]", Math.Round(effectRows.Where(e => e.shouldBeActive()).Sum(e => e.value), 2).ToString()),
                    elementHeight,
                    rightColumn,
                    labelGetter: () => 
                    {
                        float currentTotal = effectRows.Where(e => e.shouldBeActive()).Sum(e => e.value);
                        return TextManager.Get(currentTotal >= 0 ? "planter.info.gaininghealth" : "planter.info.losinghealth");
                    },
                    textColor: GUIStyle.TextColorBright);
                
                // Update background color dynamically, in case thresholds are crossed while the panel is open
                var backgroundFrame = new GUIFrame(new RectTransform(new Vector2(1.05f, 1.55f), totalHealthDisplay.RectTransform, Anchor.Center), style: null)
                {
                    IgnoreLayoutGroups = true,
                    OnAddedToGUIUpdateList = (component) =>
                    {
                        float currentTotal = effectRows.Where(e => e.shouldBeActive()).Sum(e => e.value);
                        component.Color = (currentTotal >= 0 ? GUIStyle.Green : GUIStyle.Red) * 0.7f;
                    }
                };
                           
                foreach ((LocalizedString text, float value, bool isPositive, Func<bool> shouldBeActive) in effectRows)
                {
                    var element = CreateLabeledValue("   - "+ TextManager.Get(text.Value).Fallback(text),
                        () => TextManager.GetWithVariable("planter.info.healthchange", "[amount]", Math.Round(value, 2).ToString()),
                        elementHeight,
                        rightColumn,
                        textColor: isPositive ? GUIStyle.Green : GUIStyle.Red,
                        labelColor: isPositive ? GUIStyle.Green * 0.5f : GUIStyle.Red * 0.5f);
                    if (!shouldBeActive())
                    {
                        element.Visible = false;
                        element.IgnoreLayoutGroups = true;
                    }
                    elementVisibilityChecks.Add(element, shouldBeActive);
                }

                // Production info
                CreateLabel("planter.info.production", elementHeight, rightColumn, font: GUIStyle.Font);

                CreateLabeledValue("planter.info.oxygengenerated",
                    () => Math.Round(growable.Vines.Count * growable.OxygenPerVine, 2).ToString(),
                    elementHeight, rightColumn);

                CreateLabeledValue("planter.info.produceditems",
                    () => string.Empty,
                    elementHeight, rightColumn);

                foreach (var producedItem in growable.ProducedItems)
                {
                    CreateLabeledValue("    - " + (producedItem.Prefab?.Name ?? "Unknown"),
                        () => string.Empty,
                    elementHeight, rightColumn);
                }

                CreateLabeledValue("planter.info.productionrate",
                    () => 
                    TextManager.GetWithVariable(
                        "planter.info.productionrate.value", 
                        "[amount]", 
                        growable.ProducedItems.Any() ?
                            //convert product spawn chance per product interval (in seconds) to products per hour
                            $"{Math.Round(growable.ProductSpawnChance * growable.GetProductSpawnChanceMultiplier() / growable.ProductIntervalModified * 60 * 60, 2)}" :
                            "0"),
                    elementHeight, rightColumn);
            }

            RefreshPlanterInfoPanelSize(panel);

            return panel;
        }

        private static void RefreshPlanterInfoPanelSize(GUIComponent panel)
        {
            var horizontalLayout = panel.GetChild<GUILayoutGroup>();
            var leftColumn = horizontalLayout.GetAllChildren<GUILayoutGroup>().First();
            var rightColumn = horizontalLayout.GetAllChildren<GUILayoutGroup>().Last();

            leftColumn.InheritTotalChildrenMinHeight(additionalHeight: leftColumn.AbsoluteSpacing * leftColumn.CountChildren);
            rightColumn.InheritTotalChildrenMinHeight(additionalHeight: rightColumn.AbsoluteSpacing * rightColumn.CountChildren);

            horizontalLayout.RectTransform.MinSize = new Point(0, Math.Max(leftColumn.Rect.Height, rightColumn.Rect.Height));
            panel.RectTransform.MinSize = new Point(horizontalLayout.Rect.Width, (int)(horizontalLayout.Rect.Height / horizontalLayout.RectTransform.RelativeSize.Y));
        }

        private static void CreateMeter(string textTag, Func<LocalizedString> valueGetter, Func<float> progressGetter, int height, GUIComponent parent, Func<Color> colorGetter, float? minThreshold = null, float? maxThreshold = null, Func<LocalizedString> centerTextGetter = null, Func<Color> textColorGetter = null)
        {
            CreateMeter(TextManager.Get(textTag), valueGetter, progressGetter, height, parent, colorGetter, minThreshold, maxThreshold, centerTextGetter, textColorGetter);
        }

        private static void CreateMeter(LocalizedString label, Func<LocalizedString> valueGetter, Func<float> progressGetter, int height, GUIComponent parent, Func<Color> colorGetter, float? minThreshold = null, float? maxThreshold = null, Func<LocalizedString> centerTextGetter = null, Func<Color> textColorGetter = null)
        {
            var container = new GUILayoutGroup(new RectTransform(new Point(parent.Rect.Width, height * 2), parent.RectTransform), isHorizontal: false);

            var textContainer = new GUILayoutGroup(new RectTransform(new Point(container.Rect.Width, height), container.RectTransform), isHorizontal: true);
            float columnWidth = centerTextGetter != null ? 0.33f : 0.5f;
            new GUITextBlock(new RectTransform(new Vector2(columnWidth, 1.0f), textContainer.RectTransform), label, textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont, textColor: GUIStyle.TextColorBright)
            {
                Padding = Vector4.Zero
            };
            if (centerTextGetter != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(columnWidth, 1.0f), textContainer.RectTransform), "", textAlignment: Alignment.Center, font: GUIStyle.SmallFont, textColor: Color.White * 0.8f)
                {
                    Padding = Vector4.Zero,
                    TextGetter = () => centerTextGetter()
                };
            }
            Color thresholdNormalColor = Color.LightGray;
            Color thresholdCrossedColor = GUIStyle.Red;

            float cachedProgress = progressGetter();

            new GUITextBlock(new RectTransform(new Vector2(columnWidth, 1.0f), textContainer.RectTransform), null, textAlignment: Alignment.CenterRight, font: GUIStyle.SmallFont, textColor: GUIStyle.TextColorBright)
            {
                Padding = Vector4.Zero,
                TextGetter = () => valueGetter(),
                OnAddedToGUIUpdateList = (component) =>
                {
                    cachedProgress = progressGetter();
                    if (textColorGetter != null)
                    {
                        ((GUITextBlock)component).TextColor = textColorGetter();
                    }
                    else
                    {
                        bool angry = (minThreshold.HasValue && cachedProgress < minThreshold.Value)
                                  || (maxThreshold.HasValue && cachedProgress > maxThreshold.Value);
                        ((GUITextBlock)component).TextColor = angry ? thresholdCrossedColor : GUIStyle.TextColorBright;
                    }
                }
            };
            textContainer.RectTransform.MinSize = new Point(0, height);
            textContainer.RectTransform.MaxSize = new Point(int.MaxValue, height);

            var scrollBarContainer = new GUILayoutGroup(new RectTransform(new Point(container.Rect.Width, height), container.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            scrollBarContainer.RectTransform.MinSize = new Point(0, height);
            scrollBarContainer.RectTransform.MaxSize = new Point(int.MaxValue, height);
            new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.8f), scrollBarContainer.RectTransform), progressGetter(), colorGetter())
            {
                ProgressGetter = () => cachedProgress,
                OnAddedToGUIUpdateList = (GUIComponent component) =>
                {
                    component.Color = colorGetter();
                }
            };
            if (minThreshold.HasValue || maxThreshold.HasValue)
            {
                new GUICustomComponent(new RectTransform(new Vector2(1.0f, 1.0f), scrollBarContainer.RectTransform),
                    onDraw: (spriteBatch, component) =>
                    {
                        if (minThreshold.HasValue)
                        {
                            float minX = MathHelper.Clamp(minThreshold.Value, 0.0f, 1.0f) * component.Rect.Width;
                            Color minLineColor = cachedProgress < minThreshold.Value ? thresholdCrossedColor : thresholdNormalColor;
                            GUI.DrawRectangle(spriteBatch, new Vector2((int)(component.Rect.X + minX - 1), component.Rect.Y), new Vector2(2, component.Rect.Height), minLineColor);
                        }
                        if (maxThreshold.HasValue)
                        {
                            float maxX = MathHelper.Clamp(maxThreshold.Value, 0.0f, 1.0f) * component.Rect.Width;
                            Color maxLineColor = cachedProgress > maxThreshold.Value ? thresholdCrossedColor : thresholdNormalColor;
                            GUI.DrawRectangle(spriteBatch, new Vector2((int)(component.Rect.X + maxX - 1), component.Rect.Y), new Vector2(2, component.Rect.Height), maxLineColor);
                        }
                    })
                {
                    IgnoreLayoutGroups = true
                };
            }
            container.InheritTotalChildrenMinHeight();
            container.RectTransform.MaxSize = new Point(int.MaxValue, container.RectTransform.MinSize.Y);
        }

        private static void CreateLabel(string textTag, int height, GUIComponent parent, Color? labelColor = null, GUIFont font = null)
        {
            CreateLabel(TextManager.Get(textTag), height, parent, labelColor, font);
        }

        private static void CreateLabel(LocalizedString label, int height, GUIComponent parent, Color? labelColor = null, GUIFont font = null)
        {
            var labelTextBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), parent.RectTransform), label,
                textAlignment: Alignment.CenterLeft, textColor: labelColor, font: font ?? GUIStyle.SmallFont,
                color: labelColor)
            {
                Padding = Vector4.Zero
            };
            labelTextBlock.RectTransform.MinSize = new Point(0, height);
            labelTextBlock.RectTransform.MaxSize = new Point(int.MaxValue, height);
        }


        private static GUIComponent CreateLabeledValue(string textTag, Func<LocalizedString> valueGetter, int height, GUIComponent parent, Func<LocalizedString> labelGetter = null, Color? textColor = null, Color? labelColor = null)
        {
            return CreateLabeledValue(TextManager.Get(textTag), valueGetter, height, parent, labelGetter, textColor, labelColor);
        }

        private static GUIComponent CreateLabeledValue(LocalizedString label, Func<LocalizedString> valueGetter, int height, GUIComponent parent, Func<LocalizedString> labelGetter = null, Color? textColor = null, Color? labelColor = null)
        {
            var container = new GUILayoutGroup(new RectTransform(new Point(parent.Rect.Width, height), parent.RectTransform), isHorizontal: true);
            container.RectTransform.MinSize = new Point(0, height);
            container.RectTransform.MaxSize = new Point(int.MaxValue, height);

            if (labelColor.HasValue)
            {
                new GUIFrame(new RectTransform(new Vector2(1.05f, 1.55f), container.RectTransform, Anchor.Center), style: null, color: labelColor)
                {
                    IgnoreLayoutGroups = true
                };
            }

            var labelTextBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), container.RectTransform), label, textAlignment: Alignment.CenterLeft, 
                textColor: textColor ?? GUIStyle.TextColorBright, font: GUIStyle.SmallFont)
            {
                Padding = Vector4.Zero
            };
            if (labelGetter != null)
            {
                labelTextBlock.TextGetter = () => labelGetter();
            }
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), container.RectTransform), "", textAlignment: Alignment.CenterRight, font: GUIStyle.SmallFont,
                textColor: GUIStyle.TextColorBright)
            {
                Padding = Vector4.Zero,
                TextGetter = () => valueGetter()
            };

            return container;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            currentPlanterInfoPanel?.Parent?.RemoveChild(currentPlanterInfoPanel);
            currentPlanterInfoPanel = null;
            lastFocusedPlanter = null;
            elementVisibilityChecks?.Clear();
        }
    }
}
