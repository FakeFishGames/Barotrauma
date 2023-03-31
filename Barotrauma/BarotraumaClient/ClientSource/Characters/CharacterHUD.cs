using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterHUD
    {        
        const float BossHealthBarDuration = 120.0f;

        abstract class BossProgressBar
        {
            public float FadeTimer;

            public readonly GUIComponent TopContainer;
            public readonly GUIComponent SideContainer;

            public readonly GUIProgressBar TopHealthBar;
            public readonly GUIProgressBar SideHealthBar;

            public abstract bool Completed { get; }

            public abstract bool Interrupted { get; }

            public abstract float State { get; }

            public abstract string NumberToDisplay { get; }

            public abstract Color Color { get; }

            public BossProgressBar(LocalizedString label)
            {
                FadeTimer = BossHealthBarDuration;

                TopContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.18f, 0.03f), HUDFrame.RectTransform, Anchor.TopCenter)
                {
                    MinSize = new Point(100, 50),
                    RelativeOffset = new Vector2(0.0f, 0.01f)
                }, isHorizontal: false, childAnchor: Anchor.TopCenter);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), TopContainer.RectTransform), label, textAlignment: Alignment.Center, textColor: GUIStyle.Red);
                TopHealthBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.6f), TopContainer.RectTransform)
                {
                    MinSize = new Point(100, HUDLayoutSettings.HealthBarArea.Size.Y)
                }, barSize: 0.0f, style: "CharacterHealthBarCentered")
                {
                    Color = GUIStyle.Red
                };
                CreateNumberText(TopHealthBar);

                SideContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), bossHealthContainer.RectTransform)
                {
                    MinSize = new Point(80, 60)
                }, isHorizontal: false, childAnchor: Anchor.TopRight);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), SideContainer.RectTransform), label, textAlignment: Alignment.CenterRight, textColor: GUIStyle.Red);
                SideHealthBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.7f), SideContainer.RectTransform), barSize: 0.0f, style: "CharacterHealthBar")
                {
                    Color = GUIStyle.Red
                };
                CreateNumberText(SideHealthBar);

                TopContainer.Visible = SideContainer.Visible = false;
                TopContainer.CanBeFocused = false;
                TopContainer.Children.ForEach(c => c.CanBeFocused = false);
                SideContainer.CanBeFocused = false;
                SideContainer.Children.ForEach(c => c.CanBeFocused = false);

                void CreateNumberText(GUIComponent parent)
                {
                    new GUITextBlock(new RectTransform(Vector2.One, parent.RectTransform)
                        { AbsoluteOffset = new Point(2) }, 
                        string.Empty, textAlignment: Alignment.Center, textColor: GUIStyle.TextColorDark)
                    {
                        TextGetter = () => NumberToDisplay
                    };
                    new GUITextBlock(new RectTransform(Vector2.One, parent.RectTransform), 
                        string.Empty, textAlignment: Alignment.Center, textColor: GUIStyle.TextColorBright)
                    {
                        TextGetter = () => NumberToDisplay
                    };
                }
            }

            public abstract bool IsDuplicate(object targetObject);
        }

        class BossHealthBar : BossProgressBar
        {
            public readonly Character Character;

            public override float State => Character.Vitality / Character.MaxVitality;

            public override bool Completed => Character.IsDead;

            public override bool Interrupted => Character.Removed || !Character.Enabled;

            public override Color Color => 
                Character.CharacterHealth.GetAfflictionStrength(AfflictionPrefab.PoisonType) > 0 || Character.CharacterHealth.GetAfflictionStrength(AfflictionPrefab.ParalysisType) > 0 ? 
                    GUIStyle.HealthBarColorPoisoned : GUIStyle.Red;

            public override string NumberToDisplay => string.Empty;

            public BossHealthBar(Character character) : base(character.DisplayName)
            {
                Character = character;
            }

            public override bool IsDuplicate(object targetObject)
            {
                return targetObject is Character character && Character == character;
            }
        }

        class MissionProgressBar : BossProgressBar
        {
            public readonly Mission Mission;

            public override float State => Mission.State / (float)Mission.Prefab.MaxProgressState;

            public override bool Completed => Mission.State >= Mission.Prefab.MaxProgressState;

            public override bool Interrupted => Mission.Failed || GameMain.GameSession?.Missions == null || !GameMain.GameSession.Missions.Contains(Mission);

            public override Color Color => GUIStyle.Red;

            public override string NumberToDisplay => Mission.Prefab.ShowProgressInNumbers ?
                $"{Mission.State}/{Mission.Prefab.MaxProgressState}" :
                string.Empty;

            public MissionProgressBar(Mission mission) : base(mission.Prefab.ProgressBarLabel)
            {
                Mission = mission;
            }

            public override bool IsDuplicate(object targetObject)
            {
                return targetObject is Mission mission && Mission == mission;
            }
        }

        private static readonly Dictionary<ISpatialEntity, int> orderIndicatorCount = new Dictionary<ISpatialEntity, int>();
        const float ItemOverlayDelay = 1.0f;
        private static Item focusedItem;
        private static float focusedItemOverlayTimer;
        
        private static readonly List<Item> brokenItems = new List<Item>();
        private static float brokenItemsCheckTimer;

        private static readonly List<BossProgressBar> bossProgressBars = new List<BossProgressBar>();

        private static readonly Dictionary<Identifier, LocalizedString> cachedHudTexts = new Dictionary<Identifier, LocalizedString>();
        private static LanguageIdentifier cachedHudTextLanguage = LanguageIdentifier.None;

        private static GUILayoutGroup bossHealthContainer;

        private static GUIFrame hudFrame;
        public static GUIFrame HUDFrame
        {

            get
            {
                if (hudFrame == null)
                {
                    hudFrame = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas), style: null)
                    {
                        CanBeFocused = false
                    };
                    bossHealthContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 0.5f), hudFrame.RectTransform, Anchor.CenterRight)
                    {
                        RelativeOffset = new Vector2(0.005f, 0.0f)
                    })
                    {
                        AbsoluteSpacing = GUI.IntScale(10)
                    };
                }
                return hudFrame;
            }
        }

        public static bool RecreateHudTexts { get; set; } = true;
        private static bool lastHudTextsContextual;
        private static float timeHealthWindowClosed;

        public static bool IsCampaignInterfaceOpen =>
            GameMain.GameSession?.Campaign != null && 
            (GameMain.GameSession.Campaign.ShowCampaignUI || GameMain.GameSession.Campaign.ForceMapUI);

        public static bool ShouldDrawInventory(Character character)
        {
            var controller = character.SelectedItem?.GetComponent<Controller>();

            return 
                character?.Inventory != null && 
                !character.Removed && !character.IsKnockedDown &&
                (controller?.User != character || !controller.HideHUD || Screen.Selected.IsEditor) &&
                !IsCampaignInterfaceOpen &&
                !ConversationAction.FadeScreenToBlack;
        }

        public static LocalizedString GetCachedHudText(string textTag, InputType keyBind)
        {
            if (cachedHudTextLanguage != GameSettings.CurrentConfig.Language)
            {
                cachedHudTexts.Clear();
            }
            Identifier key = (textTag + keyBind).ToIdentifier();
            if (cachedHudTexts.TryGetValue(key, out LocalizedString text)) { return text; }
            text = TextManager.GetWithVariable(textTag, "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(keyBind)).Value;
            cachedHudTexts.Add(key, text);
            cachedHudTextLanguage = GameSettings.CurrentConfig.Language;
            return text;
        }
        
        public static void AddToGUIUpdateList(Character character)
        {
            if (GUI.DisableHUD) { return; }
            
            if (!character.IsIncapacitated && character.Stun <= 0.0f && !IsCampaignInterfaceOpen)
            {
                if (character.Inventory != null)
                {
                    for (int i = 0; i < character.Inventory.Capacity; i++)
                    {
                        var item = character.Inventory.GetItemAt(i);
                        if (item == null || character.Inventory.SlotTypes[i] == InvSlotType.Any) { continue; }

                        foreach (ItemComponent ic in item.Components)
                        {
                            if (ic.DrawHudWhenEquipped) { ic.AddToGUIUpdateList(); }
                        }
                    }
                }

                if (character.Params.CanInteract && character.SelectedCharacter != null)
                {
                    character.SelectedCharacter.CharacterHealth.AddToGUIUpdateList();
                }
            }

            HUDFrame.AddToGUIUpdateList();
        }

        public static void Update(float deltaTime, Character character, Camera cam)
        {
            UpdateBossProgressBars(deltaTime);

            if (GUI.DisableHUD)
            {
                if (character.Inventory != null && !LockInventory(character))
                {
                    character.Inventory.UpdateSlotInput();
                }
                return;
            }

            if (!character.IsIncapacitated && character.Stun <= 0.0f && !IsCampaignInterfaceOpen)
            {
                if (character.Info != null && !character.ShouldLockHud() && character.SelectedCharacter == null && Screen.Selected != GameMain.SubEditorScreen)
                {
                    bool mouseOnPortrait = MouseOnCharacterPortrait() && GUI.MouseOn == null;
                    bool healthWindowOpen = CharacterHealth.OpenHealthWindow != null || timeHealthWindowClosed < 0.2f;
                    if (mouseOnPortrait && !healthWindowOpen && PlayerInput.PrimaryMouseButtonClicked() && Inventory.DraggingItems.None())
                    {
                        CharacterHealth.OpenHealthWindow = character.CharacterHealth;
                    }
                }

                if (character.Inventory != null)
                {
                    if (!LockInventory(character))
                    {
                        character.Inventory.Update(deltaTime, cam);
                    }
                    else
                    {
                        character.Inventory.ClearSubInventories();
                    }
                }

                if (character.Params.CanInteract && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (character.SelectedCharacter.CanInventoryBeAccessed)
                    {
                        character.SelectedCharacter.Inventory.Update(deltaTime, cam);
                    }
                    character.SelectedCharacter.CharacterHealth.UpdateHUD(deltaTime);
                }

                Inventory.UpdateDragging();
            }

            if (focusedItem != null)
            {
                if (character.FocusedItem != null)
                {
                    focusedItemOverlayTimer = Math.Min(focusedItemOverlayTimer + deltaTime, ItemOverlayDelay + 1.0f);
                }
                else
                {
                    focusedItemOverlayTimer = Math.Max(focusedItemOverlayTimer - deltaTime, 0.0f);
                    if (focusedItemOverlayTimer <= 0.0f)
                    {
                        focusedItem = null;
                        RecreateHudTexts = true;
                    }
                }
            }

            if (brokenItemsCheckTimer > 0.0f)
            {
                brokenItemsCheckTimer -= deltaTime;
            }
            else
            {
                brokenItems.Clear();
                brokenItemsCheckTimer = 1.0f;
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.TeamID != character.TeamID || item.Submarine.Info.IsWreck) { continue; }
                    if (!item.Repairables.Any(r => r.IsBelowRepairIconThreshold)) { continue; }
                    if (Submarine.VisibleEntities != null && !Submarine.VisibleEntities.Contains(item)) { continue; }

                    Vector2 diff = item.WorldPosition - character.WorldPosition;
                    if (Submarine.CheckVisibility(character.SimPosition, character.SimPosition + ConvertUnits.ToSimUnits(diff)) == null)
                    {
                        brokenItems.Add(item); 
                    }                   
                }
            }

            if (CharacterHealth.OpenHealthWindow != null)
            {
                timeHealthWindowClosed = 0.0f;
            }
            else
            {
                timeHealthWindowClosed += deltaTime;
            }
        }
        
        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (GUI.DisableHUD) { return; }
            
            character.CharacterHealth.Alignment = Alignment.Right;           

            if (GameMain.GameSession?.CrewManager != null)
            {
                orderIndicatorCount.Clear();
                foreach (CrewManager.ActiveOrder activeOrder in GameMain.GameSession.CrewManager.ActiveOrders)
                {
                    if (!DrawIcon(activeOrder.Order)) { continue; }

                    if (activeOrder.FadeOutTime.HasValue)
                    {
                        DrawOrderIndicator(spriteBatch, cam, character, activeOrder.Order, iconAlpha: MathHelper.Clamp(activeOrder.FadeOutTime.Value / 10.0f, 0.2f, 1.0f));
                    }
                    else
                    {
                        float iconAlpha = GetDistanceBasedIconAlpha(activeOrder.Order.TargetSpatialEntity, maxDistance: 450.0f);
                        if (iconAlpha <= 0.0f) { continue; }
                        DrawOrderIndicator(spriteBatch, cam, character, activeOrder.Order,
                            iconAlpha: iconAlpha, createOffset: false, scaleMultiplier: 0.5f, overrideAlpha: true);
                    }
                }

                if (character.GetCurrentOrderWithTopPriority() is Order currentOrder && DrawIcon(currentOrder))
                {
                    DrawOrderIndicator(spriteBatch, cam, character, currentOrder, 1.0f);
                }

                static bool DrawIcon(Order o) =>
                    o != null &&
                    (!(o.TargetEntity is Item i) ||
                     o.DrawIconWhenContained ||
                     i.GetRootInventoryOwner() == i);
            }

            if (GameMain.GameSession != null)
            {
                foreach (var mission in GameMain.GameSession.Missions)
                {
                    if (!mission.DisplayTargetHudIcons) { continue; }
                    foreach (var target in mission.HudIconTargets)
                    {
                        if (target.Submarine != character.Submarine) { continue; }
                        float alpha = GetDistanceBasedIconAlpha(target, maxDistance: mission.Prefab.HudIconMaxDistance);
                        if (alpha <= 0.0f) { continue; }
                        GUI.DrawIndicator(spriteBatch, target.DrawPosition, cam, 100.0f, mission.Prefab.HudIcon, mission.Prefab.HudIconColor * alpha);
                    }
                }
            }

            foreach (Character.ObjectiveEntity objectiveEntity in character.ActiveObjectiveEntities)
            {
                DrawObjectiveIndicator(spriteBatch, cam, character, objectiveEntity, 1.0f);
            }

            foreach (Item brokenItem in brokenItems)
            {
                if (!brokenItem.IsInteractable(character)) { continue; }
                float alpha = GetDistanceBasedIconAlpha(brokenItem);
                if (alpha <= 0.0f) { continue; }
                GUI.DrawIndicator(spriteBatch, brokenItem.DrawPosition, cam, 100.0f, GUIStyle.BrokenIcon.Value.Sprite, 
                    Color.Lerp(GUIStyle.Red, GUIStyle.Orange * 0.5f, brokenItem.Condition / brokenItem.MaxCondition) * alpha);
            }

            float GetDistanceBasedIconAlpha(ISpatialEntity target, float maxDistance = 1000.0f)
            {
                float dist = Vector2.Distance(character.WorldPosition, target.WorldPosition);
                return Math.Min((maxDistance - dist) / maxDistance * 2.0f, 1.0f);
            }

            if (!character.IsIncapacitated && character.Stun <= 0.0f && !IsCampaignInterfaceOpen && (!character.IsKeyDown(InputType.Aim) || character.HeldItems.None(it => it?.GetComponent<Sprayer>() != null)))
            {
                if (character.FocusedCharacter != null && character.FocusedCharacter.CanBeSelected)
                {
                    DrawCharacterHoverTexts(spriteBatch, cam, character);
                }

                if (character.FocusedItem != null)
                {
                    if (focusedItem != character.FocusedItem)
                    {
                        focusedItemOverlayTimer = Math.Min(1.0f, focusedItemOverlayTimer);
                        RecreateHudTexts = true;
                    }
                    focusedItem = character.FocusedItem;
                }

                if (focusedItem != null && focusedItemOverlayTimer > ItemOverlayDelay)
                {
                    Vector2 circlePos = cam.WorldToScreen(focusedItem.DrawPosition);
                    float circleSize = Math.Max(focusedItem.Rect.Width, focusedItem.Rect.Height) * 1.5f;
                    circleSize = MathHelper.Clamp(circleSize, 45.0f, 100.0f) * Math.Min((focusedItemOverlayTimer - 1.0f) * 5.0f, 1.0f);
                    if (circleSize > 0.0f)
                    {
                        Vector2 scale = new Vector2(circleSize / GUIStyle.FocusIndicator.FrameSize.X);
                        GUIStyle.FocusIndicator.Draw(spriteBatch,
                            (int)((focusedItemOverlayTimer - 1.0f) * GUIStyle.FocusIndicator.FrameCount * 3.0f),
                            circlePos,
                            Color.LightBlue * 0.3f,
                            origin: GUIStyle.FocusIndicator.FrameSize.ToVector2() / 2,
                            rotate: (float)Timing.TotalTime,
                            scale: scale);
                    }

                    if (!GUI.DisableItemHighlights && !Inventory.DraggingItemToWorld)
                    {
                        bool hudTextsContextual = PlayerInput.IsShiftDown();
                        if (RecreateHudTexts || lastHudTextsContextual != hudTextsContextual)
                        {
                            RecreateHudTexts = true;
                            lastHudTextsContextual = hudTextsContextual;
                        }
                        var hudTexts = focusedItem.GetHUDTexts(character, RecreateHudTexts);
                        RecreateHudTexts = false;

                        int dir = Math.Sign(focusedItem.WorldPosition.X - character.WorldPosition.X);

                        Vector2 textSize = GUIStyle.Font.MeasureString(hudTexts.First().Text);
                        Vector2 largeTextSize = GUIStyle.SubHeadingFont.MeasureString(hudTexts.First().Text);

                        Vector2 startPos = cam.WorldToScreen(focusedItem.DrawPosition);
                        startPos.Y -= (hudTexts.Count + 1) * textSize.Y;
                        if (focusedItem.Sprite != null)
                        {
                            startPos.X += (int)(circleSize * 0.4f * dir);
                            startPos.Y -= (int)(circleSize * 0.4f);
                        }

                        Vector2 textPos = startPos;
                        if (dir == -1) { textPos.X -= largeTextSize.X; }

                        float alpha = MathHelper.Clamp((focusedItemOverlayTimer - ItemOverlayDelay) * 2.0f, 0.0f, 1.0f);

                        GUI.DrawString(spriteBatch, textPos, hudTexts.First().Text, hudTexts.First().Color * alpha, Color.Black * alpha * 0.7f, 2, font: GUIStyle.SubHeadingFont, ForceUpperCase.No);
                        startPos.X += dir * 10.0f * GUI.Scale;
                        textPos.X += dir * 10.0f * GUI.Scale;
                        textPos.Y += largeTextSize.Y;
                        foreach (ColoredText coloredText in hudTexts.Skip(1))
                        {
                            if (dir == -1) textPos.X = (int)(startPos.X - GUIStyle.SmallFont.MeasureString(coloredText.Text).X);
                            GUI.DrawString(spriteBatch, textPos, coloredText.Text, coloredText.Color * alpha, Color.Black * alpha * 0.7f, 2, GUIStyle.SmallFont);
                            textPos.Y += textSize.Y;
                        }
                    }                    
                }

                foreach (HUDProgressBar progressBar in character.HUDProgressBars.Values)
                {
                    progressBar.Draw(spriteBatch, cam);
                }

                void DrawInteractionIcon(Entity entity, Identifier iconStyle)
                {
                    if (entity == null || entity.Removed) { return; }

                    Hull currentHull = entity switch
                    {
                        Character character => character.CurrentHull,
                        Item item => item.CurrentHull,
                        _ => null
                    };
                    Range<float> visibleRange = new Range<float>(currentHull == Character.Controlled.CurrentHull ? 500.0f : 100.0f, float.PositiveInfinity);
                    LocalizedString label = null;
                    if (entity is Character characterEntity)
                    {
                        if (characterEntity.IsDead || characterEntity.IsIncapacitated) { return; }
                        if (characterEntity?.CampaignInteractionType == CampaignMode.InteractionType.Examine)
                        {
                            //TODO: we could probably do better than just hardcoding
                            //a check for InteractionType.Examine here.

                            if (Vector2.DistanceSquared(character.Position, entity.Position) > 500f * 500f) { return; }

                            var body = Submarine.CheckVisibility(character.SimPosition, entity.SimPosition, ignoreLevel: true);
                            if (body != null && body.UserData != entity) { return; }

                            visibleRange = new Range<float>(-100f, 500f);
                        }
                        label = characterEntity?.Info?.Title;
                    }

                    if (GUIStyle.GetComponentStyle(iconStyle) is not GUIComponentStyle style) { return; }

                    float dist = Vector2.Distance(character.WorldPosition, entity.WorldPosition);
                    float distFactor = 1.0f - MathUtils.InverseLerp(1000.0f, 3000.0f, dist);
                    float alpha = MathHelper.Lerp(0.3f, 1.0f, distFactor);
                    GUI.DrawIndicator(
                        spriteBatch,
                        entity.WorldPosition,
                        cam,
                        visibleRange,
                        style.GetDefaultSprite(),
                        style.Color * alpha,
                        label: label);
                }

                foreach (Character npc in Character.CharacterList)
                {
                    if (npc.CampaignInteractionType == CampaignMode.InteractionType.None) { continue; }
                    DrawInteractionIcon(npc, ("CampaignInteractionIcon." + npc.CampaignInteractionType).ToIdentifier());
                }

                if (GameMain.GameSession?.GameMode is TutorialMode tutorialMode && tutorialMode.Tutorial is not null)
                {
                    foreach (var (entity, iconStyle) in tutorialMode.Tutorial.Icons)
                    {
                        DrawInteractionIcon(entity, iconStyle);
                    }
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.IconStyle is null || item.Submarine != character.Submarine) { continue; }
                    if (Vector2.DistanceSquared(character.Position, item.Position) > 500f * 500f) { continue; }
                    var body = Submarine.CheckVisibility(character.SimPosition, item.SimPosition, ignoreLevel: true);
                    if (body != null && body.UserData as Item != item) { continue; }
                    GUI.DrawIndicator(spriteBatch, item.WorldPosition + new Vector2(0f, item.RectHeight * 0.65f), cam, new Range<float>(-100f, 500.0f), item.IconStyle.GetDefaultSprite(), item.IconStyle.Color, createOffset: false);
                }
            }

            if (character.SelectedItem != null && 
                (character.CanInteractWith(character.SelectedItem) || Screen.Selected == GameMain.SubEditorScreen))
            {
                character.SelectedItem.DrawHUD(spriteBatch, cam, character);
            }
            if (character.Inventory != null)
            {
                foreach (Item item in character.Inventory.AllItems)
                {
                    if (character.HasEquippedItem(item))
                    {
                        item.DrawHUD(spriteBatch, cam, character);
                    }
                }
            }

            if (IsCampaignInterfaceOpen) { return; }

            if (character.Inventory != null)
            {
                for (int i = 0; i < character.Inventory.Capacity; i++)
                {
                    var item = character.Inventory.GetItemAt(i);
                    if (item == null || character.Inventory.SlotTypes[i] == InvSlotType.Any) { continue; }
                    //if the item is also equipped in another slot we already went through, don't draw the hud again
                    bool duplicateFound = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (character.Inventory.SlotTypes[j] != InvSlotType.Any && character.Inventory.GetItemAt(j) == item)
                        {
                            duplicateFound = true;
                            break;
                        }
                    }
                    if (duplicateFound) { continue; }
                    foreach (ItemComponent ic in item.Components)
                    {
                        if (ic.DrawHudWhenEquipped) { ic.DrawHUD(spriteBatch, character); }
                    }
                }
            }

            bool mouseOnPortrait = false;
            if (character.Stun <= 0.1f && !character.IsDead)
            {
                bool wiringMode = Screen.Selected == GameMain.SubEditorScreen && GameMain.SubEditorScreen.WiringMode;
                if (CharacterHealth.OpenHealthWindow == null && character.SelectedCharacter == null && !wiringMode)
                {
                    if (character.Info != null && !character.ShouldLockHud())
                    {
                        character.Info.DrawBackground(spriteBatch);
                        character.Info.DrawJobIcon(spriteBatch,
                            new Rectangle(
                                (int)(HUDLayoutSettings.BottomRightInfoArea.X + HUDLayoutSettings.BottomRightInfoArea.Width * 0.05f),
                                (int)(HUDLayoutSettings.BottomRightInfoArea.Y + HUDLayoutSettings.BottomRightInfoArea.Height * 0.1f),
                                (int)(HUDLayoutSettings.BottomRightInfoArea.Width / 2),
                                (int)(HUDLayoutSettings.BottomRightInfoArea.Height * 0.7f)), character.Info.IsDisguisedAsAnother);
                        float yOffset = (GameMain.GameSession?.Campaign is MultiPlayerCampaign ? -10 : 4) * GUI.Scale;
                        character.Info.DrawPortrait(spriteBatch, HUDLayoutSettings.PortraitArea.Location.ToVector2(), new Vector2(-12 * GUI.Scale, yOffset), targetWidth: HUDLayoutSettings.PortraitArea.Width, true, character.Info.IsDisguisedAsAnother);
                        character.Info.DrawForeground(spriteBatch);
                    }
                    mouseOnPortrait = MouseOnCharacterPortrait() && !character.ShouldLockHud();
                    if (mouseOnPortrait)
                    {
                        GUIStyle.UIGlow.Draw(spriteBatch, HUDLayoutSettings.BottomRightInfoArea, GUIStyle.Green * 0.5f);
                    }
                }
                if (ShouldDrawInventory(character))
                {
                    character.Inventory.Locked = character == Character.Controlled && LockInventory(character);
                    character.Inventory.DrawOwn(spriteBatch);
                    character.Inventory.CurrentLayout = CharacterHealth.OpenHealthWindow == null && character.SelectedCharacter == null ?
                        CharacterInventory.Layout.Default :
                        CharacterInventory.Layout.Right;
                }
            }

            if (!character.IsIncapacitated && character.Stun <= 0.0f)
            {
                if (character.Params.CanInteract && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (character.SelectedCharacter.CanInventoryBeAccessed)
                    {
                        character.SelectedCharacter.Inventory.Locked = false;
                        character.SelectedCharacter.Inventory.CurrentLayout = CharacterInventory.Layout.Left;
                        character.SelectedCharacter.Inventory.DrawOwn(spriteBatch);
                    }
                    if (CharacterHealth.OpenHealthWindow == character.SelectedCharacter.CharacterHealth)
                    {
                        character.SelectedCharacter.CharacterHealth.Alignment = Alignment.Left;
                        //character.SelectedCharacter.CharacterHealth.DrawStatusHUD(spriteBatch);
                    }
                }
                else if (character.Inventory != null)
                {
                    //character.Inventory.CurrentLayout = (CharacterHealth.OpenHealthWindow == null) ? Alignment.Center : Alignment.Left;
                }
            }

            if (mouseOnPortrait)
            {
                GUIComponent.DrawToolTip(
                    spriteBatch,
                    character.Info?.Job == null ? character.DisplayName : character.DisplayName + " (" + character.Info.Job.Name + ")",
                    HUDLayoutSettings.PortraitArea);
            }
        }

        public static bool MouseOnCharacterPortrait()
        {
            if (Character.Controlled == null) { return false; }
            if (CharacterHealth.OpenHealthWindow != null || Character.Controlled.SelectedCharacter != null) { return false; }
            return HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition);
        }

        private static void DrawCharacterHoverTexts(SpriteBatch spriteBatch, Camera cam, Character character)
        {
            var allItems = character.Inventory?.AllItems;
            if (allItems != null)
            {
                foreach (Item item in allItems)
                {
                    var statusHUD = item?.GetComponent<StatusHUD>();
                    if (statusHUD != null && statusHUD.IsActive && statusHUD.VisibleCharacters.Contains(character.FocusedCharacter))
                    {
                        return;
                    }
                }
            }

            float dist = Vector2.Distance(character.FocusedCharacter.DrawPosition, character.DrawPosition);
            Vector2 startPos = character.DrawPosition + (character.FocusedCharacter.DrawPosition - character.DrawPosition) / dist * Math.Min(dist, Character.MaxDragDistance);
            startPos = cam.WorldToScreen(startPos);

            string focusName = character.FocusedCharacter.Info == null ? character.FocusedCharacter.DisplayName : character.FocusedCharacter.Info.DisplayName;
            Vector2 textPos = startPos;
            Vector2 textSize = GUIStyle.Font.MeasureString(focusName);
            Vector2 largeTextSize = GUIStyle.SubHeadingFont.MeasureString(focusName);

            textPos -= new Vector2(textSize.X / 2, textSize.Y);

            Color nameColor = character.FocusedCharacter.GetNameColor();
            GUI.DrawString(spriteBatch, textPos, focusName, nameColor, Color.Black * 0.7f, 2, GUIStyle.SubHeadingFont, ForceUpperCase.No);
            textPos.Y += GUIStyle.SubHeadingFont.MeasureString(focusName).Y;

            if (character.FocusedCharacter.Info?.Title != null && 
                !character.FocusedCharacter.Info.Title.IsNullOrEmpty() && 
                character.FocusedCharacter.TeamID != CharacterTeamType.Team1)
            {
                GUI.DrawString(spriteBatch, textPos, character.FocusedCharacter.Info.Title, nameColor, Color.Black * 0.7f, 2, GUIStyle.SubHeadingFont, ForceUpperCase.No);
                textPos.Y += GUIStyle.SubHeadingFont.MeasureString(character.FocusedCharacter.Info.Title.Value).Y;
            }
            textPos.X += 10.0f * GUI.Scale;

            if (!character.FocusedCharacter.IsIncapacitated && character.FocusedCharacter.IsPet)
            {
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText("PlayHint", InputType.Use),
                    GUIStyle.Green, Color.Black, 2, GUIStyle.SmallFont);
                textPos.Y += largeTextSize.Y;
            }

            if (character.FocusedCharacter.CanBeDragged)
            {
                string text = character.CanEat ? "EatHint" : "GrabHint";
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText(text, InputType.Grab),
                    GUIStyle.Green, Color.Black, 2, GUIStyle.SmallFont);
                textPos.Y += largeTextSize.Y;
            }

            if (!character.DisableHealthWindow &&
                character.IsFriendly(character.FocusedCharacter) && 
                character.FocusedCharacter.CharacterHealth.UseHealthWindow &&
                character.CanInteractWith(character.FocusedCharacter, 160f, false) &&
                !character.IsClimbing)
            {
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText("HealHint", InputType.Health),
                    GUIStyle.Green, Color.Black, 2, GUIStyle.SmallFont);
                textPos.Y += textSize.Y;
            }
            if (!character.FocusedCharacter.CustomInteractHUDText.IsNullOrEmpty() && character.FocusedCharacter.AllowCustomInteract)
            {
                GUI.DrawString(spriteBatch, textPos, character.FocusedCharacter.CustomInteractHUDText, GUIStyle.Green, Color.Black, 2, GUIStyle.SmallFont);
                textPos.Y += textSize.Y;
            }
        }

        public static void ShowBossHealthBar(Character character, float damage)
        {
            if (character == null || character.IsDead || character.Removed) { return; }
            if (bossProgressBars.Any(b => b.IsDuplicate(character))) { return; }
            AddBossProgressBar(new BossHealthBar(character));
        }

        public static void ShowMissionProgressBar(Mission mission)
        {
            if (mission == null || mission.Completed || mission.Failed) { return; }
            if (bossProgressBars.Any(b => b.IsDuplicate(mission))) { return; }
            AddBossProgressBar(new MissionProgressBar(mission));
        }

        public static void ClearBossProgressBars()
        {
            for (int i = bossProgressBars.Count - 1; i>= 0; i--)
            {
                RemoveBossProgressBar(bossProgressBars[i]);
            }
            bossProgressBars.Clear();
        }

        private static void RemoveBossProgressBar(BossProgressBar progressBar)
        {
            progressBar.SideContainer.Parent?.RemoveChild(progressBar.SideContainer);
            progressBar.TopContainer.Parent?.RemoveChild(progressBar.TopContainer);
            bossProgressBars.Remove(progressBar);
        }

        private static void AddBossProgressBar(BossProgressBar progressBar)
        {
            var healthBarMode = GameMain.NetworkMember?.ServerSettings.ShowEnemyHealthBars ?? GameSettings.CurrentConfig.ShowEnemyHealthBars;
            if (healthBarMode == EnemyHealthBarMode.HideAll)
            {
                return;
            }
            if (bossProgressBars.Count > 5)
            {
                BossProgressBar oldestHealthBar = bossProgressBars.First();
                foreach (var bar in bossProgressBars)
                {
                    if (bar.TopHealthBar.BarSize < oldestHealthBar.TopHealthBar.BarSize)
                    {
                        oldestHealthBar = bar;
                    }
                }
                oldestHealthBar.FadeTimer = Math.Min(oldestHealthBar.FadeTimer, 1.0f);
            }
            bossProgressBars.Add(progressBar);
        }

        public static void UpdateBossProgressBars(float deltaTime)
        {
            var healthBarMode = GameMain.NetworkMember?.ServerSettings.ShowEnemyHealthBars ?? GameSettings.CurrentConfig.ShowEnemyHealthBars;

            for (int i = 0; i < bossProgressBars.Count; i++)
            {
                var bossHealthBar = bossProgressBars[i];

                bool showTopBar = i == bossProgressBars.Count - 1;
                if (showTopBar && !bossHealthBar.TopContainer.Visible)
                {
                    bossHealthBar.SideContainer.SetAsLastChild();
                    SetColor(bossHealthBar, bossHealthBar.SideContainer, 0);
                }

                bossHealthBar.TopContainer.Visible = showTopBar;
                bossHealthBar.SideContainer.Visible = !bossHealthBar.TopContainer.Visible;

                bossHealthBar.TopHealthBar.BarSize = bossHealthBar.SideHealthBar.BarSize = bossHealthBar.State;
                float alpha = Math.Min(bossHealthBar.FadeTimer, 1.0f);

                if (bossHealthBar.TopContainer.Visible)
                {
                    SetColor(bossHealthBar, bossHealthBar.TopContainer, alpha);
                }
                if (bossHealthBar.SideContainer.Visible)
                {
                    SetColor(bossHealthBar, bossHealthBar.SideContainer, alpha);
                }

                static void SetColor(BossProgressBar bossHealthBar, GUIComponent container, float alpha)
                {
                    foreach (var component in container.GetAllChildren())
                    {
                        component.Color = new Color(bossHealthBar.Color, (byte)(alpha * 255));
                        if (component is GUITextBlock textBlock)
                        {
                            textBlock.TextColor = new Color(bossHealthBar.Completed ? Color.Gray : textBlock.TextColor, (byte)(alpha * 255));
                        }
                    }
                }

                if (bossHealthBar.Interrupted)
                {
                    bossHealthBar.FadeTimer = Math.Min(bossHealthBar.FadeTimer, 1.0f);
                }
                else if (bossHealthBar.Completed)
                {
                    bossHealthBar.FadeTimer = Math.Min(bossHealthBar.FadeTimer, 5.0f);
                }
                bossHealthBar.FadeTimer -= deltaTime;
            }
            for (int i = bossProgressBars.Count - 1; i >= 0 ; i--)
            {
                var bossHealthBar = bossProgressBars[i];
                if (bossHealthBar.FadeTimer <= 0 || healthBarMode == EnemyHealthBarMode.HideAll)
                {
                    bossHealthBar.SideContainer.Parent?.RemoveChild(bossHealthBar.SideContainer);
                    bossHealthBar.TopContainer.Parent?.RemoveChild(bossHealthBar.TopContainer);
                    bossProgressBars.RemoveAt(i);
                    bossHealthContainer.Recalculate();
                }
            }
        }

        private static bool LockInventory(Character character)
        {
            if (character?.Inventory == null || !character.AllowInput || character.LockHands || IsCampaignInterfaceOpen) { return true; }
            return character.ShouldLockHud();
        }

        /// <param name="overrideAlpha">Override the distance-based alpha value with the iconAlpha parameter value</param>
        private static void DrawOrderIndicator(SpriteBatch spriteBatch, Camera cam, Character character, Order order,
            float iconAlpha = 1.0f, bool createOffset = true, float scaleMultiplier = 1.0f, bool overrideAlpha = false)
        {
            if (order?.SymbolSprite == null) { return; }
            if (order.IsReport && order.OrderGiver != character && !order.HasAppropriateJob(character)) { return; }

            ISpatialEntity target = order.ConnectedController?.Item ?? order.TargetSpatialEntity;
            if (target == null) { return; }

            //don't show the indicator if far away and not inside the same sub
            //prevents exploiting the indicators in locating the sub
            if (character.Submarine != target.Submarine && 
                Vector2.DistanceSquared(character.WorldPosition, target.WorldPosition) > 1000.0f * 1000.0f)
            {
                return;
            }

            if (!orderIndicatorCount.ContainsKey(target)) { orderIndicatorCount.Add(target, 0); }

            Vector2 drawPos = target is Entity ? (target as Entity).DrawPosition :
                target.Submarine == null ? target.Position : target.Position + target.Submarine.DrawPosition;
            drawPos += Vector2.UnitX * order.SymbolSprite.size.X * 1.5f * orderIndicatorCount[target];
            GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, order.SymbolSprite, order.Color * iconAlpha,
                createOffset: createOffset, scaleMultiplier: scaleMultiplier, overrideAlpha: overrideAlpha ? (float?)iconAlpha : null);

            orderIndicatorCount[target] = orderIndicatorCount[target] + 1;
        }        

        private static void DrawObjectiveIndicator(SpriteBatch spriteBatch, Camera cam, Character character, Character.ObjectiveEntity objectiveEntity, float iconAlpha = 1.0f)
        {
            if (objectiveEntity == null) return;

            Vector2 drawPos = objectiveEntity.Entity.WorldPosition;// + Vector2.UnitX * objectiveEntity.Sprite.size.X * 1.5f;
            GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, objectiveEntity.Sprite, objectiveEntity.Color * iconAlpha);
        }

        static partial void RecreateHudTextsIfControllingProjSpecific(Character character)
        {
            if (character == Character.Controlled)
            {
                RecreateHudTexts = true;
            }
        }

        static partial void RecreateHudTextsIfFocusedProjSpecific(params Item[] items)
        {
            foreach (var item in items)
            {
                if (item == Character.Controlled?.FocusedItem)
                {
                    RecreateHudTexts = true;
                    break;
                }
            }
        }
    }
}
