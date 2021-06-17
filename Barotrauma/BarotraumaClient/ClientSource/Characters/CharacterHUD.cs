using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class CharacterHUD
    {        
        const float BossHealthBarDuration = 120.0f;

        class BossHealthBar
        {
            public readonly Character Character;
            public float FadeTimer;

            public readonly GUIComponent TopContainer;
            public readonly GUIComponent SideContainer;

            public readonly GUIProgressBar TopHealthBar;
            public readonly GUIProgressBar SideHealthBar;

            public BossHealthBar(Character character)
            {
                Character = character;
                FadeTimer = BossHealthBarDuration;

                TopContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.18f, 0.03f), HUDFrame.RectTransform, Anchor.TopCenter)
                {
                    MinSize = new Point(100, 50),
                    RelativeOffset = new Vector2(0.0f, 0.01f)
                }, isHorizontal: false, childAnchor: Anchor.TopCenter);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), TopContainer.RectTransform), character.DisplayName, textAlignment: Alignment.Center, textColor: GUI.Style.Red);
                TopHealthBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.6f), TopContainer.RectTransform)
                {
                    MinSize = new Point(100, HUDLayoutSettings.HealthBarArea.Size.Y)
                }, barSize: 0.0f, style: "CharacterHealthBarCentered")
                {
                    Color = GUI.Style.Red
                };

                SideContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), bossHealthContainer.RectTransform)
                {
                    MinSize = new Point(80, 60)
                }, isHorizontal: false, childAnchor: Anchor.TopRight);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), SideContainer.RectTransform), character.DisplayName, textAlignment: Alignment.CenterRight, textColor: GUI.Style.Red);
                SideHealthBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.7f), SideContainer.RectTransform), barSize: 0.0f, style: "CharacterHealthBar")
                {
                    Color = GUI.Style.Red
                };

                TopContainer.Visible = SideContainer.Visible = false;
                TopContainer.CanBeFocused = false;
                TopContainer.Children.ForEach(c => c.CanBeFocused = false);
                SideContainer.CanBeFocused = false;
                SideContainer.Children.ForEach(c => c.CanBeFocused = false);
            }
        }

        private static readonly Dictionary<ISpatialEntity, int> orderIndicatorCount = new Dictionary<ISpatialEntity, int>();
        const float ItemOverlayDelay = 1.0f;
        private static Item focusedItem;
        private static float focusedItemOverlayTimer;
        
        private static readonly List<Item> brokenItems = new List<Item>();
        private static float brokenItemsCheckTimer;

        private static readonly List<BossHealthBar> bossHealthBars = new List<BossHealthBar>();

        private static readonly Dictionary<string, string> cachedHudTexts = new Dictionary<string, string>();

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

        private static bool shouldRecreateHudTexts = true;
        private static bool heldDownShiftWhenGotHudTexts;

        public static bool IsCampaignInterfaceOpen =>
            GameMain.GameSession?.Campaign != null && 
            (GameMain.GameSession.Campaign.ShowCampaignUI || GameMain.GameSession.Campaign.ForceMapUI);

        private static bool ShouldDrawInventory(Character character)
        {
            var controller = character.SelectedConstruction?.GetComponent<Controller>();

            return 
                character?.Inventory != null && 
                character.AllowInput &&
                (controller?.User != character || !controller.HideHUD) &&
                !IsCampaignInterfaceOpen &&
                !ConversationAction.FadeScreenToBlack;
        }

        public static string GetCachedHudText(string textTag, string keyBind)
        {
            if (cachedHudTexts.TryGetValue(textTag + keyBind, out string text))
            {
                return text;
            }
            text = TextManager.GetWithVariable(textTag, "[key]", keyBind);
            cachedHudTexts.Add(textTag + keyBind, text);
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

                if (character.IsHumanoid && character.SelectedCharacter != null)
                {
                    character.SelectedCharacter.CharacterHealth.AddToGUIUpdateList();
                }
            }

            HUDFrame.AddToGUIUpdateList();
        }

        public static void Update(float deltaTime, Character character, Camera cam)
        {
            UpdateBossHealthBars(deltaTime);

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
                if (character.Info != null && !character.ShouldLockHud() && character.SelectedCharacter == null)
                {
                    bool mouseOnPortrait = HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition) && GUI.MouseOn == null;
                    if (mouseOnPortrait && PlayerInput.PrimaryMouseButtonClicked())
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

                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
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
                        shouldRecreateHudTexts = true;
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
                    if (!item.Repairables.Any(r => item.ConditionPercentage <= r.RepairIconThreshold)) { continue; }
                    if (Submarine.VisibleEntities != null && !Submarine.VisibleEntities.Contains(item)) { continue; }

                    Vector2 diff = item.WorldPosition - character.WorldPosition;
                    if (Submarine.CheckVisibility(character.SimPosition, character.SimPosition + ConvertUnits.ToSimUnits(diff)) == null)
                    {
                        brokenItems.Add(item); 
                    }                   
                }
            }
        }
        
        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (GUI.DisableHUD) { return; }
            
            character.CharacterHealth.Alignment = Alignment.Right;           

            if (GameMain.GameSession?.CrewManager != null)
            {
                orderIndicatorCount.Clear();
                foreach (Pair<Order, float?> activeOrder in GameMain.GameSession.CrewManager.ActiveOrders)
                {
                    if (!DrawIcon(activeOrder.First)) { continue; }

                    if (activeOrder.Second.HasValue)
                    {
                        DrawOrderIndicator(spriteBatch, cam, character, activeOrder.First, iconAlpha: MathHelper.Clamp(activeOrder.Second.Value / 10.0f, 0.2f, 1.0f));
                    }
                    else
                    {
                        float iconAlpha = GetDistanceBasedIconAlpha(activeOrder.First.TargetSpatialEntity, maxDistance: 450.0f);
                        if (iconAlpha <= 0.0f) { continue; }
                        DrawOrderIndicator(spriteBatch, cam, character, activeOrder.First,
                            iconAlpha: iconAlpha, createOffset: false, scaleMultiplier: 0.5f, overrideAlpha: true);
                    }
                }

                if (character.GetCurrentOrderWithTopPriority()?.Order is Order currentOrder && DrawIcon(currentOrder))
                {
                    DrawOrderIndicator(spriteBatch, cam, character, currentOrder, 1.0f);
                }

                static bool DrawIcon(Order o) =>
                    o != null &&
                    (!(o.TargetEntity is Item i) ||
                     o.DrawIconWhenContained ||
                     i.GetRootInventoryOwner() == i);
            }

            foreach (Character.ObjectiveEntity objectiveEntity in character.ActiveObjectiveEntities)
            {
                DrawObjectiveIndicator(spriteBatch, cam, character, objectiveEntity, 1.0f);
            }

            foreach (Item brokenItem in brokenItems)
            {
                if (!brokenItem.IsInteractable(character)) { continue; }
                float alpha = GetDistanceBasedIconAlpha(brokenItem);
                if (alpha <= 0.0f) continue;
                GUI.DrawIndicator(spriteBatch, brokenItem.DrawPosition, cam, 100.0f, GUI.BrokenIcon, 
                    Color.Lerp(GUI.Style.Red, GUI.Style.Orange * 0.5f, brokenItem.Condition / brokenItem.MaxCondition) * alpha);
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
                        shouldRecreateHudTexts = true;
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
                        Vector2 scale = new Vector2(circleSize / GUI.Style.FocusIndicator.FrameSize.X);
                        GUI.Style.FocusIndicator.Draw(spriteBatch,
                            (int)((focusedItemOverlayTimer - 1.0f) * GUI.Style.FocusIndicator.FrameCount * 3.0f),
                            circlePos,
                            Color.LightBlue * 0.3f,
                            origin: GUI.Style.FocusIndicator.FrameSize.ToVector2() / 2,
                            rotate: (float)Timing.TotalTime,
                            scale: scale);
                    }

                    if (!GUI.DisableItemHighlights && !Inventory.DraggingItemToWorld)
                    {
                        bool shiftDown = PlayerInput.IsShiftDown();
                        if (shouldRecreateHudTexts || heldDownShiftWhenGotHudTexts != shiftDown)
                        {
                            shouldRecreateHudTexts = true;
                            heldDownShiftWhenGotHudTexts = shiftDown;
                        }
                        var hudTexts = focusedItem.GetHUDTexts(character, shouldRecreateHudTexts);
                        shouldRecreateHudTexts = false;

                        int dir = Math.Sign(focusedItem.WorldPosition.X - character.WorldPosition.X);

                        Vector2 textSize = GUI.Font.MeasureString(hudTexts.First().Text);
                        Vector2 largeTextSize = GUI.SubHeadingFont.MeasureString(hudTexts.First().Text);

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

                        GUI.DrawString(spriteBatch, textPos, hudTexts.First().Text, hudTexts.First().Color * alpha, Color.Black * alpha * 0.7f, 2, font: GUI.SubHeadingFont);
                        startPos.X += dir * 10.0f * GUI.Scale;
                        textPos.X += dir * 10.0f * GUI.Scale;
                        textPos.Y += largeTextSize.Y;
                        foreach (ColoredText coloredText in hudTexts.Skip(1))
                        {
                            if (dir == -1) textPos.X = (int)(startPos.X - GUI.SmallFont.MeasureString(coloredText.Text).X);
                            GUI.DrawString(spriteBatch, textPos, coloredText.Text, coloredText.Color * alpha, Color.Black * alpha * 0.7f, 2, GUI.SmallFont);
                            textPos.Y += textSize.Y;
                        }
                    }                    
                }

                foreach (HUDProgressBar progressBar in character.HUDProgressBars.Values)
                {
                    progressBar.Draw(spriteBatch, cam);
                }

                foreach (Character npc in Character.CharacterList)
                {
                    if (npc.CampaignInteractionType == CampaignMode.InteractionType.None || npc.Submarine != character.Submarine || npc.IsDead || npc.IsIncapacitated) { continue; }

                    var iconStyle = GUI.Style.GetComponentStyle("CampaignInteractionIcon." + npc.CampaignInteractionType);
                    GUI.DrawIndicator(spriteBatch, npc.WorldPosition, cam, npc.CurrentHull == Character.Controlled.CurrentHull ? 500.0f : 100.0f, iconStyle.GetDefaultSprite(), iconStyle.Color);
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.IconStyle is null || item.Submarine != character.Submarine) { continue; }
                    if (Vector2.DistanceSquared(character.Position, item.Position) > 500f*500f) { continue; }
                    var body = Submarine.CheckVisibility(character.SimPosition, item.SimPosition, ignoreLevel: true);
                    if (body != null && body.UserData as Item != item) { continue; }
                    GUI.DrawIndicator(spriteBatch, item.WorldPosition + new Vector2(0f, item.RectHeight * 0.65f), cam, new Vector2(-100f, 500.0f), item.IconStyle.GetDefaultSprite(), item.IconStyle.Color, createOffset: false);
                }
            }

            if (character.SelectedConstruction != null && 
                (character.CanInteractWith(Character.Controlled.SelectedConstruction) || Screen.Selected == GameMain.SubEditorScreen))
            {
                character.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Controlled);
            }
            if (Character.Controlled.Inventory != null)
            {
                foreach (Item item in Character.Controlled.Inventory.AllItems)
                {
                    if (Character.Controlled.HasEquippedItem(item))
                    {
                        item.DrawHUD(spriteBatch, cam, Character.Controlled);
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

                    foreach (ItemComponent ic in item.Components)
                    {
                        if (ic.DrawHudWhenEquipped) { ic.DrawHUD(spriteBatch, character); }
                    }
                }
            }

            bool mouseOnPortrait = false;
            if (character.Stun <= 0.1f && !character.IsDead)
            {
                if (CharacterHealth.OpenHealthWindow == null && character.SelectedCharacter == null)
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
                        character.Info.DrawPortrait(spriteBatch, HUDLayoutSettings.PortraitArea.Location.ToVector2(), new Vector2(-12 * GUI.Scale, 4 * GUI.Scale), targetWidth: HUDLayoutSettings.PortraitArea.Width, true, character.Info.IsDisguisedAsAnother);
                    }
                    mouseOnPortrait = HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition) && !character.ShouldLockHud();
                    if (mouseOnPortrait)
                    {
                        GUI.UIGlow.Draw(spriteBatch, HUDLayoutSettings.BottomRightInfoArea, GUI.Style.Green * 0.5f);
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
                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
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
                        character.SelectedCharacter.CharacterHealth.DrawStatusHUD(spriteBatch);
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

            Vector2 startPos = character.DrawPosition + (character.FocusedCharacter.DrawPosition - character.DrawPosition) * 0.7f;
            startPos = cam.WorldToScreen(startPos);

            string focusName = character.FocusedCharacter.Info == null ? character.FocusedCharacter.DisplayName : character.FocusedCharacter.Info.DisplayName;
            Vector2 textPos = startPos;
            Vector2 textSize = GUI.Font.MeasureString(focusName);
            Vector2 largeTextSize = GUI.SubHeadingFont.MeasureString(focusName);

            textPos -= new Vector2(textSize.X / 2, textSize.Y);

            Color nameColor = GUI.Style.TextColor;
            if (character.TeamID != character.FocusedCharacter.TeamID)
            {
                nameColor = character.FocusedCharacter.TeamID == CharacterTeamType.FriendlyNPC ? Color.SkyBlue : GUI.Style.Red;
            }

            GUI.DrawString(spriteBatch, textPos, focusName, nameColor, Color.Black * 0.7f, 2, GUI.SubHeadingFont);
            textPos.X += 10.0f * GUI.Scale;
            textPos.Y += GUI.SubHeadingFont.MeasureString(focusName).Y;

            if (!character.FocusedCharacter.IsIncapacitated && character.FocusedCharacter.IsPet)
            {
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText("PlayHint", GameMain.Config.KeyBindText(InputType.Use)),
                    GUI.Style.Green, Color.Black, 2, GUI.SmallFont);
                textPos.Y += largeTextSize.Y;
            }

            if (character.FocusedCharacter.CanBeDragged)
            {
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText("GrabHint", GameMain.Config.KeyBindText(InputType.Grab)),
                    GUI.Style.Green, Color.Black, 2, GUI.SmallFont);
                textPos.Y += largeTextSize.Y;
            }
            if (character.FocusedCharacter.CharacterHealth.UseHealthWindow && character.CanInteractWith(character.FocusedCharacter, 160f, false))
            {
                GUI.DrawString(spriteBatch, textPos, GetCachedHudText("HealHint", GameMain.Config.KeyBindText(InputType.Health)),
                    GUI.Style.Green, Color.Black, 2, GUI.SmallFont);
                textPos.Y += textSize.Y;
            }
            if (!string.IsNullOrEmpty(character.FocusedCharacter.customInteractHUDText) && character.FocusedCharacter.AllowCustomInteract)
            {
                GUI.DrawString(spriteBatch, textPos, character.FocusedCharacter.customInteractHUDText, GUI.Style.Green, Color.Black, 2, GUI.SmallFont);
                textPos.Y += textSize.Y;
            }
        }

        public static void ShowBossHealthBar(Character character)
        {
            if (character == null || character.IsDead || character.Removed) { return; }

            var existingBar = bossHealthBars.Find(b => b.Character == character);
            if (existingBar != null)
            {
                existingBar.FadeTimer = BossHealthBarDuration;
                return;
            }

            if (bossHealthBars.Count > 5)
            {
                BossHealthBar oldestHealthBar = bossHealthBars.First();
                foreach (var bar in bossHealthBars)
                {
                    if (bar.TopHealthBar.BarSize < oldestHealthBar.TopHealthBar.BarSize)
                    {
                        oldestHealthBar = bar;
                    }
                }
                oldestHealthBar.FadeTimer = Math.Min(oldestHealthBar.FadeTimer, 1.0f);
            }

            bossHealthBars.Add(new BossHealthBar(character));
        }

        public static void UpdateBossHealthBars(float deltaTime)
        {
            for (int i = 0; i < bossHealthBars.Count; i++)
            {
                var bossHealthBar = bossHealthBars[i];

                bool showTopBar = i == 0;
                if (showTopBar != bossHealthBar.TopContainer.Visible)
                {
                    bossHealthContainer.Recalculate();
                }

                bossHealthBar.TopContainer.Visible = showTopBar;
                bossHealthBar.SideContainer.Visible = !bossHealthBar.TopContainer.Visible;

                float health =  bossHealthBar.Character.Vitality / bossHealthBar.Character.MaxVitality;

                float alpha = Math.Min(bossHealthBar.FadeTimer, 1.0f);
                foreach (var c in bossHealthBar.SideContainer.GetAllChildren().Concat(bossHealthBar.TopContainer.GetAllChildren()))
                {
                    c.Color = new Color(c.Color, (byte)(alpha * 255));
                    if (c is GUITextBlock textBlock)
                    {
                        textBlock.TextColor = new Color(bossHealthBar.Character.IsDead ? Color.Gray : textBlock.TextColor, (byte)(alpha * 255));
                    }
                }

                bossHealthBar.TopHealthBar.BarSize = bossHealthBar.SideHealthBar.BarSize = health;

                if (bossHealthBar.Character.Removed || !bossHealthBar.Character.Enabled)
                {
                    bossHealthBar.FadeTimer = Math.Min(bossHealthBar.FadeTimer, 1.0f);
                }
                else if (bossHealthBar.Character.IsDead)
                {
                    bossHealthBar.FadeTimer = Math.Min(bossHealthBar.FadeTimer, 5.0f);
                }
                bossHealthBar.FadeTimer -= deltaTime;
            }

            for (int i = bossHealthBars.Count - 1; i >= 0 ; i--)
            {
                var bossHealthBar = bossHealthBars[i];
                if (bossHealthBar.FadeTimer <= 0)
                {
                    bossHealthBar.SideContainer.Parent?.RemoveChild(bossHealthBar.SideContainer);
                    bossHealthBar.TopContainer.Parent?.RemoveChild(bossHealthBar.TopContainer);
                    bossHealthBars.RemoveAt(i);
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
    }
}
