using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private enum SortBy
        {
            Category,
            Alphabetical,
            SkillRequirement,
            Price
        }

        private GUIListBox itemList;

        private GUIFrame selectedItemFrame;
        private GUIFrame selectedItemReqsFrame;

        private GUILayoutGroup outputTopArea, paddedOutputArea;

        private GUITextBlock amountTextMax;
        private GUIScrollBar amountInput;

        public GUIButton ActivateButton
        {
            get { return activateButton; }
        }
        private GUIButton activateButton;

        private GUITextBox itemFilterBox;
        private GUITickBox availableOnlyTickBox;
        private GUIDropDown sortByDropdown;

        private GUIComponent outputSlot;
        private GUIComponent inputInventoryHolder, outputInventoryHolder;

        private readonly List<GUIButton> itemCategoryButtons = new List<GUIButton>();
        private MapEntityCategory? selectedItemCategory;

        private GUITextBlock requiresRecipeText;
        private GUITextBlock nothingToShowText;

        public FabricationRecipe SelectedItem
        {
            get { return selectedItem; }
        }
        private FabricationRecipe selectedItem;

        /// <summary>
        /// Which character's skills the current view is displayed based on
        /// </summary>
        private Character displayingForCharacter;

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

        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowSortByDropdown { get; set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowAvailableOnlyTickBox { get; set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool ShowCategoryButtons { get; set; }

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
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), paddedFrame.RectTransform), item.Prefab.Name, font: GUIStyle.SubHeadingFont)
            {
                TextAlignment = Alignment.Center,
                AutoScaleVertical = true
            };

            var innerArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.95f), paddedFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.01f,
                Stretch = true,
                CanBeFocused = true
            };

            List<MapEntityCategory> itemCategories = Enum.GetValues<MapEntityCategory>().ToList();
            itemCategories.Remove(MapEntityCategory.None);
            itemCategories.RemoveAll(c => fabricationRecipes.None(f => f.Value?.TargetItem is ItemPrefab ti && ti.Category.HasFlag(c)));
            itemCategoryButtons.Clear();

            //only create category buttons if there's more than one category in addition to "All"
            if (ShowCategoryButtons && itemCategories.Count > 2)
            {
                // ===  Item category buttons ===
                var categoryButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.05f, 1.0f), innerArea.RectTransform))
                {
                    RelativeSpacing = 0.01f
                };

                int buttonSize = Math.Min(categoryButtonContainer.Rect.Width, categoryButtonContainer.Rect.Height / itemCategories.Count);

                var categoryButton = new GUIButton(new RectTransform(new Point(buttonSize), categoryButtonContainer.RectTransform), style: "CategoryButton.All")
                {
                    ToolTip = TextManager.Get("MapEntityCategory.All"),
                    OnClicked = OnClickedCategoryButton
                };
                itemCategoryButtons.Add(categoryButton);
                foreach (MapEntityCategory category in itemCategories)
                {
                    categoryButton = new GUIButton(new RectTransform(new Point(buttonSize), categoryButtonContainer.RectTransform),
                        style: "CategoryButton." + category)
                    {
                        ToolTip = TextManager.Get("MapEntityCategory." + category),
                        UserData = category,
                        OnClicked = OnClickedCategoryButton
                    };
                    itemCategoryButtons.Add(categoryButton);
                }
                bool OnClickedCategoryButton(GUIButton button, object userData)
                {
                    MapEntityCategory? newCategory = !button.Selected ? (MapEntityCategory?)userData : null;
                    if (newCategory.HasValue) { itemFilterBox.Text = ""; }
                    selectedItemCategory = newCategory;
                    FilterEntities(newCategory, itemFilterBox.Text);
                    return true;
                }
                foreach (var btn in itemCategoryButtons)
                {
                    btn.RectTransform.SizeChanged += () =>
                    {
                        if (btn.Frame.sprites == null || !btn.Frame.sprites.TryGetValue(GUIComponent.ComponentState.None, out var spriteList)) { return; }
                        var sprite = spriteList?.First();
                        if (sprite == null) { return; }
                        btn.RectTransform.NonScaledSize = new Point(btn.Rect.Width, (int)(btn.Rect.Width * ((float)sprite.Sprite.SourceRect.Height / sprite.Sprite.SourceRect.Width)));
                    };
                }
            }            

            var mainFrame = new GUILayoutGroup(new RectTransform(Vector2.One, innerArea.RectTransform), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.02f,
                Stretch = true,
                CanBeFocused = true
            };
            
            // === TOP AREA ===
            var topFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), mainFrame.RectTransform), style: "InnerFrameDark");

                // === ITEM LIST ===
                var itemListFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), topFrame.RectTransform), childAnchor: Anchor.Center);
                    var paddedItemFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), itemListFrame.RectTransform), isHorizontal: false)
                    {
                        Stretch = true
                    };
                        var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedItemFrame.RectTransform), isHorizontal: true)
                        {
                            Stretch = true,
                            RelativeSpacing = 0.03f,
                            UserData = "filterarea"
                        };
                            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), filterArea.RectTransform), TextManager.Get("serverlog.filter"),
                                font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
                            {
                                Padding = Vector4.Zero,
                                AutoScaleVertical = true
                            };
                            itemFilterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), createClearButton: true)
                            {
                                OverflowClip = true
                            };
                            itemFilterBox.OnTextChanged += (textBox, text) =>
                            {
                                FilterEntities(selectedItemCategory, text); 
                                return true;
                            };
                            filterArea.RectTransform.MinSize = new Point(0, itemFilterBox.Rect.Height);
                            filterArea.RectTransform.MaxSize = new Point(int.MaxValue, itemFilterBox.Rect.Height);

                        var sortByArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedItemFrame.RectTransform), isHorizontal: true)
                        {
                            Stretch = true,
                            RelativeSpacing = 0.03f,
                            Visible = ShowSortByDropdown,
                            IgnoreLayoutGroups = !ShowSortByDropdown
                        };
                            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), sortByArea.RectTransform), TextManager.Get("campaignstore.sortby"),
                                font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
                            {
                                Padding = Vector4.Zero,
                                AutoScaleVertical = true
                            };
                            sortByDropdown = new GUIDropDown(new RectTransform(new Vector2(0.8f, 1.0f), sortByArea.RectTransform));
                            foreach (SortBy sortBy in Enum.GetValues<SortBy>())
                            {
                                sortByDropdown.AddItem(TextManager.Get("fabricator.sortby." + sortBy), userData: sortBy);
                            }
                            sortByDropdown.Select(index: 0);
                            sortByDropdown.AfterSelected += (GUIComponent selected, object userdata) =>
                            {
                                FilterEntities(selectedItemCategory, itemFilterBox.Text);
                                SortItems(character: Character.Controlled);
                                return true;
                            };
                            sortByArea.RectTransform.MinSize = new Point(0, sortByDropdown.Rect.Height);
                            sortByArea.RectTransform.MaxSize = new Point(int.MaxValue, sortByDropdown.Rect.Height);

                        var availableOnlyTickBoxArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedItemFrame.RectTransform), isHorizontal: true)
                        {
                            Stretch = true,
                            Visible = ShowAvailableOnlyTickBox,
                            IgnoreLayoutGroups = !ShowAvailableOnlyTickBox
                        };
                            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), availableOnlyTickBoxArea.RectTransform), TextManager.Get("fabricator.onlyshowavailable"),
                                font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
                            {
                                Padding = Vector4.Zero,
                                AutoScaleVertical = true
                            };
                            availableOnlyTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f), availableOnlyTickBoxArea.RectTransform, scaleBasis: ScaleBasis.BothHeight), label: string.Empty)
                            {
                                ToolTip = TextManager.Get("fabricator.onlyshowavailable.tooltip")
                            };
                            availableOnlyTickBox.OnSelected += (tickbox) =>
                            {                                
                                FilterEntities(selectedItemCategory, itemFilterBox.Text);
                                return true;
                            };
                            availableOnlyTickBox.RectTransform.MinSize = new Point(availableOnlyTickBox.Rect.Height);
                            availableOnlyTickBox.RectTransform.IsFixedSize = true;
                            availableOnlyTickBoxArea.RectTransform.MinSize = new Point(0, availableOnlyTickBox.Rect.Height);
                            availableOnlyTickBoxArea.RectTransform.MaxSize = new Point(int.MaxValue, availableOnlyTickBox.Rect.Height);

                        itemList = new GUIListBox(new RectTransform(new Vector2(1f, 0.8f), paddedItemFrame.RectTransform), style: null)
                        {
                            PlaySoundOnSelect = true,
                            OnSelected = (component, userdata) =>
                            {
                                if (userdata is FabricationRecipe fabricationRecipe) 
                                { 
                                    selectedItem = fabricationRecipe;
                                    SelectItem(Character.Controlled, selectedItem); 
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        };

            // === SEPARATOR === //
            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), topFrame.RectTransform, Anchor.Center), style: "VerticalLine");

                // === OUTPUT AREA === //
                var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), topFrame.RectTransform, Anchor.TopRight), childAnchor: Anchor.Center);
                    paddedOutputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), outputArea.RectTransform)) { Stretch = true };
                        outputTopArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), paddedOutputArea.RectTransform, Anchor.Center), isHorizontal: true);
                            // === OUTPUT SLOT === //
                            outputSlot = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), outputTopArea.RectTransform, scaleBasis: ScaleBasis.BothWidth), style: null);
                                outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.0f), outputSlot.RectTransform, Anchor.BottomCenter), style: null);
                                    new GUICustomComponent(new RectTransform(Vector2.One, outputInventoryHolder.RectTransform), DrawOutputOverLay) { CanBeFocused = false };
                            // === DESCRIPTION === //
                            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1f), outputTopArea.RectTransform), style: null);
                        // === REQUIREMENTS === //
                        selectedItemReqsFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedOutputArea.RectTransform), style: null);

            // === BOTTOM AREA === //
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.2f), mainFrame.RectTransform), style: null);

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

                new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), amountInputHolder.RectTransform), "1", textAlignment: Alignment.Center);

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

            foreach (MapEntityCategory category in itemCategories)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                    TextManager.Get("MapEntityCategory." + category), textColor: GUIStyle.TextColorBright)
                {
                    CanBeFocused = false,
                    UserData = category,
                    Visible = false
                };
            }

            requiresRecipeText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorrequiresrecipe"), textColor: Color.Red, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };

            nothingToShowText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.8f), itemList.Content.RectTransform), TextManager.Get("noitemsheader"),
                textAlignment: Alignment.Center, textColor: GUIStyle.TextColorDim)
            {
                CanBeFocused = false,
                Visible = false
            };

            SortItems(character: Character.Controlled);
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
                RichString recipeTooltip = RichString.Rich(fi.TargetItem.Description);
                if (fi.RequiresRecipe)
                {
                    recipeTooltip += "\n\n" + $"‖color:{XMLExtensions.ToStringHex(GUIStyle.Red)}‖{TextManager.Get("fabricatorrequiresrecipe")}‖color:end‖";
                }
                recipeTooltip = RichString.Rich(recipeTooltip);

                var frame = new GUIFrame(new RectTransform(new Point(itemList.Content.Rect.Width, (int)(40 * GUI.yScale)), itemList.Content.RectTransform), style: null)
                {
                    UserData = fi,
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = recipeTooltip
                };
                
                var container = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform),
                    childAnchor: Anchor.CenterLeft, isHorizontal: true) { RelativeSpacing = 0.02f };

                var itemIcon = fi.TargetItem.InventoryIcon ?? fi.TargetItem.Sprite;
                if (itemIcon != null)
                {
                    new GUIImage(new RectTransform(new Point(frame.Rect.Height,frame.Rect.Height), container.RectTransform),
                        itemIcon, scaleToFit: true)
                    {
                        Color = itemIcon == fi.TargetItem.Sprite ? fi.TargetItem.SpriteColor : fi.TargetItem.InventoryIconColor,
                        ToolTip = recipeTooltip
                    };
                }

                new GUITextBlock(new RectTransform(new Vector2(0.85f, 1f), container.RectTransform),
                    RichString.Rich(GetRecipeNameAndAmount(fi)), font: GUIStyle.SmallFont)
                {
                    Padding = Vector4.Zero,
                    AutoScaleVertical = true,
                    ToolTip = recipeTooltip
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
                inputContainer.Inventory.DrawWhenEquipped = true;
                inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            }
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.DrawWhenEquipped = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;
        }

        private static RichString GetRecipeNameAndAmount(FabricationRecipe fabricationRecipe)
        {
            if (fabricationRecipe == null) { return ""; }
            if (fabricationRecipe.Amount > 1)
            {
                return TextManager.GetWithVariables("fabricationrecipenamewithamount",
                    ("[name]", RichString.Rich(fabricationRecipe.DisplayName)), ("[amount]", fabricationRecipe.Amount.ToString()));
            }
            else
            {
                return RichString.Rich(fabricationRecipe.DisplayName);
            }
        }

        partial void OnItemLoadedProjSpecific()
        {
            CreateGUI();
            InitInventoryUIs();
        }

        partial void SelectProjSpecific(Character character)
        {
            if (character != Character.Controlled) { return; }

            var nonItems = itemList.Content.Children.Where(c => c.UserData is not FabricationRecipe).ToList();
            nonItems.ForEach(i => i.Visible = false);

            SortItems(character);
            FilterEntities(selectedItemCategory, itemFilterBox?.Text ?? string.Empty);
            HideEmptyItemListCategories();
        }

        private void SortItems(Character character)
        {
            SortBy sortBy = (SortBy)sortByDropdown.SelectedData;

            itemList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var item1 = c1.GUIComponent.UserData as FabricationRecipe;
                var item2 = c2.GUIComponent.UserData as FabricationRecipe;

                if (item1 == null && item2 == null)
                {
                    return 0;
                }
                else if (item1 == null)
                {
                    return -1;
                }
                else if (item2 == null)
                {
                    return 1;
                }

                bool missingRecipe1 = MissingRequiredRecipe(item1, character);
                bool missingRecipe2 = MissingRequiredRecipe(item2, character);
                if (missingRecipe1 != missingRecipe2)
                {
                    return missingRecipe1.CompareTo(missingRecipe2);
                }

                switch (sortBy)
                {
                    case SortBy.Alphabetical:
                        return string.Compare(item1.DisplayName.Value, item2.DisplayName.Value);
                    case SortBy.Category:
                        var category1 = EnumExtensions.GetIndividualFlags(item1.TargetItem.Category).FirstOrDefault();
                        var category2 = EnumExtensions.GetIndividualFlags(item2.TargetItem.Category).FirstOrDefault();
                        if (category1 == category2)
                        {
                            return string.Compare(item1.DisplayName.Value, item2.DisplayName.Value);
                        }
                        return category1.CompareTo(category2);                        
                    case SortBy.SkillRequirement:
                        float skillRequirement1 = item1.RequiredSkills.Sum(skill => skill.Level);
                        float skillRequirement2 = item2.RequiredSkills.Sum(skill => skill.Level);
                        if (MathUtils.NearlyEqual(skillRequirement1, skillRequirement2))
                        {
                            return string.Compare(item1.DisplayName.Value, item2.DisplayName.Value);
                        }
                        return skillRequirement1.CompareTo(skillRequirement2);
                    case SortBy.Price:
                        float itemValue1 = item1.TargetItem.DefaultPrice?.Price ?? 0;
                        float itemValue2 = item2.TargetItem.DefaultPrice?.Price ?? 0;
                        if (MathUtils.NearlyEqual(itemValue1, itemValue2))
                        {
                            return string.Compare(item1.DisplayName.Value, item2.DisplayName.Value);
                        }
                        return itemValue2.CompareTo(itemValue1);
                    default:
                        throw new NotImplementedException($"Sorting by {sortBy} has not been implemented.");
                }
            });

            if (sortBy == SortBy.Category)
            {
                foreach (var categoryText in itemList.Content.Children.Where(c => c.UserData?.GetType() == typeof(MapEntityCategory)).ToList())
                {
                    categoryText.RectTransform.SetAsLastChild();
                    var category = (MapEntityCategory)categoryText.UserData;
                    var firstChildWithMatchingCategory = itemList.Content.Children.FirstOrDefault(c => c.UserData is FabricationRecipe recipe && EnumExtensions.GetIndividualFlags(recipe.TargetItem.Category).FirstOrDefault() == category);
                    if (firstChildWithMatchingCategory != null)
                    { 
                        categoryText.RectTransform.RepositionChildInHierarchy(itemList.Content.GetChildIndex(firstChildWithMatchingCategory));
                        categoryText.Visible = true;
                    }
                    else
                    {
                        categoryText.Visible = false;
                    }
                }
            }

            requiresRecipeText.RectTransform.SetAsLastChild();
            var firstMissingRecipe = itemList.Content.Children.FirstOrDefault(c => c.UserData is FabricationRecipe recipe && MissingRequiredRecipe(recipe, character));
            if (firstMissingRecipe != null)
            {
                requiresRecipeText.RectTransform.RepositionChildInHierarchy(itemList.Content.GetChildIndex(firstMissingRecipe));
                requiresRecipeText.Visible = true;
            }
            else
            {
                requiresRecipeText.Visible = false;
            }

            HideEmptyItemListCategories();
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
                    if (inputContainer.Inventory?.visualSlots == null) { break; }

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

                    Rectangle slotRect = inputContainer.Inventory.visualSlots[slotIndex].Rect;

                    var requiredItemPrefab = requiredItem.FirstMatchingPrefab;

                    float iconAlpha = 0.0f;
                    ItemPrefab requiredItemToDisplay = requiredItem.DefaultItem.IsEmpty ? null : requiredItem.ItemPrefabs.FirstOrDefault(p => p.Identifier == requiredItem.DefaultItem);
                    if (requiredItemToDisplay == null && requiredItem.ItemPrefabs.Multiple())
                    {
                        float iconCycleSpeed = 0.75f;
                        float iconCycleT = (float)Timing.TotalTime * iconCycleSpeed;
                        int iconIndex = (int)(iconCycleT % requiredItem.ItemPrefabs.Count());

                        requiredItemToDisplay = requiredItem.ItemPrefabs.Skip(iconIndex).FirstOrDefault();
                        iconAlpha = Math.Min(Math.Abs(MathF.Sin(iconCycleT * MathHelper.Pi)) * 2.0f, 1.0f);
                    }
                    else
                    {
                        requiredItemToDisplay ??= requiredItem.ItemPrefabs.FirstOrDefault();
                        iconAlpha = 1.0f;
                    }
                    if (iconAlpha > 0.0f)
                    {
                        var itemIcon = requiredItemToDisplay.InventoryIcon ?? requiredItemToDisplay.Sprite;
                        itemIcon.Draw(
                            spriteBatch,
                            slotRect.Center.ToVector2(),
                            color: requiredItemToDisplay.InventoryIconColor * 0.3f * iconAlpha,
                            scale: Math.Min(slotRect.Width * 0.9f / itemIcon.size.X, slotRect.Height * 0.9f / itemIcon.size.Y));
                    }

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
                        LocalizedString toolTipText = requiredItem.OverrideHeader;
                        if (requiredItem.OverrideHeader.IsNullOrEmpty())
                        {
                            var suitableIngredients = requiredItem.ItemPrefabs.Where(ip => !ip.HideInMenus).OrderBy(ip => ip.DefaultPrice?.Price ?? 0).Select(ip => ip.Name).Distinct();
                            toolTipText = GetSuitableIngredientText(suitableIngredients);
                        }
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

                        toolTipText = $"‖color:{Color.White.ToStringHex()}‖{toolTipText}‖color:end‖";
                        if (!requiredItem.OverrideDescription.IsNullOrEmpty())
                        {
                            toolTipText += '\n' + requiredItem.OverrideDescription;
                        }
                        else if (!requiredItemPrefab.Description.IsNullOrEmpty())
                        {
                            toolTipText += '\n' + requiredItemPrefab.Description;
                        }
                        tooltip = new ToolTip { TargetElement = slotRect, Tooltip = toolTipText };
                    }

                    slotIndex++;
                }
            }
        }

        private LocalizedString GetSuitableIngredientText(IEnumerable<LocalizedString> itemNameList)
        {
            int count = itemNameList.Count();
            if (count == 0)
            {
                return string.Empty;
            }
            else if (count == 1)
            {
                return itemNameList.First();
            }
            else if (count == 2)
            {
                //[item1] or [item2]
                return TextManager.GetWithVariables(
                    "DialogRequiredTreatmentOptionsLast",
                    ("[treatment1]", itemNameList.ElementAt(0)),
                    ("[treatment2]", itemNameList.ElementAt(1)));
            }
            else
            {
                // [item1], [item2], [item3] ... or [lastitem]
                LocalizedString itemListStr = TextManager.GetWithVariables(
                    "DialogRequiredTreatmentOptionsFirst",
                    ("[treatment1]", itemNameList.ElementAt(0)),
                    ("[treatment2]", itemNameList.ElementAt(1)));

                int i;
                bool isTruncated = false;
                for (i = 2; i < count - 1; i++)
                {
                    if (itemListStr.Length > 50)
                    {
                        isTruncated = true;
                        break;
                    }
                    itemListStr = TextManager.GetWithVariables(
                      "DialogRequiredTreatmentOptionsFirst",
                      ("[treatment1]", itemListStr),
                      ("[treatment2]", itemNameList.ElementAt(i)));
                }
                itemListStr = TextManager.GetWithVariables(
                    "DialogRequiredTreatmentOptionsLast",
                    ("[treatment1]", itemListStr),
                    ("[treatment2]", itemNameList.ElementAt(i)));

                if (isTruncated)
                {
                    itemListStr += TextManager.Get("ellipsis");
                }
                return itemListStr;
            }
        }

        private void DrawOutputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null && outputContainer.Inventory?.visualSlots != null)
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
                        color: Color.Lerp(targetItem.TargetItem.InventoryIconColor, Color.TransparentBlack, 0.5f),
                        scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y) * 0.9f);
                }
            }
            
            if (tooltip != null)
            {
                GUIComponent.DrawToolTip(spriteBatch, RichString.Rich(tooltip.Tooltip), tooltip.TargetElement);
                tooltip = null;
            }
        }

        private bool FilterEntities(MapEntityCategory? category, string filter)
        {
            bool onlyShowAvailable = availableOnlyTickBox is { Selected: true };

            bool anyVisible = false;
            foreach (GUIComponent child in itemList.Content.Children)
            {
                FabricationRecipe recipe = child.UserData as FabricationRecipe;
                if (recipe?.DisplayName == null) { continue; }

                if (recipe.HideForNonTraitors)
                {
                    if (Character.Controlled is not { IsTraitor: true })
                    {
                        child.Visible = false;
                        continue;
                    }
                }

                if (recipe.RequiresRecipe && recipe.HideIfNoRecipe)
                {
                    if (Character.Controlled != null)
                    {
                        if (!AnyOneHasRecipeForItem(Character.Controlled, recipe.TargetItem))
                        {
                            child.Visible = false;
                            continue;
                        }
                    }
                }

                child.Visible =
                    (string.IsNullOrWhiteSpace(filter) || recipe.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)) &&
                    (!category.HasValue || recipe.TargetItem.Category.HasFlag(category.Value)) &&
                    (!onlyShowAvailable || CanBeFabricated(recipe, availableIngredients, Character.Controlled));
                if (child.Visible)
                {
                    anyVisible = true;
                }
            }

            foreach (GUIButton btn in itemCategoryButtons)
            {
                btn.Selected = (MapEntityCategory?)btn.UserData == selectedItemCategory;
            }
            HideEmptyItemListCategories();
            nothingToShowText.Visible = !anyVisible;
            itemList.UserData = "itemlist";

            return true;
        }

        private void HideEmptyItemListCategories()
        {
            bool visibleElementsChanged = false;
            //go through the elements backwards, and disable the labels if there's no items below them
            bool recipeVisible = false;
            foreach (GUIComponent child in itemList.Content.Children.Reverse())
            {
                if (child.UserData is not FabricationRecipe recipe)
                {
                    if (child.Enabled)
                    {
                        if (child.Visible != recipeVisible)
                        {
                            child.Visible = recipeVisible;
                            visibleElementsChanged = true;
                        }
                    }
                    recipeVisible = false;
                }
                else
                {
                    recipeVisible |= child.Visible;
                }
            }

            SortBy sortBy = (SortBy)sortByDropdown.SelectedData;
            if (sortBy != SortBy.Category)
            {
                itemList.Content.Children.Where(c => c.UserData?.GetType() == typeof(MapEntityCategory)).ForEach(c => c.Visible = false);
            }

            if (visibleElementsChanged)
            {
                itemList.UpdateScrollBarSize();
                itemList.BarScroll = 0.0f;
            }
        }

        public bool ClearFilter()
        {
            FilterEntities(selectedItemCategory, "");
            itemList.UpdateScrollBarSize();
            itemList.BarScroll = 0.0f;
            itemFilterBox.Text = "";
            return true;
        }

        private readonly record struct SelectedRecipe(Character User, FabricationRecipe SelectedItem, Option<float> OverrideRequiredTime);
        private Option<SelectedRecipe> LastSelectedRecipe = Option.None;

        private bool SelectItem(Character user, FabricationRecipe selectedItem, float? overrideRequiredTime = null)
        {
            this.selectedItem = selectedItem;
            displayingForCharacter = user;
            var selectedRecipe = new SelectedRecipe(user, selectedItem, overrideRequiredTime is null ? Option.None : Option.Some(overrideRequiredTime.Value));
            LastSelectedRecipe = Option.Some(selectedRecipe);
            CreateSelectedItemUI(selectedRecipe);
            return true;
        }

        private void CreateSelectedItemUI(SelectedRecipe recipe)
        {
            var (user, selectedRecipe, overrideRequiredTime) = recipe;
            int max = Math.Max(selectedRecipe.TargetItem.GetMaxStackSize(outputContainer.Inventory) / selectedRecipe.Amount, 1);

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

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f, CanBeFocused = true };
            var paddedReqFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemReqsFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };

            LocalizedString itemName = GetRecipeNameAndAmount(selectedRecipe);
            LocalizedString name = itemName;

            QualityResult result = GetFabricatedItemQuality(selectedRecipe, user);

            float minimumQuality = selectedRecipe.Quality ?? result.Quality;

            LocalizedString qualityTooltip = string.Empty;
            if (result.HasRandomQualityRollChance)
            {
                float plusOnePercentage = result.TotalPlusOnePercentage;
                float plusTwoPercentage = result.TotalPlusTwoPercentage;

                string plusOnePercentageText = plusOnePercentage.ToString("F1", CultureInfo.InvariantCulture);
                string plusTwoPercentageText = plusTwoPercentage.ToString("F1", CultureInfo.InvariantCulture);

                int plusOneQuality = Math.Clamp(result.Quality + 1, min: 0, max: 3);
                int plusTwoQuality = Math.Clamp(result.Quality + 2, min: 0, max: 3);

                LocalizedString plusOneQualityText = TextManager.Get($"quality{plusOneQuality}");
                LocalizedString plusTwoQualityText = TextManager.Get($"quality{plusTwoQuality}");

                string localizationTag = plusTwoPercentage > 0f && plusOnePercentage > 0 && plusOneQuality != plusTwoQuality ? "meetsbonusrequirementtwice" : "meetsbonusrequirement";

                var variables = new (string Key, LocalizedString Value)[]
                {
                    ("[chance]", plusOnePercentageText), ("[quality]", plusOneQualityText),
                    ("[chance2]", plusTwoPercentageText), ("[quality2]", plusTwoQualityText)
                };

                if (MathUtils.NearlyEqual(plusOnePercentage, 0))
                {
                    variables = new[] { ("[chance]", plusTwoPercentageText), ("[quality]", plusTwoQualityText) };
                }

                if (plusOneQuality == plusTwoQuality)
                {
                    LocalizedString rawPercentage = result.PlusOnePercentage.ToString("F1", CultureInfo.InvariantCulture);
                    variables = new[] { ("[chance]", rawPercentage), ("[quality]", plusOneQualityText) };
                }

                if (plusOnePercentage >= 100.0f) { minimumQuality = plusOneQuality; }
                if (plusTwoPercentage >= 100.0f) { minimumQuality = plusTwoQuality; }

                qualityTooltip = TextManager.GetWithVariables(localizationTag, variables);
            }

            if (minimumQuality > 0 || result.HasRandomQualityRollChance)
            {
                name = TextManager.GetWithVariable("itemname.quality" + (int)minimumQuality, "[itemname]", itemName + '\n')
                    .Fallback(TextManager.GetWithVariable("itemname.quality3", "[itemname]", itemName + '\n'));
            }

            var nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
               RichString.Rich(name), textAlignment: Alignment.TopLeft, textColor: Color.Aqua, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };

            if (result.HasRandomQualityRollChance)
            {
                var iconLayout = new GUIFrame(new RectTransform(new Vector2(0.4f, 1f), selectedItemFrame.RectTransform, anchor: Anchor.TopRight), style: null);
                var icon = GameSession.CreateNotificationIcon(iconLayout, offset: true);
                icon.ToolTip = RichString.Rich(qualityTooltip);
                icon.Visible = icon.CanBeFocused = true;
            }

            outputTopArea.RectTransform.MaxSize = new Point(int.MaxValue, outputInventoryHolder.Rect.Height);
            paddedOutputArea.Recalculate();

            nameBlock.Padding = new Vector4(0, nameBlock.Padding.Y, GUI.IntScale(5), nameBlock.Padding.W);
            if (nameBlock.TextScale < 0.7f)
            {
                nameBlock.AutoScaleHorizontal = false;
                nameBlock.TextScale = 0.7f;
                nameBlock.Wrap = true;
                nameBlock.SetTextPos();
                nameBlock.RectTransform.MinSize = new Point(0, (int)(nameBlock.TextSize.Y * nameBlock.TextScale));
            }

            bool largeUI = GuiFrame.Rect.Height > GUI.IntScale(500);
            if (largeUI)
            {
                paddedFrame.ChildAnchor = Anchor.CenterLeft;
            }

            if (!selectedRecipe.TargetItem.Description.IsNullOrEmpty())
            {
                var descriptionParent = largeUI ? paddedReqFrame : paddedFrame;
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), descriptionParent.RectTransform),
                    RichString.Rich(selectedRecipe.TargetItem.Description),
                    font: GUIStyle.SmallFont, wrap: true);
                if (!largeUI)
                {
                    description.Padding = new Vector4(0, description.Padding.Y, description.Padding.Z, description.Padding.W);
                }

                while (description.Rect.Height + nameBlock.Rect.Height > descriptionParent.Rect.Height)
                {
                    var lines = description.WrappedText.Split('\n');
                    if (lines.Count <= 1) { break; }
                    var newString = string.Join('\n', lines.Take(lines.Count - 1));
                    description.Text = newString.Substring(0, newString.Length - 4) + "...";
                    description.CalculateHeightFromText();
                    description.ToolTip = selectedRecipe.TargetItem.Description;
                }
            }

            IEnumerable<Skill> inadequateSkills = Enumerable.Empty<Skill>();
            if (user != null)
            {
                inadequateSkills = selectedRecipe.RequiredSkills.Where(skill => user.GetSkillLevel(skill.Identifier) < Math.Round(skill.Level * SkillRequirementMultiplier));
            }

            if (selectedRecipe.RequiredSkills.Any())
            {
                LocalizedString text = "";
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), 
                    TextManager.Get("FabricatorRequiredSkills"), textColor: inadequateSkills.Any() ? GUIStyle.Red : GUIStyle.Green, font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                    ToolTip = TextManager.Get("fabricatorrequiredskills.tooltip")
                };
                foreach (Skill skill in selectedRecipe.RequiredSkills)
                {
                    text += TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + Math.Round(skill.Level * SkillRequirementMultiplier);
                    if (skill != selectedRecipe.RequiredSkills.Last()) { text += "\n"; }
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), text, font: GUIStyle.SmallFont);
            }

            float degreeOfSuccess = user == null ? 0.0f : FabricationDegreeOfSuccess(user, selectedRecipe.RequiredSkills);
            if (degreeOfSuccess > 0.5f) { degreeOfSuccess = 1.0f; }

            float requiredTime = overrideRequiredTime.TryUnwrap(out var time) 
                ? time
                : (user == null ? selectedRecipe.RequiredTime : GetRequiredTime(selectedRecipe, user));

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

            if (selectedRecipe.RequiredMoney > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform),
                    TextManager.Get("subeditor.price"), textColor: ToolBox.GradientLerp(degreeOfSuccess, GUIStyle.Red, Color.Yellow, GUIStyle.Green), font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), TextManager.FormatCurrency(SelectedItem.RequiredMoney),
                    font: GUIStyle.SmallFont);
            }

            if (selectedRecipe.RequiresRecipe && !AnyOneHasRecipeForItem(Character.Controlled, selectedRecipe.TargetItem))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform),
                    TextManager.Get("fabricatorrequiresrecipe"), textColor: GUIStyle.Red, font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
            }
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
                !outputContainer.Inventory.CanProbablyBePut(selectedItem.TargetItem, selectedItem.OutCondition * selectedItem.TargetItem.Health))
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

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            activateButton.Enabled = false;
            inSufficientPowerWarning.Visible = IsActive && !hasPower;

            ingredientHighlightTimer -= deltaTime;

            if (!IsActive)
            {
                if (outputContainer != null && outputContainer.Inventory.AllItems.Any())
                {
                    if (outputContainer.Inventory.visualSlots is { } visualSlots && visualSlots.Any() &&
                        visualSlots[0].HighlightTimer <= 0.0f)
                    {
                        visualSlots[0].ShowBorderHighlight(GUIStyle.Green, 0.5f, 0.5f);
                    }
                }

                if (selectedItem != null && displayingForCharacter != character)
                {
                    //reselect to recreate the info based on the new user's skills
                    SelectItem(character, selectedItem);
                }

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

                    bool sufficientSkills = FabricationDegreeOfSuccess(character, recipe.RequiredSkills) >= 0.5f;

                    Color baseColor = MissingRequiredRecipe(recipe, character) ? 
                        GUIStyle.Red :
                        (sufficientSkills ? GUIStyle.TextColorNormal : GUIStyle.Orange);

                    var childContainer = child.GetChild<GUILayoutGroup>();
                    childContainer.GetChild<GUITextBlock>().TextColor = baseColor * (canBeFabricated ? 1.0f : 0.5f);
                    childContainer.GetChild<GUIImage>().Color = recipe.TargetItem.InventoryIconColor * (canBeFabricated ? 1.0f : 0.5f);

                    var limitReachedText = child.FindChild(nameof(FabricationLimitReachedText));
                    limitReachedText.Visible = !canBeFabricated && fabricationLimits.TryGetValue(recipe.RecipeHash, out int amount) && amount <= 0;
                }
            }
        }

        public override void OnPlayerSkillsChanged()
            => RefreshSelectedItem();

        public void RefreshSelectedItem()
        {
            if (!LastSelectedRecipe.TryUnwrap(out var lastSelected)) { return; }
            CreateSelectedItemUI(lastSelected);
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
            //don't touch the amount unless another character changed it or the fabricator is running
            //otherwise we may end up reverting the changes the client just did to the amount
            if ((user != null && user != Character.Controlled) || State != FabricatorState.Stopped)
            {
                this.amountToFabricate = amountToFabricate;
            }
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
