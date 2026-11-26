using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma.Items.Components
{
    public partial class Planter
    {
        private GUIButton uprootButton, emptyButton;
        private GUILayoutGroup buttonsContainer;

        private GUIProgressBar waterMeter, fertilizerMeter, saltMeter;

        private GUIImage uprootingForbiddenIcon, emptyingForbiddenIcon;

        private GUIScrollBar targetWaterSlider;
        private GUITextBlock targetWaterValueLabel;

        private GUITextBlock plantNameBlock, lightStatusBlock, healthStatusBlock, powerStatusBlock;

        private GUICustomComponent seedIconSlot;

        private static readonly string[] PlantPhaseKeys = new string[]
        {
            "planter.plant.phase.budding",
            "planter.plant.phase.earlygrowing",
            "planter.plant.phase.midgrowing",
            "planter.plant.phase.almostgrown",
            "planter.plant.phase.fullygrown",
            "planter.plant.phase.decayed"
        };
        
        private static readonly string[] PlantHealthKeys = new string[]
        {
            "planter.plant.health.excellent.improving",
            "planter.plant.health.excellent.deteriorating",
            "planter.plant.health.good.improving",
            "planter.plant.health.good.deteriorating",
            "planter.plant.health.poor.improving",
            "planter.plant.health.poor.deteriorating",
            "planter.plant.health.critical.improving",
            "planter.plant.health.critical.deteriorating",
            "planter.plant.health.dying.improving",
            "planter.plant.health.dying.deteriorating"
        };
        
        private int currentHealthIndex = -1;
        private bool isEvaluatingHealth = false;
        private float healthEvaluationTimer = 0f;
        private readonly float healthEvaluationDuration = 5.0f;

        private const float SectionSpacingHeight = 0.018f;

        public Vector2 DrawSize => CalculateSize();

        partial void InitProjSpecific(ContentXElement element)
        {
            CreateGUI();
        }
        
        protected override void CreateGUI()
        {
            if (GuiFrame == null) { return; }
            GuiFrame.ClearChildren();
            
            TryCreateDragHandle(); // Need to be recreated because this depends on CanBeSelected, which in the case of planters is determined dynamically

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.85f, 0.825f), GuiFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.01f)
            }, style: null);
            var statusLayout = new GUILayoutGroup(new RectTransform(Vector2.One, paddedFrame.RectTransform), childAnchor: Anchor.TopLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.04f
            };

            var nameRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.24f), statusLayout.RectTransform),
                isHorizontal: true,
                childAnchor: Anchor.CenterLeft);

            seedIconSlot = new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1.0f), nameRow.RectTransform),
                onDraw: DrawSeedIconSlot)
            {
                CanBeFocused = true
            };

            // Plant name and growth status
            plantNameBlock = new GUITextBlock(new RectTransform(new Vector2(0.75f, 1.0f), nameRow.RectTransform),
                "", font: GUIStyle.Font, wrap: true, textAlignment: Alignment.CenterLeft);
            plantNameBlock.TextGetter = () =>
            {
                var growable = HasGrowables() ? GrowableSeeds.FirstOrDefault(s => s != null) : null;
                if (growable == null)
                {
                    // When there's nothing growing in the planter the panel is kind of uninformative and empty, so show
                    // the same planting hint here that's also shown in the tooltip
                    plantNameBlock.TextColor = GUIStyle.TextColorNormal;
                    return TextManager.Get("planter.emptyseedslot.tooltip").Value;
                }

                string plantName = (growable.GrownPlantName ?? TextManager.Get("planter.plant.unknown")).Value;

                // Verbally describe current phase of growth
                int phaseIndex = GetCurrentPlantPhaseIndex(growable);
                if (phaseIndex >= 0 && phaseIndex < PlantPhaseKeys.Length)
                {
                    plantNameBlock.TextColor = growable.Decayed ? GUIStyle.TextColorDark : GUIStyle.TextColorNormal;
                    return TextManager.GetWithVariable(PlantPhaseKeys[phaseIndex], "[plantname]", plantName).Value;
                }

                return "";
            };

            CreateSpacing(statusLayout, SectionSpacingHeight);

            var meterContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), statusLayout.RectTransform), childAnchor: Anchor.TopLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            waterMeter = CreateMeter(meterContainer, "Water");
            fertilizerMeter = CreateMeter(meterContainer, "Fertilizer");
            saltMeter = CreateMeter(meterContainer, "Salt");

            if (ShowTargetWaterLevel)
            {
                CreateWaterRegulationControls(statusLayout);
            }

            CreateSpacing(statusLayout, SectionSpacingHeight);

            // Light status display
            lightStatusBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), statusLayout.RectTransform),
                "", font: GUIStyle.SmallFont, wrap: true, textAlignment: Alignment.Center);
            lightStatusBlock.TextGetter = () =>
            {
                if (!HasGrowables())
                {
                    return "";
                }

                var growable = GrowableSeeds.FirstOrDefault(s => s != null);
                if (growable == null)
                {
                    return "";
                }

                // Don't show lighting conditions for dead plants
                if (growable.Decayed)
                {
                    return "";
                }

                if (!MathUtils.NearlyEqual(growable.NotEnoughLightDamage, 0f) &&
                    PlantLightAmount < growable.NotEnoughLightThreshold)
                {
                    lightStatusBlock.TextColor = GUIStyle.Orange;
                    return TextManager.Get("planter.light.notenough").Value;
                }
                else if (!MathUtils.NearlyEqual(growable.TooMuchLightDamage, 0f) &&
                    PlantLightAmount > growable.TooMuchLightThreshold)
                {
                    lightStatusBlock.TextColor = GUIStyle.Orange;
                    return TextManager.Get("planter.light.toomuch").Value;
                }
                else
                {
                    lightStatusBlock.TextColor = GUIStyle.TextColorNormal;
                    return TextManager.Get("planter.light.neutral").Value;
                }
            };

            // Verbally describe current health and which way it's going
            healthStatusBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), statusLayout.RectTransform),
                "", font: GUIStyle.SmallFont, wrap: true, textAlignment: Alignment.Center);
            healthStatusBlock.TextGetter = () =>
            {

                if (!HasGrowables())
                {
                    return "";
                }

                var growable = GrowableSeeds.FirstOrDefault(s => s != null);
                if (growable == null)
                {
                    return "";
                }
                
                // If the health status has recently changed, temporarily show "evaluating..." instead of status, to make it feel more natural without instant jumps
                if (isEvaluatingHealth)
                {
                    healthStatusBlock.TextColor = GUIStyle.TextColorBright;
                    return TextManager.Get("planter.plant.evaluating").Value;
                }

                // Check if plant is decayed first, before checking health status
                if (growable.Decayed)
                {
                    healthStatusBlock.TextColor = GUIStyle.Red;
                    return TextManager.GetWithVariable(PlantPhaseKeys[5], "[plantname]", growable.GrownPlantName ?? TextManager.Get("planter.plant.unknown")).Value;
                }

                // Get the current health status and display it
                int healthIndex = GetCurrentPlantHealthIndex(growable);
                if (healthIndex >= 0 && healthIndex < PlantHealthKeys.Length)
                {
                    float healthPercent = growable.Health / growable.MaxHealth * 100;
                    bool hasOptimalConditions = Growable.HasOptimalConditions(growable, this);
                    bool isProblematic = healthPercent < 50f || !hasOptimalConditions;

                    healthStatusBlock.TextColor = isProblematic ? GUIStyle.Orange : GUIStyle.TextColorNormal;
                    return TextManager.Get(PlantHealthKeys[healthIndex]).Value;
                }

                return "";
            };

            // Tell the user about the planter's optional power requirement, regardless of if there's anything growing in it
            powerStatusBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), statusLayout.RectTransform),
                TextManager.Get("planter.powerhint"), font: GUIStyle.SmallFont, wrap: true, textAlignment: Alignment.Center);

            // Some empty space before the buttons, the panel looks visually more balanced like that
            CreateSpacing(statusLayout, 0.05f);

            // Uproot and Empty buttons
            buttonsContainer = new GUILayoutGroup(new RectTransform(new Vector2(1, 0.28f), statusLayout.RectTransform),
                childAnchor: Anchor.Center)
            {
                Stretch = true,
                RelativeSpacing = 0.12f
            };
            uprootButton = new GUIButton(new RectTransform(new Vector2(0.6f, 0.5f), buttonsContainer.RectTransform),
                TextManager.Get("planter.uproot"), style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("planter.uproot.tooltip"),
                OnClicked = (_, _) =>
                {
                    if (Character.Controlled == null) { return false; }
                    //close panel by deselecting the item
                    Character.Controlled.SelectedItem = null;
                    StartUprootingOrPlanting(Character.Controlled);
                    item.CreateClientEvent(this, new EventData(PlanterSyncEvent.Uproot));
                    return false;
                },
            };
            uprootingForbiddenIcon = new GUIImage(
                new RectTransform(new Vector2(1.5f), uprootButton.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.Smallest) { AbsoluteOffset = new Point(-GUI.IntScale(15), 0) },
                CharacterInventory.LimbSlotIcons[InvSlotType.LeftHand], scaleToFit: true)
            {
                Visible = false,
                Color = GUIStyle.Red,
                HoverColor = Color.Lerp(GUIStyle.Red, Color.White, 0.5f),
                ToolTip = TextManager.Get("planter.uproot.forbidden")
            };
            
            emptyButton = new GUIButton(new RectTransform(new Vector2(0.6f, 0.5f), buttonsContainer.RectTransform),
                TextManager.Get("planter.makeempty"),
                style: "GUIButtonSmall")
            {
                ToolTip = TextManager.Get("planter.makeempty.tooltip"),
                OnAddedToGUIUpdateList = (GUIComponent component) =>
                {
                    if (component is GUIButton button)
                    {
                        button.Enabled = HasAnythingToEmpty();
                    }
                },
                OnClicked = (_, _) =>
                {
                    if (Character.Controlled == null) { return false; }
                    Character.Controlled.SelectedItem = null; // Close panel by deselecting the item
                    
                    // No need for confirmation, the emptying action has a progress bar so the player can still move away to cancel
                    StartEmptying(Character.Controlled);

                    item.CreateClientEvent(this, new EventData(PlanterSyncEvent.Empty));
                    return false;
                },
            };
            emptyingForbiddenIcon = new GUIImage(
                new RectTransform(new Vector2(1.5f), emptyButton.RectTransform, Anchor.CenterLeft,
                    scaleBasis: ScaleBasis.Smallest)
                {
                    AbsoluteOffset = new Point(-GUI.IntScale(15), 0)
                },
                CharacterInventory.LimbSlotIcons[InvSlotType.LeftHand], scaleToFit: true)
            {
                Visible = false,
                Color = GUIStyle.Red,
                HoverColor = Color.Lerp(GUIStyle.Red, Color.White, 0.5f),
                ToolTip = TextManager.Get("planter.uproot.forbidden")
            };
        }

        private static void CreateSpacing(GUILayoutGroup parent, float relativeHeight)
        {
            new GUIFrame(new RectTransform(new Vector2(1.0f, relativeHeight), parent.RectTransform), style: null);
        }

        private void DrawSeedIconSlot(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            Rectangle boxRect = GetSeedIconSlotRect();
            float boxSize = boxRect.Width;
            Vector2 boxCenter = boxRect.Center.ToVector2();

            if (Inventory.SlotSpriteSmall is Sprite slotSprite)
            {
                slotSprite.Draw(spriteBatch, boxRect.Location.ToVector2(),
                    scale: boxSize / slotSprite.SourceRect.Width,
                    color: Color.White);
            }

            var growable = GrowableSeeds.FirstOrDefault(s => s != null);
            if (growable?.Item is Item seedItem) // something's already growing in the planter
            {
                component.ToolTip = seedItem.Description;

                // Resolve the icon like the inventory does in case there's ever some differing ways growables are defined
                Sprite icon = seedItem.OverrideInventorySprite ?? seedItem.Prefab.InventoryIcon ?? seedItem.Sprite;
                if (icon != null)
                {
                    Color iconColor = icon == seedItem.Sprite ? seedItem.GetSpriteColor() : seedItem.GetInventoryIconColor();
                    float iconScale = (boxSize - GUI.IntScale(8)) / Math.Max(icon.size.X, icon.size.Y);
                    icon.Draw(spriteBatch, boxCenter, iconColor, rotate: 0f, scale: iconScale);
                }
            }
            else // planter's empty
            {
                component.ToolTip = TextManager.Get("planter.emptyseedslot.tooltip");
                if (Inventory.DraggingItems.FirstOrDefault() is Item draggedItem &&
                    CanPlantItem(draggedItem, Character.Controlled) &&
                    boxRect.Contains(PlayerInput.MousePosition))
                {
                    GUIStyle.UIGlow.Draw(spriteBatch, boxRect, GUIStyle.Green);
                }
            }
        }

        public bool IsMouseOnSeedIconSlot()
        {
            return seedIconSlot is { Visible: true } && GetSeedIconSlotRect().Contains(PlayerInput.MousePosition);
        }

        private Rectangle GetSeedIconSlotRect()
        {
            if (seedIconSlot == null) { return Rectangle.Empty; }

            Rectangle rect = seedIconSlot.Rect;
            int boxSize = Math.Min(rect.Width, rect.Height);
            return new Rectangle(rect.Right - boxSize, rect.Y + (rect.Height - boxSize) / 2,
                            boxSize, boxSize);
        }

        private GUIProgressBar CreateMeter(GUILayoutGroup parent, string labelText)
        {
            var meterRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), parent.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), meterRow.RectTransform),
                labelText, font: GUIStyle.SmallFont, textAlignment: Alignment.CenterRight);

            // Meter background with sprite (TODO: replace with actual art? preferably something a bit different for each meter based on what they measure?)
            var meterBackground = new GUIImage(new RectTransform(new Vector2(0.5f, 1.0f), meterRow.RectTransform),
                sprite: null, scaleToFit: true)
            {
                Color = Color.DarkGray
            };
            
            var progressBar = new GUIProgressBar(new RectTransform(Vector2.One, meterBackground.RectTransform),
                0.0f, Color.White);

            return progressBar;
        }

        private void CreateWaterRegulationControls(GUILayoutGroup parent)
        {
            var sliderRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.12f), parent.RectTransform),
                isHorizontal: true,
                childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), sliderRow.RectTransform),
                TextManager.Get("hydroponic.waterlevel"),
                font: GUIStyle.SmallFont,
                textAlignment: Alignment.CenterRight);

            targetWaterSlider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 0.9f), sliderRow.RectTransform),
                barSize: 0.1f,
                style: "DeviceSliderSeeThrough")
            {
                BarScroll = TargetWaterLevel / 100f,
                // The actual target is only set on release, the label shows the slider's value while dragging
                OnReleased = (scrollBar, barScroll) =>
                {
                    TargetWaterLevel = barScroll * 100f;
                    // Delay server corrections until the server has had time to receive the new value
                    if (GameMain.Client != null) { correctionTimer = CorrectionDelay; }
                    // Only push the value to the server on release to avoid needless spamming
                    SendSettingsUpdate();
                    return true;
                }
            };

            targetWaterValueLabel = new GUITextBlock(new RectTransform(new Vector2(0.16f, 1.0f), sliderRow.RectTransform),
                "", font: GUIStyle.SmallFont, textAlignment: Alignment.CenterLeft)
            {
                TextGetter = () =>
                {
                    float shownValue = GUIScrollBar.DraggingBar == targetWaterSlider ? targetWaterSlider.BarScroll * 100f : TargetWaterLevel;
                    return $"{(int)Math.Round(shownValue)} %";
                }
            };
        }

        private void SendSettingsUpdate()
        {
            if (GameMain.NetworkMember != null) // MP?
            {
                item.CreateClientEvent(this, new EventData(PlanterSyncEvent.Settings));
            }
        }

        // Show or hide the plant-specific UI elements (depending on if there's a plant or not)
        private void SetPlantSpecificUIVisible(bool visible)
        {
            foreach (GUIComponent component in new GUIComponent[] { lightStatusBlock, healthStatusBlock })
            {
                if (component == null) { continue; }
                component.Visible = visible;
            }
        }

        public Color GetWaterMeterColor(Growable growable)
        {
            if (growable != null)
            {
                if (NormalizedWaterLevel < growable.TooDryThreshold)
                {
                    return GUIStyle.Red;
                }
                else if (NormalizedWaterLevel > growable.OverwaterThreshold)
                {
                    return GUIStyle.Orange;
                }
            }
            return Color.LightBlue;
        }

        public Color GetFertilizerMeterColor(Growable growable)
        {
            if (growable != null)
            {
                if (NormalizedFertilizerLevel < growable.UnderfertilizedThreshold)
                {
                    return GUIStyle.Red;
                }
                else if (NormalizedFertilizerLevel > growable.OverfertilizedThreshold)
                {
                    return GUIStyle.Orange;
                }
            }
            return Color.LightGreen;
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            //planters did not used to have UIs, and they're not mandatory to have in mods
            // -> no UI, do nothing
            if (GuiFrame == null) { return; }

            var growable = GrowableSeeds.FirstOrDefault(s => s != null);
            bool hasPlant = growable != null;

            // Hide the plant name/light/health text and the uproot button when the planter has no plant in it
            SetPlantSpecificUIVisible(hasPlant);

            powerStatusBlock.Visible = !HasPower;

            uprootButton.Visible = hasPlant;
            uprootingForbiddenIcon.Visible = hasPlant && item.Illegitimate;
            emptyingForbiddenIcon.Visible = hasPlant && item.Illegitimate;

            // The Planter always shows the meters, whether or not something is growing in it
            waterMeter.Enabled = fertilizerMeter.Enabled = saltMeter.Enabled = true;
            waterMeter.BarSize = MaxWater > 0 ? CurrentWater / MaxWater : 0f;
            fertilizerMeter.BarSize = FertilizerCapacity > 0 ? Fertilizer / FertilizerCapacity : 0f;
            saltMeter.BarSize = Salinity / SeawaterSalinity;

            if (!hasPlant)
            {
                // No plant to determine if the levels are good or bad, so use neutral colors
                waterMeter.Color = Color.LightBlue;
                fertilizerMeter.Color = Color.LightGreen;
                saltMeter.Color = Salinity < 0.1f ? Color.DarkGray : Color.LightBlue;

                currentHealthIndex = -1;
                isEvaluatingHealth = false;
                healthEvaluationTimer = 0f;
                
                return;
            }

            UpdatePlantHealthEvaluation(growable, deltaTime);

            waterMeter.Color = GetWaterMeterColor(growable);
            fertilizerMeter.Color = GetFertilizerMeterColor(growable);

            if (Salinity < 0.1f)
            {
                // Dim salt meter when salinity is not relevant
                saltMeter.Color = Color.DarkGray;
            }
            else
            {
                float salinityEffect = growable.CurrentSalinityEffect(this);
                saltMeter.Color = GUIStyle.Red;
                if (salinityEffect > 0)
                {
                    saltMeter.Color = Color.LightGreen;
                }
                else if (salinityEffect == 0)
                {
                    saltMeter.Color = Color.LightBlue;
                }
            }
        }

        private Vector2 CalculateSize()
        {
            if (GrowableSeeds.All(s => s == null)) { return Vector2.Zero; }

            Point pos = item.DrawPosition.ToPoint();
            Rectangle rect = new Rectangle(pos, Point.Zero);

            for (int i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable seed = GrowableSeeds[i];
                PlantSlot slot = PlantSlots.ContainsKey(i) ? PlantSlots[i] : NullSlot; 
                if (seed == null) { continue; }

                foreach (VineTile vine in seed.Vines)
                {
                    Rectangle worldRect = vine.Rect;
                    worldRect.Location += slot.Offset.ToPoint();
                    worldRect.Location += pos;
                    rect = Rectangle.Union(rect, worldRect);
                }
            }

            Vector2 result = new Vector2(MaxDistance(pos.X, rect.Left, rect.Right) * 2, MaxDistance(pos.Y, rect.Top, rect.Bottom) * 2);
            return result;

            static float MaxDistance(float origin, float x, float y)
            {
                return Math.Max(Math.Abs(origin - x), Math.Abs(origin - y));
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            for (var i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable growable = GrowableSeeds[i];
                PlantSlot slot = PlantSlots.ContainsKey(i) ? PlantSlots[i] : NullSlot;
                growable?.Draw(spriteBatch, this, slot.Offset, itemDepth);
            }

#if DEBUG
            if (GameMain.DebugDraw && HasBeenPlaced && HasGrowables())
            {
                foreach (var (from, to, blocked) in DebugRaycastLines)
                {
                    // Convert world positions to draw positions (flip Y)
                    Vector2 drawFrom = new Vector2(from.X, -from.Y);
                    Vector2 drawTo = new Vector2(to.X, -to.Y);

                    // Green = success (light reaches), Red = blocked
                    Color lineColor = blocked ? Color.Red : Color.LimeGreen;
                    GUI.DrawLine(spriteBatch, drawFrom, drawTo, lineColor, width: 2);
                }
            }
            else
            {
                DebugRaycastLines.Clear();
            }
#endif
        }
        
        public override void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;

            float currentWater = msg.ReadRangedSingle(0, MaxWater, 16);
            float fertilizer = msg.ReadRangedSingle(0, FertilizerCapacity, 16);
            float salinity = msg.ReadRangedSingle(0, SeawaterSalinity, 16);
            float targetWaterLevel = msg.ReadRangedSingle(0f, 100f, 16);

            // The player has recently adjusted the target water level -> wait before applying the server state,
            // so a stale update (sent before the server received the new value) doesn't revert it
            if (correctionTimer > 0.0f)
            {
                int msgLength = msg.BitPosition - msgStartPos;
                msg.BitPosition = msgStartPos;
                StartDelayedCorrection(msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            CurrentWater = currentWater;
            Fertilizer = fertilizer;
            Salinity = salinity;
            TargetWaterLevel = targetWaterLevel;
            // Update the slider but only if it's not being dragged by the player
            if (targetWaterSlider != null && GUIScrollBar.DraggingBar != targetWaterSlider)
            {
                targetWaterSlider.BarScroll = TargetWaterLevel / 100f;
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            var (syncEvent, plantedSeed) = ExtractEventData<EventData>(extraData);
            msg.WriteByte((byte)syncEvent);
            // If the sync is for Uproot or Empty, no further data needs to be written
            if (syncEvent == PlanterSyncEvent.Settings)
            {
                msg.WriteRangedSingle(TargetWaterLevel, 0f, 100f, 16);
            }
            else if (syncEvent == PlanterSyncEvent.Plant)
            {
                msg.WriteUInt16(plantedSeed?.ID ?? Entity.NullEntityID);
            }
        }

        private void UpdatePlantHealthEvaluation(Growable growable, float deltaTime)
        {
            int newHealthIndex = GetCurrentPlantHealthIndex(growable);
            
            // Check if health status has changed
            if (newHealthIndex != currentHealthIndex)
            {
                if (!isEvaluatingHealth)
                {
                    isEvaluatingHealth = true;
                    healthEvaluationTimer = 0f;
                }
            }
            
            if (isEvaluatingHealth)
            {
                healthEvaluationTimer += deltaTime;
                if (healthEvaluationTimer >= healthEvaluationDuration)
                {
                    isEvaluatingHealth = false;
                    healthEvaluationTimer = 0f;
                    currentHealthIndex = newHealthIndex;
                }
            }
        }

        private int GetCurrentPlantPhaseIndex(Growable growable)
        {
            if (growable.Decayed)
            {
                return 5;
            }
            else if (growable.FullyGrown)
            {
                return 4;
            }
            else
            {
                if (growable.Vines.Count <= 1)
                {
                    return 0; // Budding (0-1 vines)
                }
                else
                {
                    float growthPercent = (float)growable.Vines.Count / growable.MaximumVines;
                    if (growthPercent < 0.33f)
                    {
                        return 1; // Early growth (more than 1 vine but less than 1/3 of max vines)
                    }
                    else if (growthPercent < 0.67f)
                    {
                        return 2; // Mid growth (between 1/3 and 2/3 of max vines)
                    }
                    else
                    {
                        return 3; // Almost grown (between 2/3 and max vines)
                    }
                }
            }
        }

        private int GetCurrentPlantHealthIndex(Growable growable)
        {
            float healthPercent = growable.Health / growable.MaxHealth * 100;
            bool hasOptimalConditions = Growable.HasOptimalConditions(growable, this);

            switch (healthPercent)
            {
                case > 80:
                    return hasOptimalConditions ? 0 : 1; // excellent.improving : excellent.deteriorating
                case > 60:
                    return hasOptimalConditions ? 2 : 3; // good.improving : good.deteriorating
                case > 40:
                    return hasOptimalConditions ? 4 : 5; // poor.improving : poor.deteriorating
                case > 20:
                    return hasOptimalConditions ? 6 : 7; // critical.improving : critical.deteriorating
                default:
                    return hasOptimalConditions ? 8 : 9; // dying.improving : dying.deteriorating
            }
        }

        public static void OpenPlanterEditor(Planter planter)
        {
            GUIMessageBox msgBox = new GUIMessageBox(planter.item.Name, string.Empty, buttons: [TextManager.Get("applysettingsbutton"), TextManager.Get("cancel")], relativeSize: new Vector2(0.25f, 0.45f));
            GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.78f), msgBox.Content.RectTransform), childAnchor: Anchor.TopCenter);

            GUIListBox seedSlotList = new GUIListBox(new RectTransform(new Vector2(1f, 0.4f), content.RectTransform), isHorizontal: true)
            {
                CurrentDragMode = GUIListBox.DragMode.DragOutsideBoxWithoutRepositioning,
                CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                HideDraggedElement = true,
                ScrollBarVisible = true,
                AutoHideScrollBar = false
            };

            GUIComponent slotParent;
            Vector2 childSize;

            GUIFrame arrow = new GUIFrame(new RectTransform(new Vector2(0.15f, 0.1f), content.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow")
            {
                SpriteEffects = SpriteEffects.FlipVertically,
                ToolTip = TextManager.Get("subeditor.plantertools.tooltip")
            };

            Vector2 slotSize = new Vector2(1f, 0.4f);

            // way too much effort for something that will probably never be used
            switch (planter.PlantSlots.Count)
            {
                case 1:
                {
                    childSize = new Vector2(0.33f, 1f);
                    slotParent = new GUILayoutGroup(new RectTransform(slotSize, content.RectTransform), childAnchor: Anchor.Center);
                    break;
                }
                case 2:
                {
                    childSize = new Vector2(0.5f, 1f);
                    GUILayoutGroup container = new GUILayoutGroup(new RectTransform(slotSize, content.RectTransform), childAnchor: Anchor.Center);
                    slotParent = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), container.RectTransform), isHorizontal: true);
                    break;
                }
                case 3:
                {
                    childSize = new Vector2(0.33f, 1f);
                    slotParent = new GUILayoutGroup(new RectTransform(slotSize, content.RectTransform), isHorizontal: true);
                    break;
                }
                default:
                {
                    childSize = new Vector2(0.33f, 1f);
                    GUIListBox container = new GUIListBox(new RectTransform(slotSize, content.RectTransform), isHorizontal: true)
                    {
                        ScrollBarVisible = true,
                        AutoHideScrollBar = false
                    };
                    slotParent = container.Content;
                    break;
                }
            }

            foreach (int index in planter.PlantSlots.Keys)
            {
                GUIFrame slotFrame = new GUIFrame(new RectTransform(childSize, slotParent.RectTransform), style: "ListBoxElement")
                {
                    UserData = index
                };
                GUICustomComponent slotComponent = new GUICustomComponent(new RectTransform(new Vector2(0.9f), slotFrame.RectTransform, Anchor.Center))
                {
                    CanBeFocused = false,
                };

                GUIListBox seedContainer = new GUIListBox(new RectTransform(new Vector2(0.9f), slotFrame.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: null)
                {
                    ScrollBarEnabled = false,
                    AutoHideScrollBar = false,
                    ScrollBarVisible = false,
                    HideDraggedElement = true,
                    CurrentDragMode = GUIListBox.DragMode.DragOutsideBoxWithoutRepositioning,
                    CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                };

                bool isDragging = false;
                slotComponent.OnUpdate = (f, c) =>
                {
                    if (isDragging)
                    {
                        if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            if (!msgBox.InnerFrame.Rect.Contains(PlayerInput.MousePosition) || seedSlotList.Rect.Contains(PlayerInput.MousePosition))
                            {
                                seedContainer.Content.ClearChildren();
                            }
                        }
                    }

                    isDragging = seedContainer.DraggedElement != null;
                };

                slotComponent.OnDraw = (sb, c) =>
                {
                    if (Inventory.SlotSpriteSmall is { } sprite)
                    {
                        float slotDrawSize = MathF.Min(c.Rect.Size.X, c.Rect.Size.Y);
                        Vector2 slotPos = c.Rect.Location.ToVector2() - new Vector2((slotDrawSize - c.Rect.Size.X) / 2f, (slotDrawSize - c.Rect.Size.Y) / 2f);
                        sprite.Draw(sb, slotPos,
                                    scale: slotDrawSize / Inventory.SlotSpriteSmall.SourceRect.Width,
                                    color: Color.White);
                    }

                    seedContainer.DraggedElement?.DrawManually(sb, true);
                };

                if (index < 0 || index >= planter.GrowableSeeds.Length) { continue; }

                if (planter.GrowableSeeds[index] is { Item.Prefab: { } prefab })
                {
                    CreateSeedButton(seedContainer, prefab);
                }
            }

            CreateSeedList(seedSlotList, planter);
            GUIComponent lastDraggedElement = null;

            new GUICustomComponent(new RectTransform(Vector2.Zero, content.RectTransform),
                                   onUpdate: (f, component) =>
                                   {
                                       // mouse released
                                       if (PlayerInput.PrimaryMouseButtonClicked())
                                       {
                                           if (lastDraggedElement is { } draggedElement)
                                           {
                                               if (draggedElement.UserData is not ItemPrefab prefab) { return; }

                                               foreach (GUIComponent child in slotParent.Children)
                                               {
                                                   if (child.Rect.Contains(PlayerInput.MousePosition))
                                                   {
                                                       CreateSeedButton(child.GetChild<GUIListBox>(), prefab);
                                                   }
                                               }
                                           }
                                       }

                                       lastDraggedElement = seedSlotList.DraggedElement;
                                   },
                                   onDraw: (spriteBatch, component) =>
                                   {
                                       seedSlotList.DraggedElement?.DrawManually(spriteBatch, true);
                                   })
            {
                IgnoreLayoutGroups = true,
            };

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                msgBox.Close();
                if (planter.Item.GetComponent<ItemContainer>()?.Inventory is not { } inv) { return true; }
                inv.DeleteAllItems();
                Array.Clear(planter.GrowableSeeds);

                foreach (GUIComponent child in slotParent.Children)
                {
                    if (child.UserData is not int index) { continue; }

                    GUIComponent seedContainer = child.GetChild<GUIListBox>().Content;
                    if (seedContainer.GetChild(0) is { UserData: ItemPrefab prefab })
                    {
                        Item newItem = new Item(prefab, Vector2.Zero, Submarine.MainSub);
                        if (!inv.TryPutItem(newItem, index, allowSwapping: false, allowCombine: false, Character.Controlled, createNetworkEvent: false))
                        {
                            DebugConsole.ThrowError($"Failed to add the item \"{newItem.Name}\" to the planter (index {index})");
                            newItem.Remove();
                            continue;
                        }

                        if (newItem.GetComponent<Growable>() is { } growable)
                        {
                            growable.GenerateStem();
                            growable.Decayed = false;
                            growable.FullyGrown = false;
                            while (growable.MaximumVines > growable.Vines.Count)
                            {
                                if (!growable.CanGrowMore())
                                {
                                    break;
                                }

                                growable.TryGenerateBranches(planter, planter.PlantSlots[index]);
                            }

                            if (!growable.Decayed)
                            {
                                growable.FullyGrown = true;
                            }

                            foreach (VineTile vineTile in growable.Vines)
                            {
                                vineTile.GrowthStep = VineTile.FullyGrownWithFlowers;
                            }
                            //make sure the health reflects the new size of the plant after we've "artificially" modified the size
                            growable.RestoreFullHealth();
                            planter.GrowableSeeds[index] = growable;
                        }
                    }
                }

                return true;
            };
            msgBox.Buttons[1].OnClicked = (btn, userdata) =>
            {
                msgBox.Close();
                return true;
            };
            return;

            static void CreateSeedList(GUIListBox seedSlotList, Planter planter)
            {
                ItemContainer container = planter.Item.GetComponent<ItemContainer>();
                if (container?.Inventory == null) { return; }

                foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
                {
                    if (container.CanBeContained(prefab))
                    {
                        GUIFrame seedFrame = new GUIFrame(new RectTransform(Vector2.One, seedSlotList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "ListBoxElement")
                        {
                            UserData = prefab,
                            ToolTip = prefab.Name
                        };
                        GUIImage seedButton = new GUIImage(new RectTransform(Vector2.One, seedFrame.RectTransform),
                                                           prefab.InventoryIcon ?? prefab.Sprite, scaleToFit: true)
                        {
                            CanBeFocused = false,
                        };
                        seedButton.Color
                            = seedButton.SelectedColor
                                  = seedButton.HoverColor
                                        = prefab.InventoryIcon != null
                                              ? prefab.InventoryIconColor
                                              : prefab.SpriteColor;
                    }
                }
                seedSlotList.RecalculateChildren();
            }

            static void CreateSeedButton(GUIListBox parent, ItemPrefab prefab)
            {
                parent.Content.ClearChildren();
                GUIButton seedButton = new GUIButton(new RectTransform(Vector2.One, parent.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), text: string.Empty, style: null)
                {
                    ToolTip = prefab.Name,
                    UserData = prefab,
                    OnClicked = (btn, userdata) =>
                    {
                        if (PlayerInput.DoubleClicked())
                        {
                            parent.Content.RemoveChild(btn);
                        }
                        return true;
                    }
                };

                GUIImage image = new GUIImage(new RectTransform(Vector2.One, seedButton.RectTransform),
                                            prefab.InventoryIcon ?? prefab.Sprite, scaleToFit: true)
                {
                    CanBeFocused = false,
                };

                image.Color
                    = image.PressedColor
                          = image.SelectedColor
                                = image.HoverColor
                                      = prefab.InventoryIcon != null
                                            ? prefab.InventoryIconColor
                                            : prefab.SpriteColor;
            }
        }
    }
}