using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private GUIListBox itemList;

        private GUIFrame selectedItemFrame;
        private GUIFrame selectedItemReqsFrame;

        private GUITextBlock amountTextMin, amountTextMax;
        private GUIScrollBar amountInput;

        public GUIButton ActivateButton
        {
            get { return activateButton; }
        }
        private GUIButton activateButton;

        private GUITextBox itemFilterBox;

        private GUIComponent outputSlot;
        private GUIComponent inputInventoryHolder, outputInventoryHolder;

        public FabricationRecipe SelectedItem
        {
            get { return selectedItem; }
        }
        private FabricationRecipe selectedItem;

        public Identifier SelectedItemIdentifier => SelectedItem?.TargetItem.Identifier ?? Identifier.Empty;

        private GUIComponent inSufficientPowerWarning;

        private FabricationRecipe pendingFabricatedItem;

        private class ToolTip
        {
            public Rectangle TargetElement;
            public LocalizedString Tooltip;
        }
        private ToolTip tooltip;

        private GUITextBlock requiredTimeBlock;

        [Serialize("FabricatorCreate", IsPropertySaveable.Yes)]
        public string CreateButtonText { get; set; }

        [Serialize("vendingmachine.outofstock", IsPropertySaveable.Yes)]
        public string FabricationLimitReachedText { get; set; }

        public override bool RecreateGUIOnResolutionChange => true;

        protected override void OnResolutionChanged()
        {
            if (GuiFrame != null)
            {
                InitInventoryUIs();
            }
        }

        protected override void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter);

            // === LABEL === //
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), paddedFrame.RectTransform), item.Name, font: GUIStyle.SubHeadingFont)
            {
                TextAlignment = Alignment.Center,
                AutoScaleVertical = true
            };

            var mainFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.95f), paddedFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.02f,
                Stretch = true,
                CanBeFocused = true
            };
            
            // === TOP AREA ===
            var topFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.65f), mainFrame.RectTransform), style: "InnerFrameDark");

                // === ITEM LIST ===
                var itemListFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), topFrame.RectTransform), childAnchor: Anchor.Center);
                    var paddedItemFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), itemListFrame.RectTransform))
                    {
                        Stretch = true, 
                        RelativeSpacing = 0.03f
                    };
                        var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedItemFrame.RectTransform), isHorizontal: true)
                        {
                            Stretch = true, 
                            RelativeSpacing = 0.03f, 
                            UserData = "filterarea"
                        };
                            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.SubHeadingFont)
                            {
                                Padding = Vector4.Zero, 
                                AutoScaleVertical = true
                            };
                            itemFilterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), createClearButton: true);
                            itemFilterBox.OnTextChanged += (textBox, text) =>
                            {
                                FilterEntities(text); 
                                return true;
                            };
                            filterArea.RectTransform.MaxSize = new Point(int.MaxValue, itemFilterBox.Rect.Height);

                        itemList = new GUIListBox(new RectTransform(new Vector2(1f, 0.9f), paddedItemFrame.RectTransform), style: null)
                        {
                            PlaySoundOnSelect = true,
                            OnSelected = (component, userdata) =>
                            {
                                selectedItem = userdata as FabricationRecipe;
                                if (selectedItem != null) { SelectItem(Character.Controlled, selectedItem); }
                                return true;
                            }
                        };

                // === SEPARATOR === //
                new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), topFrame.RectTransform, Anchor.Center), style: "VerticalLine");

                // === OUTPUT AREA === //
                var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), topFrame.RectTransform, Anchor.TopRight), childAnchor: Anchor.Center);
                    var paddedOutputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), outputArea.RectTransform));
                        var outputTopArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5F), paddedOutputArea.RectTransform, Anchor.Center), isHorizontal: true);
                            // === OUTPUT SLOT === //
                            outputSlot = new GUIFrame(new RectTransform(new Vector2(0.4f, 1f), outputTopArea.RectTransform), style: null);
                                outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.2f), outputSlot.RectTransform, Anchor.BottomCenter), style: null);
                                    new GUICustomComponent(new RectTransform(Vector2.One, outputInventoryHolder.RectTransform), DrawOutputOverLay) { CanBeFocused = false };
                            // === DESCRIPTION === //
                            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1f), outputTopArea.RectTransform), style: null);
                        // === REQUIREMENTS === //
                        selectedItemReqsFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedOutputArea.RectTransform), style: null);

            // === BOTTOM AREA === //
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.3f), mainFrame.RectTransform), style: null);

            if (inputContainer.Capacity > 0)
            {
                // === SEPARATOR === //
                var separatorArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), bottomFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.03f
                };
                var inputLabel = new GUITextBlock(new RectTransform(Vector2.One, separatorArea.RectTransform), TextManager.Get("fabricator.input", "uilabel.input"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                inputLabel.RectTransform.Resize(new Point((int)inputLabel.Font.MeasureString(inputLabel.Text).X, inputLabel.RectTransform.Rect.Height));
                new GUIFrame(new RectTransform(Vector2.One, separatorArea.RectTransform), style: "HorizontalLine");

                // === INPUT AREA === //
                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1f), bottomFrame.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomLeft);

                // === INPUT SLOTS === //
                inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), inputArea.RectTransform), style: null);
                new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawInputOverLay) { CanBeFocused = false };

                // === ACTIVATE BUTTON === //
                var buttonFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.9f), inputArea.RectTransform))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var amountInputHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), buttonFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true
                };

                amountTextMin = new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), amountInputHolder.RectTransform), "1", textAlignment: Alignment.Center);

                amountInput = new GUIScrollBar(new RectTransform(new Vector2(0.7f, 1.0f), amountInputHolder.RectTransform), barSize: 0.1f, style: "GUISlider")
                {
                    OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                    {
                        scrollBar.Step = 1.0f / Math.Max(scrollBar.Range.Y - 1, 1);
                        AmountToFabricate = (int)MathF.Round(scrollBar.BarScrollValue);
                        RefreshActivateButtonText();
                        if (GameMain.Client != null)
                        {
                            pendingFabricatedItem = null;
                            item.CreateClientEvent(this);
                        }
                        return true;
                    }
                };

                amountTextMax = new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), amountInputHolder.RectTransform), "1", textAlignment: Alignment.Center);

                activateButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.6f), buttonFrame.RectTransform),
                    TextManager.Get(CreateButtonText), style: "DeviceButton")
                {
                    OnClicked = StartButtonClicked,
                    UserData = selectedItem,
                    Enabled = false
                }; 

                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), buttonFrame.RectTransform), style: null);
            }
            else
            {
                bottomFrame.RectTransform.RelativeSize = new Vector2(1.0f, 0.1f);
                activateButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), bottomFrame.RectTransform, Anchor.CenterRight),
                    TextManager.Get(CreateButtonText), style: "DeviceButtonFixedSize")
                {
                    OnClicked = StartButtonClicked,
                    UserData = selectedItem,
                    Enabled = false
                };
            }
            // === POWER WARNING === //
            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform),
                TextManager.Get("FabricatorNoPower"), textColor: GUIStyle.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow", wrap: true)
            {
                HoverColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false,
                CanBeFocused = false
            };
            CreateRecipes();
        }

        private void RefreshActivateButtonText()
        {
            if (amountInput == null)
            {
                activateButton.Text = TextManager.Get(IsActive ? "FabricatorCancel" : CreateButtonText);
            }
            else
            {
                activateButton.Text =
                    IsActive ?
                    $"{TextManager.Get("FabricatorCancel")} ({amountRemaining})" :
                    $"{TextManager.Get(CreateButtonText)} ({AmountToFabricate})";
            }
        }

        partial void CreateRecipes()
        {
            itemList.Content.RectTransform.ClearChildren();

            foreach (FabricationRecipe fi in fabricationRecipes.Values)
            {
                var frame = new GUIFrame(new RectTransform(new Point(itemList.Content.Rect.Width, (int)(40 * GUI.yScale)), itemList.Content.RectTransform), style: null)
                {
                    UserData = fi,
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = fi.TargetItem.Description
                };
                
                var container = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform),
                    childAnchor: Anchor.CenterLeft, isHorizontal: true) { RelativeSpacing = 0.02f };

                var itemIcon = fi.TargetItem.InventoryIcon ?? fi.TargetItem.Sprite;
                if (itemIcon != null)
                {
                    new GUIImage(new RectTransform(new Point(frame.Rect.Height,frame.Rect.Height), container.RectTransform),
                        itemIcon, scaleToFit: true)
                    {
                        Color = fi.TargetItem.InventoryIconColor,
                        ToolTip = fi.TargetItem.Description
                    };
                }

                new GUITextBlock(new RectTransform(new Vector2(0.85f, 1f), container.RectTransform), GetRecipeNameAndAmount(fi))
                {
                    Padding = Vector4.Zero,
                    AutoScaleVertical = true,
                    ToolTip = fi.TargetItem.Description
                };

                new GUITextBlock(new RectTransform(new Vector2(0.85f, 1f), frame.RectTransform, Anchor.BottomRight), 
                    TextManager.Get(FabricationLimitReachedText), font: GUIStyle.SmallFont, textAlignment: Alignment.BottomRight)
                {
                    UserData = nameof(FabricationLimitReachedText),
                    Visible = false
                };
            }
        }

        private void InitInventoryUIs()
        {
            if (inputInventoryHolder != null)
            {
                inputContainer.AllowUIOverlap = true;
                inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            }
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;
        }

        private static LocalizedString GetRecipeNameAndAmount(FabricationRecipe fabricationRecipe)
        {
            if (fabricationRecipe == null) { return ""; }
            if (fabricationRecipe.Amount > 1)
            {
                return TextManager.GetWithVariables("fabricationrecipenamewithamount",
                    ("[name]", fabricationRecipe.DisplayName), ("[amount]", fabricationRecipe.Amount.ToString()));
            }
            else
            {
                return fabricationRecipe.DisplayName;
            }
        }

        partial void OnItemLoadedProjSpecific()
        {
            CreateGUI();
            InitInventoryUIs();
        }

        partial void SelectProjSpecific(Character character)
        {
            var nonItems = itemList.Content.Children.Where(c => c.UserData is not FabricationRecipe).ToList();
            nonItems.ForEach(i => itemList.Content.RemoveChild(i));

            itemList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var item1 = c1.GUIComponent.UserData as FabricationRecipe;
                var item2 = c2.GUIComponent.UserData as FabricationRecipe;

                int itemPlacement1 = calculatePlacement(item1);
                int itemPlacement2 = calculatePlacement(item2);
                if (itemPlacement1 != itemPlacement2)
                {
                    return itemPlacement1 > itemPlacement2 ? -1 : 1;
                }

                int calculatePlacement(FabricationRecipe recipe)
                {
                    if (recipe.RequiresRecipe && !AnyOneHasRecipeForItem(character, recipe.TargetItem))
                    {
                        return -2;
                    }
                    int placement = FabricationDegreeOfSuccess(character, recipe.RequiredSkills) >= 0.5f ? 0 : -1;
                    return placement;
                }

                return string.Compare(item1.DisplayName.Value, item2.DisplayName.Value);
            });

            var sufficientSkillsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                    TextManager.Get("fabricatorsufficientskills"), textColor: GUIStyle.Green, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };
            sufficientSkillsText.RectTransform.SetAsFirstChild();

            var insufficientSkillsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorinsufficientskills"), textColor: Color.Orange, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };
            var firstinSufficient = itemList.Content.Children.FirstOrDefault(c => c.UserData is FabricationRecipe fabricableItem && FabricationDegreeOfSuccess(character, fabricableItem.RequiredSkills) < 0.5f);
            if (firstinSufficient != null)
            {
                insufficientSkillsText.RectTransform.RepositionChildInHierarchy(itemList.Content.RectTransform.GetChildIndex(firstinSufficient.RectTransform));
            }
            else
            {
                sufficientSkillsText.Visible = insufficientSkillsText.Visible = false;
                sufficientSkillsText.Enabled = insufficientSkillsText.Enabled = false;
            }

            var requiresRecipeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorrequiresrecipe"), textColor: Color.Red, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };
            var firstRequiresRecipe = itemList.Content.Children.FirstOrDefault(c => 
                c.UserData is FabricationRecipe fabricableItem && 
                fabricableItem.RequiresRecipe && !AnyOneHasRecipeForItem(character, fabricableItem.TargetItem));
            if (firstRequiresRecipe != null)
            {
                requiresRecipeText.RectTransform.RepositionChildInHierarchy(itemList.Content.RectTransform.GetChildIndex(firstRequiresRecipe.RectTransform));
            }

            HideEmptyItemListCategories();

            if (selectedItem != null)
            {
                //reselect to recreate the info based on the new user's skills
                SelectItem(character, selectedItem);
            }
        }

        private readonly Dictionary<FabricationRecipe.RequiredItem, int> missingIngredientCounts = new Dictionary<FabricationRecipe.RequiredItem, int>();
        private float ingredientHighlightTimer;

        private void DrawInputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            missingIngredientCounts.Clear();

            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                foreach (FabricationRecipe.RequiredItem requiredItem in targetItem.RequiredItems)
                {
                    if (missingIngredientCounts.ContainsKey(requiredItem))
                    {
                        missingIngredientCounts[requiredItem] += requiredItem.Amount;
                    }
                    else
                    {
                        missingIngredientCounts[requiredItem] = requiredItem.Amount;
                    }
                }
                foreach (Item item in inputContainer.Inventory.AllItems)
                {
                    var missingIngredient = missingIngredientCounts.Keys.FirstOrDefault(mi => mi.MatchesItem(item));
                    if (missingIngredient == null) { continue; }

                    if (missingIngredientCounts[missingIngredient] == 1)
                    {
                        missingIngredientCounts.Remove(missingIngredient);
                    }
                    else
                    {
                        missingIngredientCounts[missingIngredient]--;
                    }
                }

                if (ingredientHighlightTimer <= 0.0f)
                {
                    //highlight inventory slots that contain suitable ingredients in linked inventories
                    foreach (var inventory in linkedInventories)
                    {
                        if (inventory.visualSlots == null) { continue; }
                        for (int i = 0; i < inventory.Capacity; i++)
                        {
                            if (inventory.visualSlots[i].HighlightTimer > 0.0f) { continue; }
                            var availableItem = inventory.GetItemAt(i);
                            if (availableItem == null) { continue; }

                            if (missingIngredientCounts.Keys.Any(it => it.MatchesItem(availableItem)))
                            {
                                inventory.visualSlots[i].ShowBorderHighlight(GUIStyle.Green, 0.5f, 0.5f, 0.2f);
                                continue;
                            }
                            if (availableItem.OwnInventory != null)
                            {
                                for (int j = 0; j < availableItem.OwnInventory.Capacity; j++)
                                {
                                    var availableContainedItem = availableItem.OwnInventory.GetItemAt(i);
                                    if (availableContainedItem == null) { continue; }
                                    if (missingIngredientCounts.Keys.Any(it => it.MatchesItem(availableContainedItem)))
                                    {
                                        inventory.visualSlots[i].ShowBorderHighlight(GUIStyle.Green, 0.5f, 0.5f, 0.2f);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    ingredientHighlightTimer = 1.0f;
                }

                int slotIndex = 0;
                foreach (var kvp in missingIngredientCounts)
                {
                    var requiredItem = kvp.Key;
                    int missingCount = kvp.Value;

                    while (slotIndex < inputContainer.Capacity && inputContainer.Inventory.GetItemAt(slotIndex) != null)
                    {
                        slotIndex++;
                    }

                    if (slotIndex >= inputContainer.Capacity) { break; }

                    if (slotIndex < inputContainer.Capacity && 
                        inputContainer.Inventory.visualSlots[slotIndex].HighlightTimer <= 0.0f &&
                        availableIngredients.Any(i => i.Value.Any() && requiredItem.MatchesItem(i.Value.First())))
                    {
                        inputContainer.Inventory.visualSlots[slotIndex].ShowBorderHighlight(GUIStyle.Green, 0.5f, 0.5f, 0.2f);
                    }

                    var requiredItemPrefab = requiredItem.FirstMatchingPrefab;
                    var itemIcon = requiredItemPrefab.InventoryIcon ?? requiredItemPrefab.Sprite;
                    Rectangle slotRect = inputContainer.Inventory.visualSlots[slotIndex].Rect;
                    itemIcon.Draw(
                        spriteBatch,
                        slotRect.Center.ToVector2(),
                        color: requiredItemPrefab.InventoryIconColor * 0.3f,
                        scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y));
                                        
                    if (missingCount > 1)
                    {
                        Vector2 stackCountPos = new Vector2(slotRect.Right, slotRect.Bottom);
                        string stackCountText = "x" + missingCount;
                        stackCountPos -= GUIStyle.SmallFont.MeasureString(stackCountText) + new Vector2(4, 2);
                        GUIStyle.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos + Vector2.One, Color.Black);
                        GUIStyle.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos, Color.White);
                    }

                    if (requiredItem.UseCondition && requiredItem.MinCondition < 1.0f)
                    {
                        DrawConditionBar(spriteBatch, requiredItem.MinCondition);
                    }
                    else if (requiredItem.MaxCondition < 1.0f)
                    {
                        DrawConditionBar(spriteBatch, requiredItem.MaxCondition);
                    }

                    void DrawConditionBar(SpriteBatch sb, float condition)
                    {
                        int spacing = GUI.IntScale(4);
                        int height = GUI.IntScale(10);
                        GUI.DrawRectangle(spriteBatch, new Rectangle(slotRect.X + spacing, slotRect.Bottom - spacing - height, slotRect.Width - spacing * 2, height), Color.Black * 0.8f, true);
                        GUI.DrawRectangle(spriteBatch,
                            new Rectangle(slotRect.X + spacing, slotRect.Bottom - spacing - height, (int)((slotRect.Width - spacing * 2) * condition), height),
                            GUIStyle.Green * 0.8f, true);
                    }

                    if (slotRect.Contains(PlayerInput.MousePosition))
                    {
                        var suitableIngredients = requiredItem.ItemPrefabs.Select(ip => ip.Name);
                        LocalizedString toolTipText = string.Join(", ", suitableIngredients.Count() > 3 ? suitableIngredients.SkipLast(suitableIngredients.Count() - 3) : suitableIngredients);
                        if (suitableIngredients.Count() > 3) { toolTipText += "..."; }
                        if (requiredItem.UseCondition && requiredItem.MinCondition < 1.0f)
                        {
                            toolTipText += " " + (int)Math.Round(requiredItem.MinCondition * 100) + "%";
                        }
                        else if (requiredItem.MaxCondition < 1.0f)
                        {
                            if (requiredItem.MaxCondition <= 0.0f)
                            {
                                toolTipText += " " + (int)Math.Round(requiredItem.MaxCondition * 100) + "%";
                            }
                            else
                            {
                                toolTipText += " 0-" + (int)Math.Round(requiredItem.MaxCondition * 100) + "%";
                            }
                        }
                        else if (requiredItem.MaxCondition <= 0.0f)
                        {
                            toolTipText = TextManager.GetWithVariable("displayname.emptyitem", "[itemname]", toolTipText);
                        }
                        if (!requiredItemPrefab.Description.IsNullOrEmpty())
                        {
                            toolTipText += '\n' + requiredItemPrefab.Description;
                        }
                        tooltip = new ToolTip { TargetElement = slotRect, Tooltip = toolTipText };
                    }

                    slotIndex++;
                }
            }
        }

        private void DrawOutputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                Rectangle slotRect = outputContainer.Inventory.visualSlots[0].Rect;

                if (fabricatedItem != null)
                {
                    float clampedProgressState = Math.Clamp(progressState, 0f, 1f);
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(
                            slotRect.X, slotRect.Y + (int)(slotRect.Height * (1.0f - clampedProgressState)),
                            slotRect.Width, (int)(slotRect.Height * clampedProgressState)),
                        GUIStyle.Green * 0.5f, isFilled: true);
                }

                if (outputContainer.Inventory.IsEmpty())
                {
                    var itemIcon = targetItem.TargetItem.InventoryIcon ?? targetItem.TargetItem.Sprite;
                    itemIcon.Draw(
                        spriteBatch,
                        slotRect.Center.ToVector2(),
                        color: targetItem.TargetItem.InventoryIconColor * 0.4f,
                        scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y) * 0.9f);
                }
            }
            
            if (tooltip != null)
            {
                GUIComponent.DrawToolTip(spriteBatch, tooltip.Tooltip, tooltip.TargetElement);
                tooltip = null;
            }
        }

        private bool FilterEntities(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                itemList.Content.Children.ForEach(c => c.Visible = true);
            }
            else
            {
                foreach (GUIComponent child in itemList.Content.Children)
                {
                    FabricationRecipe recipe = child.UserData as FabricationRecipe;
                    if (recipe?.DisplayName == null) { continue; }
                    child.Visible = recipe.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
                }
            }

            HideEmptyItemListCategories();

            return true;
        }

        private void HideEmptyItemListCategories()
        {
            //go through the elements backwards, and disable the labels ("insufficient skills to fabricate", "recipe required...") if there's no items below them
            bool recipeVisible = false;
            foreach (GUIComponent child in itemList.Content.Children.Reverse())
            {
                if (child.UserData is not FabricationRecipe recipe)
                {
                    if (child.Enabled)
                    {
                        child.Visible = recipeVisible;
                    }
                    recipeVisible = false;
                }
                else
                {
                    recipeVisible |= child.Visible;
                }
            }

            itemList.UpdateScrollBarSize();
            itemList.BarScroll = 0.0f;
        }

        public bool ClearFilter()
        {
            FilterEntities("");
            itemList.UpdateScrollBarSize();
            itemList.BarScroll = 0.0f;
            itemFilterBox.Text = "";
            return true;
        }

        private bool SelectItem(Character user, FabricationRecipe selectedItem, float? overrideRequiredTime = null)
        {
            this.selectedItem = selectedItem;

            int max = Math.Max(selectedItem.TargetItem.MaxStackSize / selectedItem.Amount, 1);

            if (amountInput != null)
            {
                float prevBarScroll = amountInput.BarScroll;
                amountInput.Range = new Vector2(1, max);
                amountInput.BarScroll = prevBarScroll;

                amountTextMax.Text = max.ToString();
                amountInput.Enabled = amountTextMax.Enabled = max > 1;
                AmountToFabricate = Math.Min((int)amountInput.BarScrollValue, max);
            }
            RefreshActivateButtonText();

            selectedItemFrame.ClearChildren();
            selectedItemReqsFrame.ClearChildren();

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };
            var paddedReqFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemReqsFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };

            LocalizedString itemName = GetRecipeNameAndAmount(selectedItem);
            LocalizedString name = itemName;

            float quality = selectedItem.Quality ?? GetFabricatedItemQuality(selectedItem, user);
            if (quality > 0)
            {
                name = TextManager.GetWithVariable("itemname.quality" + (int)quality, "[itemname]", itemName + '\n')
                    .Fallback(TextManager.GetWithVariable("itemname.quality3", "[itemname]", itemName + '\n'));
            }

            var nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
               RichString.Rich(name), textAlignment: Alignment.TopLeft, textColor: Color.Aqua, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };
            nameBlock.Padding = new Vector4(0, nameBlock.Padding.Y, GUI.IntScale(5), nameBlock.Padding.W);
            if (nameBlock.TextScale < 0.7f)
            {
                nameBlock.SetRichText(TextManager.GetWithVariable("itemname.quality" + (int)quality, "[itemname]", itemName)
                    .Fallback(TextManager.GetWithVariable("itemname.quality3", "[itemname]", itemName)));
                nameBlock.AutoScaleHorizontal = false;
                nameBlock.TextScale = 0.7f;
                nameBlock.Wrap = true;
                nameBlock.SetTextPos();
                nameBlock.RectTransform.MinSize = new Point(0, (int)(nameBlock.TextSize.Y * nameBlock.TextScale));
            }            
            
            if (!selectedItem.TargetItem.Description.IsNullOrEmpty())
            {
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    selectedItem.TargetItem.Description,
                    font: GUIStyle.SmallFont, wrap: true);
                description.Padding = new Vector4(0, description.Padding.Y, description.Padding.Z, description.Padding.W);
            
                while (description.Rect.Height + nameBlock.Rect.Height > paddedFrame.Rect.Height)
                {
                    var lines = description.WrappedText.Split('\n');
                    if (lines.Count <= 1) { break; }
                    var newString = string.Join('\n', lines.Take(lines.Count - 1));
                    description.Text = newString.Substring(0, newString.Length - 4) + "...";
                    description.CalculateHeightFromText();
                    description.ToolTip = selectedItem.TargetItem.Description;
                }
            }
            
            IEnumerable<Skill> inadequateSkills = Enumerable.Empty<Skill>();
            if (user != null)
            {
                inadequateSkills = selectedItem.RequiredSkills.Where(skill => user.GetSkillLevel(skill.Identifier) < Math.Round(skill.Level * SkillRequirementMultiplier));
            }
            
            if (selectedItem.RequiredSkills.Any())
            {
                LocalizedString text = "";
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), 
                    TextManager.Get("FabricatorRequiredSkills"), textColor: inadequateSkills.Any() ? GUIStyle.Red : GUIStyle.Green, font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
                foreach (Skill skill in selectedItem.RequiredSkills)
                {
                    text += TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + Math.Round(skill.Level * SkillRequirementMultiplier);
                    if (skill != selectedItem.RequiredSkills.Last()) { text += "\n"; }
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), text, font: GUIStyle.SmallFont);
            }

            float degreeOfSuccess = user == null ? 0.0f : FabricationDegreeOfSuccess(user, selectedItem.RequiredSkills);
            if (degreeOfSuccess > 0.5f) { degreeOfSuccess = 1.0f; }

            float requiredTime = overrideRequiredTime ??
                (user == null ? selectedItem.RequiredTime : GetRequiredTime(selectedItem, user));
            
            if ((int)requiredTime > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), 
                    TextManager.Get("FabricatorRequiredTime") , textColor: ToolBox.GradientLerp(degreeOfSuccess, GUIStyle.Red, Color.Yellow, GUIStyle.Green), font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
                requiredTimeBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), ToolBox.SecondsToReadableTime(requiredTime), 
                    font: GUIStyle.SmallFont);
            }

            if (SelectedItem.RequiredMoney > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform),
                    TextManager.Get("subeditor.price"), textColor: ToolBox.GradientLerp(degreeOfSuccess, GUIStyle.Red, Color.Yellow, GUIStyle.Green), font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), TextManager.FormatCurrency(SelectedItem.RequiredMoney),
                    font: GUIStyle.SmallFont);

            }
            return true;
        }

        public void HighlightRecipe(string identifier, Color color)
        {
            foreach (GUIComponent child in itemList.Content.Children)
            {
                FabricationRecipe recipe = child.UserData as FabricationRecipe;
                if (recipe?.DisplayName == null) { continue; }
                if (recipe.TargetItem.Identifier == identifier)
                {
                    if (child.FlashTimer > 0.0f) return;
                    child.Flash(color, 1.5f, false);

                    for (int i = 0; i < child.CountChildren; i++)
                    {
                        var grandChild = child.GetChild(i);
                        if (grandChild is GUITextBlock) continue;
                        grandChild.Flash(color, 1.5f, false);
                    }

                    return;
                }
            }
        }
        
        private bool StartButtonClicked(GUIButton button, object obj)
        {
            if (selectedItem == null) { return false; }
            if (fabricatedItem == null && 
                !outputContainer.Inventory.CanBePut(selectedItem.TargetItem, selectedItem.OutCondition * selectedItem.TargetItem.Health))
            {
                outputSlot.Flash(GUIStyle.Red);
                return false;
            }

            amountRemaining = AmountToFabricate;

            if (GameMain.Client != null)
            {
                pendingFabricatedItem = fabricatedItem != null ? null : selectedItem;
                item.CreateClientEvent(this);
            }
            else
            {
                if (fabricatedItem == null)
                {
                    StartFabricating(selectedItem, Character.Controlled);
                }
                else
                {
                    CancelFabricating(Character.Controlled);
                }
            }

            return true;
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            activateButton.Enabled = false;
            inSufficientPowerWarning.Visible = IsActive && !hasPower;

            ingredientHighlightTimer -= deltaTime;

            if (!IsActive)
            {
                //only check ingredients if the fabricator isn't active (if it is, this is done in Update)
                if (refreshIngredientsTimer <= 0.0f)
                {
                    RefreshAvailableIngredients();
                    refreshIngredientsTimer = RefreshIngredientsInterval;
                }
                refreshIngredientsTimer -= deltaTime;
            }

            if (character != null)
            {
                foreach (GUIComponent child in itemList.Content.Children)
                {
                    if (child.UserData is not FabricationRecipe recipe) { continue; }

                    if (recipe != selectedItem &&
                        (child.Rect.Y > itemList.Rect.Bottom || child.Rect.Bottom < itemList.Rect.Y))
                    {
                        continue;
                    }

                    bool canBeFabricated = CanBeFabricated(recipe, availableIngredients, character);
                    if (recipe == selectedItem)
                    {
                        activateButton.Enabled = canBeFabricated;
                    }

                    var childContainer = child.GetChild<GUILayoutGroup>();
                    childContainer.GetChild<GUITextBlock>().TextColor = Color.White * (canBeFabricated ? 1.0f : 0.5f);
                    childContainer.GetChild<GUIImage>().Color = recipe.TargetItem.InventoryIconColor * (canBeFabricated ? 1.0f : 0.5f);

                    var limitReachedText = child.FindChild(nameof(FabricationLimitReachedText));
                    limitReachedText.Visible = !canBeFabricated && fabricationLimits.TryGetValue(recipe.RecipeHash, out int amount) && amount <= 0;
                }
            }
        }

        partial void UpdateRequiredTimeProjSpecific()
        {
            if (requiredTimeBlock == null) { return; }
            requiredTimeBlock.Text = ToolBox.SecondsToReadableTime(timeUntilReady > 0.0f ? timeUntilReady : requiredTime);
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            uint recipeHash = pendingFabricatedItem?.RecipeHash ?? 0;
            msg.WriteUInt32(recipeHash);
            msg.WriteRangedInteger(AmountToFabricate, 1, MaxAmountToFabricate);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            FabricatorState newState = (FabricatorState)msg.ReadByte();
            int amountToFabricate = msg.ReadRangedInteger(0, MaxAmountToFabricate);
            int amountRemaining = msg.ReadRangedInteger(0, MaxAmountToFabricate);
            float newTimeUntilReady = msg.ReadSingle();
            uint recipeHash = msg.ReadUInt32();
            UInt16 userID = msg.ReadUInt16();
            Character user = Entity.FindEntityByID(userID) as Character;

            ushort reachedLimitCount = msg.ReadUInt16();
            for (int i = 0; i < reachedLimitCount; i++)
            {
                fabricationLimits[msg.ReadUInt32()] = 0;
            }

            State = newState;
            this.amountToFabricate = amountToFabricate;
            this.amountRemaining = amountRemaining;
            if (newState == FabricatorState.Stopped || recipeHash == 0)
            {
                CancelFabricating();
            }
            else if (newState == FabricatorState.Active || newState == FabricatorState.Paused)
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricatedItem.RecipeHash == recipeHash) { return; }
                if (recipeHash == 0) { return; }

                SelectItem(user, fabricationRecipes[recipeHash]);
                StartFabricating(fabricationRecipes[recipeHash], user);
            }
            timeUntilReady = newTimeUntilReady;
        }
    }
}
