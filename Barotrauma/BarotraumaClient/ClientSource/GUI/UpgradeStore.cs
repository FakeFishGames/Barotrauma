#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlayerBalanceElement = Barotrauma.CampaignUI.PlayerBalanceElement;

// ReSharper disable UnusedVariable

namespace Barotrauma
{
    public sealed class UpgradeStore
    {
        public readonly struct CategoryData
        {
            public readonly UpgradeCategory Category;
            public readonly List<UpgradePrefab>? Prefabs;
            public readonly UpgradePrefab? SinglePrefab;

            public CategoryData(UpgradeCategory category, List<UpgradePrefab> prefabs)
            {
                Category = category;
                Prefabs = prefabs;
                SinglePrefab = null;
            }

            public CategoryData(UpgradeCategory category, UpgradePrefab prefab)
            {
                Category = category;
                SinglePrefab = prefab;
                Prefabs = null;
            }
        }

        private readonly CampaignUI campaignUI;
        private CampaignMode? Campaign => campaignUI.Campaign;
        private int PlayerBalance => Campaign?.GetBalance() ?? 0;
        private UpgradeTab selectedUpgradeTab = UpgradeTab.Upgrade;

        private GUIMessageBox? currectConfirmation;

        public readonly GUIFrame ItemInfoFrame;
        private GUIComponent? selectedUpgradeCategoryLayout;
        private GUILayoutGroup? topHeaderLayout;
        private GUILayoutGroup? mainStoreLayout;
        private GUILayoutGroup? storeLayout;
        private GUILayoutGroup? categoryButtonLayout;
        private GUILayoutGroup? submarineInfoFrame;
        private GUIListBox? currentStoreLayout;
        private GUICustomComponent? submarinePreviewComponent;
        private GUIFrame? subPreviewFrame;
        private Submarine? drawnSubmarine;
        private readonly List<UpgradeCategory> applicableCategories = new List<UpgradeCategory>();
        private Vector2[][] subHullVertices = new Vector2[0][];
        private List<Structure> submarineWalls = new List<Structure>();

        public MapEntity? HoveredEntity;
        private bool highlightWalls;

        private UpgradeCategory? currentUpgradeCategory;
        private Item? activeItemSwapSlideDown;

        private readonly Dictionary<Item, GUIComponent> itemPreviews = new Dictionary<Item, GUIComponent>();

        private static readonly Color previewWhite = Color.White * 0.5f;
        
        private Point screenResolution;

        private bool needsRefresh = true;

        private PlayerBalanceElement? playerBalanceElement;

        private static ImmutableHashSet<Character> characterList = ImmutableHashSet<Character>.Empty;

        /// <summary>
        /// While set to true any call to <see cref="RefreshUpgradeList"/> will cause the buy button to be disabled and to not update the prices.
        /// This is to prevent us from buying another upgrade before the server has given us the new prices and causing potential syncing issues.
        /// </summary>
        public static bool WaitForServerUpdate;

        private enum UpgradeTab
        {
            Upgrade,
            Repairs,
            Customize
        }

        private enum UpgradeStoreUserData
        {
            BuyButton,
            BuyButtonLayout,
            ProgressBarLayout,
            IncreaseLabel,
            PriceLabel,
            MaterialCostList
        }

        public UpgradeStore(CampaignUI campaignUI, GUIComponent parent)
        {
            WaitForServerUpdate = false;
            characterList = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            this.campaignUI = campaignUI;
            GUIFrame upgradeFrame = new GUIFrame(rectT(1, 1, parent, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                CanBeFocused = false, UserData = "outerglow"
            };

            ItemInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.13f, 0.13f), GUI.Canvas, minSize: new Point(250, 150)), style: "GUIToolTip")
            {
                CanBeFocused = false
            };

            CreateUI(upgradeFrame);

            if (Campaign == null) { return; }
            Identifier eventId = new Identifier(nameof(UpgradeStore));
            Campaign.UpgradeManager.OnUpgradesChanged?.RegisterOverwriteExisting(eventId, _ => RequestRefresh());
            Campaign.CargoManager.OnPurchasedItemsChanged.RegisterOverwriteExisting(eventId, _ => RequestRefresh());
            Campaign.CargoManager.OnSoldItemsChanged.RegisterOverwriteExisting(eventId, _ => RequestRefresh());
            Campaign.OnMoneyChanged.RegisterOverwriteExisting(eventId, _ => RequestRefresh());
        }

        public void RequestRefresh(bool refreshUpgrades = false)
        {
            needsRefresh = true;
            if (refreshUpgrades) { SelectTab(selectedUpgradeTab); }
        }

        private void RefreshAll()
        {
            characterList = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            switch (selectedUpgradeTab)
            {
                case UpgradeTab.Repairs:
                    SelectTab(UpgradeTab.Repairs);
                    break;
                case UpgradeTab.Upgrade:
                    RefreshUpgradeList();
                    RefreshItemPreviews();
                    break;
                case UpgradeTab.Customize:
                    RefreshCustomizeList();
                    RefreshItemPreviews();
                    break;
            }
            needsRefresh = false;
            return;

            void RefreshItemPreviews()
            {
                foreach (var itemPreview in itemPreviews)
                {
                    if (itemPreview.Value is not GUIImage image || itemPreview.Key == null) { continue; }
                    if (itemPreview.Key.PendingItemSwap == null)
                    {
                        image.Sprite = itemPreview.Key.Prefab.UpgradePreviewSprite;
                    }
                    else if (itemPreview.Key.PendingItemSwap.UpgradePreviewSprite != null)
                    {
                        image.Sprite = itemPreview.Key.PendingItemSwap.UpgradePreviewSprite;
                    }
                }
            }
        }

        private void RefreshCustomizeList()
        {
            if (selectedUpgradeCategoryLayout != null && activeItemSwapSlideDown != null)
            {
                selectedUpgradeCategoryLayout.ClearChildren();
                SelectCustomizeItem(activeItemSwapSlideDown);
            }

            if (currentStoreLayout is { } list)
            {
                foreach (var component in list.Content.Children)
                {
                    if (component.UserData is Item item)
                    {
                        if (component.FindChild("itemname", recursive: true) is not GUITextBlock text)
                        {
                            DebugConsole.ThrowError($"Error in {nameof(RefreshCustomizeList)}: \"itemname\" not found.");
                            continue;
                        }

                        SetSwapItemName(item, text);
                    }
                }
            }
        }

        private void RefreshUpgradeList()
        {
            if (Campaign == null) { return; }
            // Updates the progress bar / text and disables the buy button if we reached max level
            if (selectedUpgradeCategoryLayout?.Parent != null && selectedUpgradeCategoryLayout.FindChild("prefablist", true) is GUIListBox listBox)
            {
                foreach (var component in listBox.Content.Children)
                {
                    if (component.UserData is CategoryData { SinglePrefab: {  } prefab} data)
                    {
                        UpdateUpgradeEntry(component, prefab, data.Category, Campaign);
                    }
                }
                if (customizeTabOpen && selectedUpgradeCategoryLayout != null && Submarine.MainSub != null && currentUpgradeCategory != null)
                {
                    CreateSwappableItemList(listBox, currentUpgradeCategory, Submarine.MainSub);
                    if (activeItemSwapSlideDown is Item prevOpenedItem)
                    {
                        var currentButton = listBox.FindChild(c => c.UserData as Item == prevOpenedItem, recursive: true) as GUIButton;
                        currentButton?.OnClicked(currentButton, prevOpenedItem);
                    }
                }
            }

            // update the small indicator icons on the list
            if (currentStoreLayout?.Parent != null)
            {
                UpdateCategoryList(currentStoreLayout, Campaign, drawnSubmarine, applicableCategories);
            }

        }

        //TODO: move this somewhere else
        public static void UpdateCategoryList(GUIListBox categoryList, CampaignMode campaign, Submarine? drawnSubmarine, IEnumerable<UpgradeCategory> applicableCategories)
        {
            var subItems = GetSubItems();
            foreach (GUIComponent component in categoryList.Content.Children)
            {
                if (!(component.UserData is CategoryData data)) { continue; }
                if (component.FindChild("indicators", true) is { } indicators && data.Prefabs != null)
                {
                    // ReSharper disable once PossibleMultipleEnumeration
                    UpdateCategoryIndicators(indicators, component, data.Prefabs, data.Category, campaign, drawnSubmarine, applicableCategories);
                }
            }

            // reset the order first
            foreach (UpgradeCategory category in UpgradeCategory.Categories.OrderBy(c => c.Name))
            {
                GUIComponent component = categoryList.Content.FindChild(c => c.UserData is CategoryData categoryData && categoryData.Category == category);
                component?.SetAsLastChild();
            }

            // send the disabled components to the bottom
            List<GUIComponent> lastChilds = categoryList.Content.Children.Where(component => !component.Enabled).ToList();

            foreach (var lastChild in lastChilds)
            {
                lastChild.SetAsLastChild();
            }
        }

        /*                                        Rough layout of the upgrade store                                  0.9 padding
         * _____________________________________________________________________________________________________________________
         * | i | Shipyard                                            |                                                 balance |
         * |---------------------------------------------------------|                                                 xxxx mk |
         * |   upg.  |  maint.  |                                    |_________________________________________________________|
         * |---------------------------------------------------------|---------------------------------------------------------| <- header separator
         * | upgrade list       | | selected category  |                                        |                     sub name |
         * |                    | |                    |            empty space                 |        submarine description |
         * |                    | |                    |                                        |______________________________|
         * |                    | |  __________________|_______________________________________________________________________|
         * |                    | |  |                 |                                                                       |
         * |____________________| |__|_________________|                                                                       |
         * | store layout       | | category layout    |                                                                       |
         * |                    | |  |                 |                                                                       |
         * |____________________| |  |                 |                                                                       |
         * |                      |  |                 |                                                                       |
         * |                      |  |         submarine preview layout                                                        |
         * |                      |  |                 |                                                                       |
         * |                      |  |                 |                                                                       |
         * |     empty space      |  |                 |                                                                       |
         * |                      |  |                 |                                                                       |
         * |                      |  |                 |                                                                       |
         * |                      |  |                 |                                                                       |
         * |______________________|__|_________________|_______________________________________________________________________|
         */
        private void CreateUI(GUIComponent parent)
        {
            selectedUpgradeTab = UpgradeTab.Upgrade;
            parent.ClearChildren();

            ItemInfoFrame.ClearChildren();

            /*           TOOLTIP
             * |----------------------------|
             * | item name                  |
             * |----------------------------|
             * | upgrades:                  |
             * |----------------------------|
             * | upgrade list               |
             * |                            |
             * |                            |
             * |----------------------------|
             * | X more...                  |
             * |----------------------------|
             */
            GUILayoutGroup tooltipLayout = new GUILayoutGroup(rectT(0.95f,0.95f, ItemInfoFrame, Anchor.Center)) { Stretch = true };
            new GUITextBlock(rectT(1, 0, tooltipLayout), string.Empty, font: GUIStyle.SubHeadingFont) { UserData = "itemname" };
            new GUITextBlock(rectT(1, 0, tooltipLayout), TextManager.Get("UpgradeUITooltip.UpgradeListHeader"));
            new GUIListBox(rectT(1, 0.5f, tooltipLayout), style: null) { ScrollBarVisible = false, AutoHideScrollBar = false, SmoothScroll = true, UserData = "upgradelist"};
            new GUITextBlock(rectT(1, 0, tooltipLayout), string.Empty) { UserData = "moreindicator" };
            ItemInfoFrame.Children.ForEach(c => { c.CanBeFocused = false; c.Children.ForEach(c2 => c2.CanBeFocused = false); });

            GUIFrame paddedLayout = new GUIFrame(rectT(0.95f, 0.95f, parent, Anchor.Center), style: null);
            mainStoreLayout = new GUILayoutGroup(rectT(1, 0.9f, paddedLayout, Anchor.BottomLeft), isHorizontal: true) { RelativeSpacing = 0.01f };
            topHeaderLayout = new GUILayoutGroup(rectT(1, 0.1f, paddedLayout, Anchor.TopLeft), isHorizontal: true);

            storeLayout = new GUILayoutGroup(rectT(0.2f, 0.4f, mainStoreLayout), isHorizontal: true) { RelativeSpacing = 0.02f };


            /*                                         LEFT HEADER LAYOUT
             * |---------------------------------------------------------------------------------------------------|
             * | icon |  Shipyard                                                                                  |
             * |---------------------------------------------------------------------------------------------------|
             * |    upgrades    |    maintenance    | <- 1/3rd                  empty space                        |
             * |---------------------------------------------------------------------------------------------------|
             */
            GUILayoutGroup leftLayout = new GUILayoutGroup(rectT(0.4f, 1, topHeaderLayout)) { RelativeSpacing = 0.05f };
                GUILayoutGroup locationLayout = new GUILayoutGroup(rectT(1, 0.5f, leftLayout), isHorizontal: true);
                    GUIImage submarineIcon = new GUIImage(rectT(new Point(locationLayout.Rect.Height, locationLayout.Rect.Height), locationLayout), style: "SubmarineIcon", scaleToFit: true);
                    var header = new GUITextBlock(rectT(1.0f - submarineIcon.RectTransform.RelativeSize.X, 1, locationLayout), TextManager.Get("UpgradeUI.Title"), font: GUIStyle.LargeFont);
                    header.RectTransform.MaxSize = new Point((int)(header.TextSize.X + header.Padding.X + header.Padding.Z), int.MaxValue);
                    new GUITextBlock(rectT(1.0f, 1, locationLayout), TextManager.Get("UpgradeUI.AllSubmarinesInfo"), font: GUIStyle.SmallFont, wrap: true);                

                categoryButtonLayout = new GUILayoutGroup(rectT(0.75f, 0.3f, leftLayout), isHorizontal: true) { Stretch = true };
                    GUIButton upgradeButton = new GUIButton(rectT(0.33f, 1f, categoryButtonLayout), TextManager.Get("UICategory.Upgrades"), style: "GUITabButton") { UserData = UpgradeTab.Upgrade, Selected = selectedUpgradeTab == UpgradeTab.Upgrade };
                    GUIButton customizeButton = new GUIButton(rectT(0.33f, 1f, categoryButtonLayout), TextManager.Get("UICategory.Customize"), style: "GUITabButton") { UserData = UpgradeTab.Customize, Selected = selectedUpgradeTab == UpgradeTab.Customize };
                    GUIButton repairButton = new GUIButton(rectT(0.33f, 1f, categoryButtonLayout), TextManager.Get("UICategory.Maintenance"), style: "GUITabButton") { UserData = UpgradeTab.Repairs, Selected = selectedUpgradeTab == UpgradeTab.Repairs };

            /*                                         RIGHT HEADER LAYOUT
             * |---------------------------------------------------------------------------------------------------|
             * |                                           empty space                                             |
             * |---------------------------------------------------------------------------------------------------|
             * |                                                                                           Balance |
             * |                                                                                           XXXX Mk |
             * |---------------------------------------------------------------------------------------------------|
             * |               empty space                      |                horizontal line                   |
             * |---------------------------------------------------------------------------------------------------|
             */
            GUILayoutGroup rightLayout = new GUILayoutGroup(rectT(0.5f, 1, topHeaderLayout), childAnchor: Anchor.TopRight);
            playerBalanceElement = CampaignUI.AddBalanceElement(rightLayout, new Vector2(1.0f, 0.8f));
            if (playerBalanceElement is { } balanceElement)
            {
                balanceElement.TotalBalanceContainer.OnAddedToGUIUpdateList += (_) =>
                {
                    playerBalanceElement = CampaignUI.UpdateBalanceElement(playerBalanceElement);
                };
            }
            new GUIFrame(rectT(0.5f, 0.1f, rightLayout, Anchor.BottomRight), style: "HorizontalLine") { IgnoreLayoutGroups = true };

            repairButton.OnClicked = upgradeButton.OnClicked = customizeButton.OnClicked = (button, o) =>
            {
                if (o is not UpgradeTab upgradeTab) { return false;  }

                if (upgradeTab != selectedUpgradeTab || currentStoreLayout == null || currentStoreLayout.Parent != storeLayout)
                {
                    selectedUpgradeTab = upgradeTab;
                    SelectTab(selectedUpgradeTab);
                    storeLayout?.Recalculate();
                }

                foreach (GUIButton btn in (Span<GUIButton>)[repairButton, upgradeButton, customizeButton])
                {
                    btn.Selected = (UpgradeTab) btn.UserData == selectedUpgradeTab;
                }

                return true;
            };

            // submarine preview
            submarinePreviewComponent = new GUICustomComponent(rectT(0.75f, 0.75f, mainStoreLayout, Anchor.BottomRight), onUpdate: UpdateSubmarinePreview, onDraw: DrawSubmarine)
            {
                IgnoreLayoutGroups = true
            };

            SelectTab(UpgradeTab.Upgrade);

            var itemSwapPreview = new GUICustomComponent(new RectTransform(new Vector2(0.25f, 0.4f), mainStoreLayout.RectTransform, Anchor.TopLeft) 
                { RelativeOffset = new Vector2(0.52f * GUI.AspectRatioAdjustment, 0.0f) }, DrawItemSwapPreview)
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = true
            };

            GUITextBlock.AutoScaleAndNormalize(upgradeButton.TextBlock, repairButton.TextBlock);

#if DEBUG
            // creates a button that re-creates the UI
            CreateRefreshButton();
            void CreateRefreshButton()
            {
                new GUIButton(rectT(0.2f, 0.1f, parent, Anchor.TopCenter), "Recreate UI - NOT PRESENT IN RELEASE!")
                {
                    OnClicked = (button, o) =>
                    {
                        CreateUI(parent);
                        return true;
                    }
                };
            }
#endif
        }

        private void DrawItemSwapPreview(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            var selectedItem = customizeTabOpen ?
                activeItemSwapSlideDown ?? HoveredEntity as Item : 
                HoveredEntity as Item;
            if (selectedItem?.Prefab.SwappableItem == null) { return; }

            Sprite schematicsSprite = selectedItem.Prefab.SwappableItem.SchematicSprite;
            if (schematicsSprite == null) { return; }
            float schematicsScale = Math.Min(component.Rect.Width / 2 / schematicsSprite.size.X, component.Rect.Height / schematicsSprite.size.Y);
            Vector2 center = new Vector2(component.Rect.Center.X, component.Rect.Center.Y);
            schematicsSprite.Draw(spriteBatch, new Vector2(component.Rect.X, center.Y), GUIStyle.Green, new Vector2(0, schematicsSprite.size.Y / 2),
                scale: schematicsScale);

            var swappableItemList = selectedUpgradeCategoryLayout?.FindChild("prefablist", true) as GUIListBox;
            var highlightedElement = swappableItemList?.Content.FindChild(c => c.UserData is ItemPrefab && c.IsParentOf(GUI.MouseOn)) ?? GUI.MouseOn;
            ItemPrefab swapTo = highlightedElement?.UserData as ItemPrefab ?? selectedItem.PendingItemSwap;
            if (swapTo?.SwappableItem == null) { return; }
            Sprite? schematicsSprite2 = swapTo.SwappableItem?.SchematicSprite;
            schematicsSprite2?.Draw(spriteBatch, new Vector2(component.Rect.Right, center.Y), GUIStyle.Orange, new Vector2(schematicsSprite2.size.X, schematicsSprite2.size.Y / 2),
                scale: Math.Min(component.Rect.Width / 2 / schematicsSprite2.size.X, component.Rect.Height / schematicsSprite2.size.Y));

            var arrowSprite = GUIStyle.GetComponentStyle("GUIButtonToggleRight")?.GetDefaultSprite();
            if (arrowSprite != null)
            {
                arrowSprite.Draw(spriteBatch, center, scale: GUI.Scale);
            }
        }

        private void SelectTab(UpgradeTab tab)
        {
            if (currentStoreLayout != null)
            {
                storeLayout?.RemoveChild(currentStoreLayout);
            }

            if (selectedUpgradeCategoryLayout != null)
            {
                mainStoreLayout?.RemoveChild(selectedUpgradeCategoryLayout);
            }

            switch (tab)
            {
                case UpgradeTab.Upgrade:
                {
                    CreateUpgradeTab();
                    break;
                }
                case UpgradeTab.Repairs:
                {
                    CreateRepairsTab();
                    break;
                }
                case UpgradeTab.Customize:
                {
                    CreateCustomizeTab();
                    break;
                }
            }
        }

        private void CreateCustomizeTab()
        {
            if (Campaign == null || storeLayout == null || mainStoreLayout is null) { return; }

            currentStoreLayout = CreateSwappableList(rectT(1.0f, 1.5f, storeLayout));

            selectedUpgradeCategoryLayout = new GUIFrame(rectT(0.3f  * GUI.AspectRatioAdjustment, 1, mainStoreLayout), style: null) { CanBeFocused = false };

            RefreshCustomizeList();

            currentStoreLayout.OnSelected += (component, userData) =>
            {
                if (!component.Enabled)
                {
                    selectedUpgradeCategoryLayout?.ClearChildren();
                    foreach (GUIComponent itemFrame in itemPreviews.Values)
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                        itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
                    }
                    return true;
                }

                if (userData is Item item)
                {
                    SelectCustomizeItem(item);
                }

                return true;
            };
        }

        private void SelectCustomizeItem(Item item)
        {
            if (selectedUpgradeCategoryLayout == null) { return; }
            activeItemSwapSlideDown = item;

            selectedUpgradeCategoryLayout.ClearChildren();
            GUIFrame frame = new GUIFrame(rectT(1.0f, 0.4f, selectedUpgradeCategoryLayout))
            {
                UserData = item.Prefab
            };
            GUIFrame paddedFrame = new GUIFrame(rectT(0.93f, 0.9f, frame, Anchor.Center), style: null);

            GUIListBox prefabList = new GUIListBox(rectT(1f, 1f, paddedFrame, Anchor.BottomLeft))
            {
                UserData = "prefablist",
                AutoHideScrollBar = false,
                ScrollBarVisible = true,
            };

            foreach (var (itemPreview, itemFrame) in itemPreviews)
            {
                itemFrame.OutlineColor = itemFrame.Color = itemPreview == item  ? GUIStyle.Orange : previewWhite;
                itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
            }

            CreateSwappableItemSlideDown(prefabList, item);
        }

        public static GUIListBox CreateSwappableList(RectTransform rectTransform)
        {
            var upgradeCategoryList = new GUIListBox(rectTransform, style: null)
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = false,
                HideChildrenOutsideFrame = false,
                SmoothScroll = true,
                FadeElements = true,
                PadBottom = true,
                SelectTop = true,
                ClampScrollToElements = true,
                Spacing = 8,
                PlaySoundOnSelect = true
            };

            List<UpgradeCategory> categories = [];

            foreach (UpgradeCategory category in UpgradeCategory.Categories.OrderBy(static c => c.Name))
            {
                if (HasSwappableItems(category) && category.ShowInCustomizeTab)
                {
                    categories.Add(category);
                }
            }

            if (categories.None())
            {
                DebugConsole.ThrowError($"Error when creating {nameof(UpgradeStore)}: there are no categories to show in the Customize tab. "+
                    $"A category must have some items in it and {nameof(UpgradeCategory.ShowInCustomizeTab)} enabled for it to show in the customize tab.");
            }

            foreach (var category in categories)
            {
                var entitiesOnSub = GetSubItems().Where(i => (Submarine.MainSub?.IsEntityFoundOnThisSub(i, true) ?? false) && i is { IsHidden: false, AllowSwapping: true } && i.Prefab.SwappableItem != null && category.ItemTags.Any(i.HasTag)).ToList();

                foreach (Item item in entitiesOnSub)
                {
                    string tag = $"weaponslotformat.{item.Prefab?.SwappableItem?.SwapIdentifier.Value ?? "None"}";
                    (string, LocalizedString)[] variables = [("[number]", (entitiesOnSub.IndexOf(item) + 1).ToString()), ("[category]", category.Name)];
                    LocalizedString slotText = TextManager.GetWithVariables(tag, variables)
                                                          .Fallback(TextManager.GetWithVariables("weaponslotformat", variables));

                    var frameChild = new GUIFrame(rectT(1, 0.15f, upgradeCategoryList.Content), style: "UpgradeUIFrame")
                    {
                        UserData = item,
                        SelectedColor = new Color(101, 135, 121, 150),
                        GlowOnSelect = true
                    };

                    frameChild.DefaultColor = frameChild.Color;
                    frameChild.Color = Color.Transparent;

                    var weaponSwitchBg = new GUIButton(new RectTransform(new Vector2(0.65f), frameChild.RectTransform, Anchor.TopRight, scaleBasis: ScaleBasis.Smallest)
                    { RelativeOffset = new Vector2(0.04f, 0.0f) }, style: "WeaponSwitchTab")
                    {
                        CanBeSelected = false,
                        UserData = "customizebutton"
                    };
                    weaponSwitchBg.DefaultColor = weaponSwitchBg.Frame.DefaultColor = weaponSwitchBg.Color;
                    var weaponSwitchImg = new GUIImage(new RectTransform(new Vector2(0.7f), weaponSwitchBg.RectTransform, Anchor.Center), "WeaponSwitchIcon", scaleToFit: true)
                    {
                        CanBeFocused = false
                    };
                    weaponSwitchImg.DefaultColor = weaponSwitchImg.Color;

                    GUIFrame contentLayout = new GUIFrame(rectT(0.9f, 0.85f, frameChild, Anchor.Center));
                    GUILayoutGroup textLayout = new GUILayoutGroup(rectT(1, 1, contentLayout), childAnchor: Anchor.TopCenter);
                    var itemCategoryLabel = new GUITextBlock(rectT(1, 0.5f, textLayout), slotText, font: GUIStyle.SubHeadingFont);
                    var itemNameLabel = new GUITextBlock(rectT(1, 0.5f, textLayout), item.Name)
                    {
                        DefaultColor = GUIStyle.Green,
                        UserData = "itemname"
                    };

                    SetSwapItemName(item, itemNameLabel);

                    itemCategoryLabel.DefaultColor = itemCategoryLabel.TextColor;

                    itemCategoryLabel.Text = ToolBox.LimitString(slotText, itemCategoryLabel.Font, weaponSwitchBg.Rect.X - itemCategoryLabel.Rect.X);
                    if (!string.Equals(itemCategoryLabel.Text.SanitizedValue, slotText.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        itemCategoryLabel.ToolTip = slotText;
                    }
                }
            }

            return upgradeCategoryList;
        }

        private static void SetSwapItemName(Item item, GUITextBlock itemNameLabel)
        {
            if (item.PendingItemSwap is { } pending)
            {
                if (pending.Tags.Contains(Tags.Hardpoint))
                {
                    itemNameLabel.Text = TextManager.Get("weaponslotempty");
                    itemNameLabel.DefaultColor = GUIStyle.Orange;
                    return;
                }

                itemNameLabel.Text = pending.Name;
                itemNameLabel.DefaultColor = GUIStyle.Orange;
                return;
            }

            if (item.HasTag(Tags.Hardpoint))
            {
                itemNameLabel.Text = TextManager.Get("weaponslotempty");
                itemNameLabel.DefaultColor = GUIStyle.TextColorDim;
            }
            else
            {
                itemNameLabel.Text = item.Name;
                itemNameLabel.DefaultColor = GUIStyle.Green;
            }
        }

        private void CreateRepairsTab()
        {
            if (Campaign == null || storeLayout == null) { return; }

            highlightWalls = false;
            foreach (GUIComponent itemFrame in itemPreviews.Values)
            {
                itemFrame.OutlineColor = previewWhite;
            }

            currentStoreLayout = new GUIListBox(new RectTransform(new Vector2(1.2f, 1.5f), storeLayout.RectTransform) { MinSize = new Point(256, 0) }, style: null)
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = false,
                Spacing = 8
            };

            Location location = Campaign.Map.CurrentLocation;

            int hullRepairCost = CampaignMode.GetHullRepairCost();
            int itemRepairCost = CampaignMode.GetItemRepairCost();
            int shuttleRetrieveCost = CampaignMode.ShuttleReplaceCost;
            if (location != null)
            {
                hullRepairCost = location.GetAdjustedMechanicalCost(hullRepairCost);
                itemRepairCost = location.GetAdjustedMechanicalCost(itemRepairCost);
                shuttleRetrieveCost = location.GetAdjustedMechanicalCost(shuttleRetrieveCost);
            }

            CreateRepairEntry(currentStoreLayout.Content, TextManager.Get("repairallwalls"), "RepairHullButton", hullRepairCost, (button, o) =>
            {
                //cost is zero = nothing to repair
                if (Campaign.PurchasedHullRepairs || hullRepairCost <= 0)
                {
                    button.Enabled = false;
                    return false;
                }

                if (PlayerBalance >= hullRepairCost)
                {
                    LocalizedString body = TextManager.GetWithVariable("WallRepairs.PurchasePromptBody", "[amount]", hullRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (PlayerBalance >= hullRepairCost)
                        {
                            Campaign.TryPurchase(null, hullRepairCost);
                            GameAnalyticsManager.AddMoneySpentEvent(hullRepairCost, GameAnalyticsManager.MoneySink.Service, "hullrepairs");
                            Campaign.PurchasedHullRepairs = true;
                            button.Enabled = false;
                            SelectTab(UpgradeTab.Repairs);
                            GameMain.Client?.SendCampaignState();
                        }
                        else
                        {
                            button.Enabled = false;
                        }
                        return true;
                    }, overrideConfirmButtonSound: GUISoundType.ConfirmTransaction);
                }
                else
                {
                    button.Enabled = false;
                    return false;
                }
                return true;
            }, Campaign.PurchasedHullRepairs || !HasPermission || hullRepairCost <= 0, isHovered =>
            {
                highlightWalls = isHovered;
                return true;
            });

            CreateRepairEntry(currentStoreLayout.Content, TextManager.Get("repairallitems"), "RepairItemsButton", itemRepairCost, (button, o) =>
            {
                //cost is zero = nothing to repair
                if (PlayerBalance >= itemRepairCost && !Campaign.PurchasedItemRepairs && itemRepairCost > 0)
                {
                    LocalizedString body = TextManager.GetWithVariable("ItemRepairs.PurchasePromptBody", "[amount]", itemRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (PlayerBalance >= itemRepairCost && !Campaign.PurchasedItemRepairs)
                        {
                            Campaign.TryPurchase(null, itemRepairCost);
                            GameAnalyticsManager.AddMoneySpentEvent(hullRepairCost, GameAnalyticsManager.MoneySink.Service, "devicerepairs");
                            Campaign.PurchasedItemRepairs = true;
                            button.Enabled = false;
                            SelectTab(UpgradeTab.Repairs);
                            GameMain.Client?.SendCampaignState();
                        }
                        else
                        {
                            button.Enabled = false;
                        }
                        return true;
                    }, overrideConfirmButtonSound: GUISoundType.ConfirmTransaction);
                }
                else
                {
                    button.Enabled = false;
                    return false;
                }
                return true;
            }, Campaign.PurchasedItemRepairs || !HasPermission || itemRepairCost <= 0, isHovered =>
            {
                foreach (var (item, itemFrame) in itemPreviews)
                {
                    itemFrame.OutlineColor = itemFrame.Color = isHovered && item.GetComponent<DockingPort>() == null ? GUIStyle.Orange : previewWhite;
                }
                return true;
            });

            CreateRepairEntry(currentStoreLayout.Content, TextManager.Get("replacelostshuttles"), "ReplaceShuttlesButton", shuttleRetrieveCost, (button, o) =>
            {
                if (GameMain.GameSession?.SubmarineInfo != null &&
                    GameMain.GameSession.SubmarineInfo.LeftBehindSubDockingPortOccupied)
                {
                    new GUIMessageBox("", TextManager.Get("ReplaceShuttleDockingPortOccupied"));
                    return false;
                }

                if (PlayerBalance >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
                {
                    LocalizedString body = TextManager.GetWithVariable("ReplaceLostShuttles.PurchasePromptBody", "[amount]", shuttleRetrieveCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (PlayerBalance >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
                        {
                            Campaign.TryPurchase(null, shuttleRetrieveCost);
                            GameAnalyticsManager.AddMoneySpentEvent(hullRepairCost, GameAnalyticsManager.MoneySink.Service, "retrieveshuttle");
                            Campaign.PurchasedLostShuttles = true;
                            button.Enabled = false;
                            SelectTab(UpgradeTab.Repairs);
                            GameMain.Client?.SendCampaignState();
                        }
                        return true;
                    }, overrideConfirmButtonSound: GUISoundType.ConfirmTransaction);
                }
                else
                {
                    button.Enabled = false;
                    return false;
                }

                return true;
            }, Campaign.PurchasedLostShuttles || !HasPermission || GameMain.GameSession?.SubmarineInfo == null || !GameMain.GameSession.SubmarineInfo.SubsLeftBehind, isHovered =>
            {
                if (!isHovered) { return false; }
                if (!(GameMain.GameSession?.SubmarineInfo is { } subInfo)) { return false; }

                foreach (var (item, itemFrame) in itemPreviews)
                {
                    if (subInfo.LeftBehindDockingPortIDs.Contains(item.ID))
                    {
                        itemFrame.OutlineColor = itemFrame.Color = subInfo.BlockedDockingPortIDs.Contains(item.ID) ? GUIStyle.Red : GUIStyle.Green;
                    }
                    else
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                    }
                }
                return true;
            }, disableElement: true);
        }

        private void CreateRepairEntry(GUIComponent parent, LocalizedString title, string imageStyle, int price, GUIButton.OnClickedHandler onPressed, bool isDisabled, Func<bool, bool>? onHover = null, bool disableElement = false)
        {
            GUIFrame frameChild = new GUIFrame(rectT(new Point(parent.Rect.Width, (int) (96 * GUI.Scale)), parent), style: "UpgradeUIFrame");
            frameChild.SelectedColor = frameChild.Color;

            // Kinda hacky? idk, I don't see any other way to bring an Update() function to the campaign store.
            new GUICustomComponent(rectT(1, 1, frameChild), onUpdate: UpdateHover) { CanBeFocused = false };

            /*                  REPAIR ENTRY
             * |-------------------------------------------------|
             * |          |  repair title             |          |
             * |   icon   |---------------------------| buy btn. |
             * |          |  xxx mk                   |          |
             * |-------------------------------------------------|
             */
            GUILayoutGroup contentLayout = new GUILayoutGroup(rectT(0.9f, 0.85f, frameChild, Anchor.Center), isHorizontal: true);
                var repairIcon = new GUIFrame(rectT(new Point(contentLayout.Rect.Height, contentLayout.Rect.Height), contentLayout), style: imageStyle);
                GUILayoutGroup textLayout = new GUILayoutGroup(rectT(0.8f - repairIcon.RectTransform.RelativeSize.X, 1, contentLayout)) { Stretch = true };
                    new GUITextBlock(rectT(1, 0, textLayout), title, font: GUIStyle.SubHeadingFont) { CanBeFocused = false, AutoScaleHorizontal = true };
                    new GUITextBlock(rectT(1, 0, textLayout), TextManager.FormatCurrency(price));
                GUILayoutGroup buyButtonLayout = new GUILayoutGroup(rectT(0.2f, 1, contentLayout), childAnchor: Anchor.Center) { UserData = UpgradeStoreUserData.BuyButtonLayout };
                    new GUIButton(rectT(0.7f, 0.5f, buyButtonLayout), string.Empty, style: "RepairBuyButton") { Enabled = PlayerBalance >= price && !isDisabled, OnClicked = onPressed };
            contentLayout.Recalculate();
            buyButtonLayout.Recalculate();

            if (disableElement)
            {
                frameChild.Enabled = PlayerBalance >= price && !isDisabled;
            }

            if (!HasPermission)
            {
                frameChild.Enabled = false;
            }
            
            void UpdateHover(float deltaTime, GUICustomComponent component)
            {
                onHover?.Invoke(GUI.MouseOn != null && GUI.IsMouseOn(frameChild));
            }
        }

        public static GUIListBox CreateUpgradeCategoryList(RectTransform rectTransform)
        {
            var upgradeCategoryList = new GUIListBox(rectTransform, style: null)
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = false,
                HideChildrenOutsideFrame = false,
                SmoothScroll = true,
                FadeElements = true,
                PadBottom = true,
                SelectTop = true,
                ClampScrollToElements = true,
                Spacing = 8,
                PlaySoundOnSelect = true
            };

            Dictionary<UpgradeCategory, List<UpgradePrefab>> upgrades = new Dictionary<UpgradeCategory, List<UpgradePrefab>>();

            foreach (UpgradeCategory category in UpgradeCategory.Categories.OrderBy(c => c.Name))
            {
                foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs.OrderBy(p => p.Name))
                {
                    if (prefab.UpgradeCategories.Contains(category))
                    {
                        if (upgrades.ContainsKey(category))
                        {
                            upgrades[category].Add(prefab);
                        }
                        else
                        {
                            upgrades.Add(category, new List<UpgradePrefab> { prefab });
                        }
                    }
                }

                if (!upgrades.ContainsKey(category) && !category.ShowInCustomizeTab)
                {
                    upgrades.Add(category, new List<UpgradePrefab>());                    
                }
            }

            foreach (var (category, prefabs) in upgrades)
            {
                var frameChild = new GUIFrame(rectT(1, 0.15f, upgradeCategoryList.Content), style: "UpgradeUIFrame")
                {
                    UserData = new CategoryData(category, prefabs),
                    SelectedColor = new Color(101, 135, 121, 150),
                    GlowOnSelect = true
                };

                frameChild.DefaultColor = frameChild.Color;
                frameChild.Color = Color.Transparent;

                var weaponSwitchBg = new GUIButton(new RectTransform(new Vector2(0.65f), frameChild.RectTransform, Anchor.TopRight, scaleBasis: ScaleBasis.Smallest)
                { RelativeOffset = new Vector2(0.04f, 0.0f) }, style: "WeaponSwitchTab")
                {
                    Visible = false,
                    CanBeSelected = false,
                    UserData = "customizebutton"
                };
                weaponSwitchBg.DefaultColor = weaponSwitchBg.Frame.DefaultColor = weaponSwitchBg.Color;
                var weaponSwitchImg = new GUIImage(new RectTransform(new Vector2(0.7f), weaponSwitchBg.RectTransform, Anchor.Center), "WeaponSwitchIcon", scaleToFit: true)
                {
                    CanBeFocused = false
                };
                weaponSwitchImg.DefaultColor = weaponSwitchImg.Color;

                /*                     UPGRADE CATEGORY
                 * |--------------------------------------------------------|
                 * |                                                        |
                 * |  category title             |--------------------------|
                 * |                             |        indicators        |
                 * |-----------------------------|--------------------------|
                 */
                GUILayoutGroup contentLayout = new GUILayoutGroup(rectT(0.9f, 0.85f, frameChild, Anchor.Center));
                var itemCategoryLabel = new GUITextBlock(rectT(1, 1, contentLayout), category.Name, font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
                GUILayoutGroup indicatorLayout = new GUILayoutGroup(rectT(0.5f, 0.25f, contentLayout, Anchor.BottomRight), isHorizontal: true, childAnchor: Anchor.TopRight) { UserData = "indicators", IgnoreLayoutGroups = true, RelativeSpacing = 0.01f };

                foreach (var prefab in prefabs)
                {
                    GUIImage upgradeIndicator = new GUIImage(rectT(0.1f, 1f, indicatorLayout), style: "UpgradeIndicator", scaleToFit: true) { UserData = prefab, CanBeFocused = false };
                    upgradeIndicator.DefaultColor = upgradeIndicator.Color;
                    upgradeIndicator.Color = Color.Transparent;
                }

                itemCategoryLabel.DefaultColor = itemCategoryLabel.TextColor;
                itemCategoryLabel.TextColor = Color.Transparent;

                contentLayout.Recalculate();
                indicatorLayout.Recalculate();
            }

            return upgradeCategoryList;
        }

        private void CreateUpgradeTab()
        {
            if (storeLayout == null || mainStoreLayout == null) { return; }
            currentStoreLayout = CreateUpgradeCategoryList(rectT(1.0f, 1.5f, storeLayout));

            selectedUpgradeCategoryLayout = new GUIFrame(rectT(0.3f  * GUI.AspectRatioAdjustment, 1, mainStoreLayout), style: null) { CanBeFocused = false };

            RefreshUpgradeList();

            currentStoreLayout.OnSelected += (component, userData) =>
            {
                if (!component.Enabled) 
                {
                    selectedUpgradeCategoryLayout?.ClearChildren();
                    foreach (GUIComponent itemFrame in itemPreviews.Values)
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                        itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
                    }
                    return true;
                }

                if (userData is CategoryData categoryData && Submarine.MainSub is { } sub && categoryData.Prefabs is { } prefabs)
                {
                    TrySelectCategory(prefabs, categoryData.Category, sub);
                }

                return true;
            };
        }

        // This was supposed to have some logic for fancy animations to slide the previous tab out but maybe another time
        private void TrySelectCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine) => SelectUpgradeCategory(prefabs, category, submarine);

        private bool customizeTabOpen => selectedUpgradeTab is UpgradeTab.Customize;

        private static bool HasSwappableItems(UpgradeCategory category, List<Item>? subItems = null)
        {
            if (Submarine.MainSub == null) { return false; }
            subItems ??= GetSubItems();
            return subItems.Any(item => HasSwappableItems(category, item));
        }

        private static bool HasSwappableItems(UpgradeCategory category, Item item)
        {
            if (Submarine.MainSub == null) { return false; }
            return item.IsSwappable &&
                (item.Prefab.SwappableItem.IsAvailableForSwapping() || ItemPrefab.Prefabs.Any(ip => ip.SwappableItem?.ReplacementOnUninstall == item.Prefab.Identifier)) &&
                Submarine.MainSub.IsEntityFoundOnThisSub(item, true) && category.ItemTags.Any(item.HasTag);
        }

        private static List<Item> GetSubItems() => Submarine.MainSub?.GetItems(true) ?? new List<Item>();

        private void SelectUpgradeCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine)
        {
            if (selectedUpgradeCategoryLayout == null) { return; }

            bool hasUpgradeModules = prefabs.Count > 0;

            GUIComponent[] categoryFrames = GetFrames(category);
            foreach (GUIComponent itemFrame in itemPreviews.Values)
            {
                itemFrame.OutlineColor = itemFrame.Color = categoryFrames.Contains(itemFrame) ? GUIStyle.Orange : previewWhite;
                itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
            }

            highlightWalls = category.IsWallUpgrade;

            selectedUpgradeCategoryLayout.ClearChildren();
            GUIFrame frame = new GUIFrame(rectT(1.0f, 0.4f, selectedUpgradeCategoryLayout));
            GUIFrame paddedFrame = new GUIFrame(rectT(0.93f, 0.9f, frame, Anchor.Center), style: null);

            GUIListBox prefabList = new GUIListBox(rectT(1.0f, 1.0f, paddedFrame, Anchor.BottomLeft))
            {
                UserData = "prefablist",
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            //only either upgrade modules or swappable items -> just create the list
            CreateUpgradePrefabList(prefabList, category, prefabs, submarine);
        }

        private void CreateUpgradePrefabList(GUIListBox parent, UpgradeCategory category, List<UpgradePrefab> prefabs, Submarine submarine)
        {
            parent.Content.ClearChildren();
            List<Item>? entitiesOnSub = null;
            if (!category.IsWallUpgrade)
            {
                entitiesOnSub = submarine.GetItems(true).Where(i => submarine.IsEntityFoundOnThisSub(i, true)).ToList();
            }

            foreach (UpgradePrefab prefab in prefabs)
            {
                if (prefab.GetMaxLevelForCurrentSub() == 0) { continue; }
                CreateUpgradeEntry(prefab, category, parent.Content, submarine, entitiesOnSub);
            }
        }

        private void CreateSwappableItemList(GUIListBox parent, UpgradeCategory category, Submarine submarine)
        {
            parent.Content.ClearChildren();
            currentUpgradeCategory = category;
            var entitiesOnSub = submarine.GetItems(true).Where(i => submarine.IsEntityFoundOnThisSub(i, true) && !i.IsHidden && i.AllowSwapping && i.Prefab.SwappableItem != null && category.ItemTags.Any(t => i.HasTag(t))).ToList();

            foreach (Item item in entitiesOnSub)
            {
                CreateSwappableItemSlideDown(parent, item);
            }
        }

        private void CreateSwappableItemSlideDown(GUIListBox parent, Item item)
        {
            if (Campaign == null) { return; }

            IEnumerable<ItemPrefab> availableReplacements = ItemPrefab.Prefabs.Where(itemPrefab => item.CanBeSwappedTo(itemPrefab));

            var linkedItems = UpgradeManager.GetLinkedItemsToSwap(item) ?? new List<Item>() { item };
            //create the swap entry only for one of the items (the one with the smallest ID)
            if (linkedItems.Min(it => it.ID) < item.ID) { return; }

            var currentOrPending = item.PendingItemSwap ?? item.Prefab;
            LocalizedString name = currentOrPending.Name;
            LocalizedString nameWithQuantity = "";
            if (linkedItems.Count > 1)
            {
                foreach (ItemPrefab distinctItem in linkedItems.Select(it => it.Prefab).Distinct())
                {
                    if (nameWithQuantity != string.Empty)
                    {
                        nameWithQuantity += ", ";
                    }
                    int count = linkedItems.Count(it => it.Prefab == distinctItem);
                    nameWithQuantity += distinctItem.Name;
                    if (count > 1)
                    {
                        nameWithQuantity += " " + TextManager.GetWithVariable("campaignstore.quantity", "[amount]", count.ToString());
                    }
                }
            }
            else
            {
                nameWithQuantity = name;
            }

            const bool isOpen = true;

            List<GUIFrame> frames = [];
            bool canUninstall = HasPermission && (item.PendingItemSwap != null || !(currentOrPending.SwappableItem?.ReplacementOnUninstall.IsEmpty ?? true));

            bool isUninstallPending = item.Prefab.SwappableItem != null && item.PendingItemSwap?.Identifier == item.Prefab.SwappableItem.ReplacementOnUninstall;
            if (isUninstallPending) { canUninstall = false; }

            frames.Add(CreateUpgradeEntry(rectT(1f, 0.35f, parent.Content), currentOrPending.UpgradePreviewSprite, currentOrPending.ContentPackage,
                                          item.PendingItemSwap != null ? TextManager.GetWithVariable("upgrades.pendingitem", "[itemname]", name) : TextManager.GetWithVariable("upgrades.installeditem", "[itemname]", nameWithQuantity),
                                          currentOrPending.Description,
                                          0, item.Prefab, addBuyButton: canUninstall, addProgressBar: false, buttonStyle: "WeaponUninstallButton").Frame);

            if (canUninstall && frames.Last().FindChild(c => c is GUIButton, recursive: true) is GUIButton refundButton)
            {
                refundButton.Enabled = true;
                refundButton.OnClicked += (button, o) =>
                {
                    string textTag = item.PendingItemSwap != null ? "upgrades.cancelitemswappromptbody" : "upgrades.itemuninstallpromptbody";
                    if (isUninstallPending) { textTag = "upgrades.cancelitemuninstallpromptbody"; }
                    LocalizedString promptBody = TextManager.GetWithVariable(textTag, "[itemtouninstall]", isUninstallPending ? item.Name : currentOrPending.Name);
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("upgrades.refundprompttitle"), promptBody, () =>
                    {
                        if (GameMain.NetworkMember != null)
                        {
                            WaitForServerUpdate = true;
                        }
                        UpgradeManager.DebugLog($"Cancelling item swap on \"{item.Prefab.Name}\"");
                        Campaign?.UpgradeManager.CancelItemSwap(item);
                        GameMain.Client?.SendCampaignState();
                        return true;
                    });
                    return true;
                };
            }

            var dividerContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), parent.Content.RectTransform), style: null);
            new GUIFrame(new RectTransform(new Vector2(0.8f, 0.5f), dividerContainer.RectTransform, Anchor.Center), style: "HorizontalLine");
            frames.Add(dividerContainer);

            foreach (ItemPrefab replacement in availableReplacements)
            {
                if (replacement == currentOrPending) { continue; }

                bool isPurchased = item.AvailableSwaps.Contains(replacement);

                int price = isPurchased || replacement == item.Prefab ? 0 : replacement.SwappableItem.GetPrice(Campaign.Map?.CurrentLocation) * linkedItems.Count();

                frames.Add(CreateUpgradeEntry(rectT(1f, 0.35f, parent.Content), replacement.UpgradePreviewSprite, replacement.ContentPackage,
                    replacement.Name, replacement.Description,
                    price, replacement,
                    addBuyButton: true,
                    addProgressBar: false,
                    buttonStyle: isPurchased ? "WeaponInstallButton" : "StoreAddToCrateButton").Frame);

                if (!(frames.Last().FindChild(c => c is GUIButton, recursive: true) is GUIButton buyButton)) { continue; }

                if (HasPermission && PlayerBalance >= price)
                {
                    buyButton.Enabled = true;
                    buyButton.OnClicked += (button, o) =>
                    {
                        LocalizedString promptBody = TextManager.GetWithVariables(isPurchased ? "upgrades.itemswappromptbody" : "upgrades.purchaseitemswappromptbody",
                            ("[itemtoinstall]", replacement.Name),
                            ("[amount]", (replacement.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation) * linkedItems.Count()).ToString()));
                        currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get(price > 0 ? "Upgrades.PurchasePromptTitle" : "confirm"), promptBody, () =>
                        {
                            if (GameMain.NetworkMember != null)
                            {
                                WaitForServerUpdate = true;
                            }
                            UpgradeManager.DebugLog($"Swapping \"{item.Prefab.Name}\" with \"{replacement.Name}\"");
                            Campaign?.UpgradeManager.PurchaseItemSwap(item, replacement, isNetworkMessage: true);
                            GameMain.Client?.SendCampaignState();
                            return true;
                        });

                        return true;
                    };
                }
                else
                {
                    buyButton.Enabled = false;
                }
            }
        }

        public readonly record struct BuyButtonFrame(GUILayoutGroup Layout, GUIListBox MaterialCostList, GUIButton BuyButton, GUITextBlock PriceText);
        public readonly record struct ProgressBarFrame(GUITextBlock ProgressText, GUIProgressBar ProgressBar);

        public readonly record struct UpgradeFrame(GUIFrame Frame,
                                                    GUIImage Icon,
                                                    GUITextBlock Name,
                                                    GUITextBlock Description,
                                                    Option<BuyButtonFrame> BuyButton,
                                                    Option<ProgressBarFrame> ProgressBar);

        public static UpgradeFrame CreateUpgradeFrame(UpgradePrefab prefab, UpgradeCategory category, CampaignMode campaign, RectTransform rectTransform, bool addBuyButton = true)
        {
            int price = prefab.Price.GetBuyPrice(prefab, campaign.UpgradeManager.GetUpgradeLevel(prefab, category), campaign.Map?.CurrentLocation, characterList);
            return CreateUpgradeEntry(rectTransform, prefab.Sprite, prefab.ContentPackage, prefab.Name, prefab.Description, price, new CategoryData(category, prefab), addBuyButton, upgradePrefab: prefab, currentLevel: campaign.UpgradeManager.GetUpgradeLevel(prefab, category));
        }

        public static UpgradeFrame CreateUpgradeEntry(RectTransform parent, Sprite sprite, ContentPackage? contentPackage, LocalizedString title, LocalizedString body, int price, object? userData, bool addBuyButton = true, bool addProgressBar = true, string buttonStyle = "UpgradeBuyButton", UpgradePrefab? upgradePrefab = null, int currentLevel = 0)
        {
            float progressBarHeight = 0.25f;

            if (!addProgressBar)
            {
                progressBarHeight = 0f;
            }
            /*                        UPGRADE PREFAB ENTRY
             * |------------------------------------------------------------------|
             * |               | title                            |     price     |
             * |               |----------------------------------|_______________|
             * |     icon      | description                      |               |
             * |               |----------------------------------|    buy btn.   |
             * |               | progress bar             | x / y |               |
             * |------------------------------------------------------------------|
             */
            GUIFrame prefabFrame = new GUIFrame(parent, style: "ListBoxElement") { SelectedColor = Color.Transparent, UserData = userData };
                GUILayoutGroup mainLayout = new GUILayoutGroup(rectT(0.98f, 0.95f, prefabFrame, Anchor.Center), isHorizontal: false);
                    GUILayoutGroup prefabLayout = new GUILayoutGroup(rectT(1f, addBuyButton ? 0.65f : 1f, mainLayout, Anchor.Center), isHorizontal: true) { Stretch = true };
                        GUILayoutGroup imageLayout = new GUILayoutGroup(rectT(new Point(prefabLayout.Rect.Height, prefabLayout.Rect.Height), prefabLayout), childAnchor: Anchor.Center);
                            var icon = new GUIImage(rectT(0.9f, 0.9f, imageLayout, scaleBasis: ScaleBasis.BothHeight), sprite, scaleToFit: true) { CanBeFocused = false };
                        GUILayoutGroup textLayout = new GUILayoutGroup(rectT(1f - imageLayout.RectTransform.RelativeSize.X, 1, prefabLayout));
                            var name = new GUITextBlock(rectT(1, 0.35f, textLayout), RichString.Rich(title), font: GUIStyle.SubHeadingFont) { AutoScaleHorizontal = true, AutoScaleVertical = true, Padding = Vector4.Zero };
                            //name.RectTransform.MinSize = new Point(0, (int)name.TextSize.Y);
                            GUILayoutGroup descriptionLayout = new GUILayoutGroup(rectT(1, 0.75f - progressBarHeight, textLayout));
                                var description = new GUITextBlock(rectT(1, 1, descriptionLayout), body, font: GUIStyle.SmallFont, wrap: true, textAlignment: Alignment.TopLeft) { Padding = Vector4.Zero };
                                GUILayoutGroup? progressLayout = null;
                    GUILayoutGroup? buyButtonLayout = null;

            name.TextColor =
                contentPackage == ContentPackageManager.VanillaCorePackage || contentPackage == null ? GUIStyle.TextColorNormal : contentPackage.GetAccentColor();

            Option<BuyButtonFrame> buyButtonOption = Option<BuyButtonFrame>.None();
            Option<ProgressBarFrame> progressBarOption = Option<ProgressBarFrame>.None();

            if (addProgressBar)
            {
                progressLayout = new GUILayoutGroup(rectT(1, 0.25f, textLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft) { UserData = UpgradeStoreUserData.ProgressBarLayout };
                GUITextBlock progressText = new GUITextBlock(rectT(0.15f, 1, progressLayout), string.Empty, font: GUIStyle.SmallFont, textAlignment: Alignment.Center) { Padding = Vector4.Zero };
                GUIProgressBar progressBar = new GUIProgressBar(rectT(0.85f, 0.75f, progressLayout), 0.0f, GUIStyle.Orange);
                progressBarOption = Option.Some(new ProgressBarFrame(progressText, progressBar));
            }

            if (addBuyButton)
            {
                var formattedPrice = TextManager.FormatCurrency(Math.Abs(price));
                //negative price = refund
                if (price < 0) { formattedPrice = "+" + formattedPrice; }
                buyButtonLayout = new GUILayoutGroup(rectT(1f, 0.35f, mainLayout), isHorizontal: true) { UserData = UpgradeStoreUserData.BuyButtonLayout };;

                GUIListBox materialCostList;
                if (upgradePrefab is not null)
                {
                    var increaseText = new GUITextBlock(rectT(imageLayout.RectTransform.RelativeSize.X, 1f, buyButtonLayout), "", textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
                    {
                        UserData = UpgradeStoreUserData.IncreaseLabel
                    };
                    UpdateUpgradePercentageText(increaseText, upgradePrefab, currentLevel);
                    materialCostList = new GUIListBox(rectT(0.65f - imageLayout.RectTransform.RelativeSize.X, 1f, buyButtonLayout), isHorizontal: true, style: null);
                }
                else
                {
                    materialCostList = new GUIListBox(rectT(0.65f, 1f, buyButtonLayout), isHorizontal: true, style: null);
                }

                materialCostList.Visible = false;
                materialCostList.UserData = UpgradeStoreUserData.MaterialCostList;

                var priceText = new GUITextBlock(rectT(0.2f, 1f, buyButtonLayout), formattedPrice, textAlignment: Alignment.CenterRight)
                {
                    UserData = UpgradeStoreUserData.PriceLabel,
                    //prices on swappable items are always visible, upgrade prices are enabled in UpdateUpgradeEntry for purchasable upgrades
                    Visible = userData is ItemPrefab
                };

                if (price < 0)
                {
                    priceText.TextColor = GUIStyle.Green;
                }
                else if (price == 0)
                {
                    priceText.Text = string.Empty;
                }
                GUIButton buyButton = new GUIButton(rectT(0.15f, 1f, buyButtonLayout), string.Empty, style: buttonStyle)
                {
                    UserData = UpgradeStoreUserData.BuyButton,
                    Enabled = false
                };

                buyButtonOption = Option.Some(new BuyButtonFrame(buyButtonLayout, materialCostList, buyButton, priceText));
            }

            description.CalculateHeightFromText();
            // cut the description if it overflows and add a tooltip to it
            for (int i = 100; i > 0 && description.Rect.Height > descriptionLayout.Rect.Height; i--)
            {
                var lines = description.WrappedText.Split('\n');
                var newString = string.Join('\n', lines.Take(lines.Count - 1));
                if (0 >= newString.Length - 4) { break; }

                description.Text = newString.Substring(0, newString.Length - 4) + "...";
                description.CalculateHeightFromText();
                description.ToolTip = body;
            }

            // Recalculate everything to prevent jumping
            if (parent.Parent.GUIComponent is GUILayoutGroup group) { group.Recalculate(); }

            descriptionLayout.Recalculate();
            prefabLayout.Recalculate();
            imageLayout.Recalculate();
            textLayout.Recalculate();
            progressLayout?.Recalculate();
            buyButtonLayout?.Recalculate();

            return new UpgradeFrame(prefabFrame, icon, name, description, buyButtonOption, progressBarOption);
        }

        private static void UpdateUpgradePercentageText(GUITextBlock text, UpgradePrefab upgradePrefab, int currentLevel)
        {
            int maxLevel = upgradePrefab.GetMaxLevelForCurrentSub();
            float nextIncrease = upgradePrefab.IncreaseOnTooltip * Math.Min(currentLevel + 1, maxLevel);
            if (nextIncrease != 0f)
            {
                text.Text = $"{Math.Round(nextIncrease, 1)} %";
                if (currentLevel == maxLevel)
                {
                    text.TextColor = Color.Gray;
                }
            }
        }

        private void CreateUpgradeEntry(UpgradePrefab prefab, UpgradeCategory category, GUIComponent parent, Submarine submarine, List<Item>? itemsOnSubmarine)
        {
            Submarine? sub = GameMain.GameSession?.Submarine ?? Submarine.MainSub;
            if (Campaign is null || sub is null) { return; }

            UpgradeFrame prefabFrame = CreateUpgradeFrame(prefab, category, Campaign, rectT(1f, 0.4f, parent));

            if (!prefabFrame.BuyButton.TryUnwrap(out BuyButtonFrame buyButtonFrame)) { return; }

            if (!prefab.IsApplicable(submarine.Info) || (itemsOnSubmarine != null && !itemsOnSubmarine.Any(it => category.CanBeApplied(it, prefab))))
            {
                prefabFrame.Frame.Enabled = false;
                prefabFrame.Description.Enabled = false;
                prefabFrame.Name.Enabled = false;
                prefabFrame.Icon.Color = Color.Gray;
                buyButtonFrame.BuyButton.Enabled = false;
                buyButtonFrame.Layout.UserData = null; // prevent UpdateUpgradeEntry() from enabling the button
            }

            buyButtonFrame.BuyButton.OnClicked += (button, o) =>
            {
                LocalizedString promptBody = TextManager.GetWithVariables("Upgrades.PurchasePromptBody",
                    ("[upgradename]", prefab.Name),
                    ("[amount]", prefab.Price.GetBuyPrice(prefab, Campaign.UpgradeManager.GetUpgradeLevel(prefab, category), Campaign.Map?.CurrentLocation, characterList).ToString()));
                currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), promptBody, () =>
                {
                    if (Campaign.UpgradeManager.TryPurchaseUpgrade(prefab, category))
                    {
                        if (GameMain.NetworkMember != null)
                        {
                            WaitForServerUpdate = true;
                        }
                        GameMain.Client?.SendCampaignState();
                    }
                    return true;
                }, overrideConfirmButtonSound: GUISoundType.ConfirmTransaction);

                return true;
            };

            UpdateUpgradeEntry(prefabFrame.Frame, prefab, category, Campaign);
        }

        private void CreateItemTooltip(MapEntity entity)
        {
            int slotIndex = -1;
            if (entity is Item swappableItem && swappableItem.Prefab.SwappableItem != null)
            {
                var entitiesOnSub = Submarine.MainSub.GetItems(true).Where(i => i.Prefab.SwappableItem != null && Submarine.MainSub.IsEntityFoundOnThisSub(i, true) && i.Prefab.SwappableItem.SwapIdentifier == swappableItem.Prefab.SwappableItem?.SwapIdentifier).ToList();
                slotIndex = entitiesOnSub.IndexOf(entity) + 1;                
            }            

            GUITextBlock? itemName = ItemInfoFrame.FindChild("itemname", true) as GUITextBlock;
            GUIListBox? upgradeList = ItemInfoFrame.FindChild("upgradelist", true) as GUIListBox;
            GUITextBlock? moreIndicator = ItemInfoFrame.FindChild("moreindicator", true) as GUITextBlock;
            GUILayoutGroup layout = ItemInfoFrame.GetChild<GUILayoutGroup>();
            Debug.Assert(itemName != null && upgradeList != null && moreIndicator != null && layout != null, "One ore more tooltip elements not found");

            List<Upgrade> upgrades = entity.GetUpgrades();
            int upgradesCount = upgrades.Count;
            const int maxUpgrades = 4;

            Item? item = entity as Item;
            //item names are colorized individually, because the current and pending item may come from different content packages
            LocalizedString itemNameText = item != null ? GetColorizedItemName(item.Prefab) : TextManager.Get("upgradecategory.walls");
            if (slotIndex > -1)
            {
                itemNameText = TextManager.GetWithVariables("weaponslotwithname", ("[number]", slotIndex.ToString()), ("[weaponname]", itemNameText));
            }
            if (item?.PendingItemSwap != null)
            {
                itemNameText += "\n" + TextManager.GetWithVariable("upgrades.pendingitem", "[itemname]", GetColorizedItemName(item.PendingItemSwap));
            }
            itemName.Text = RichString.Rich(itemNameText);
            itemName.TextColor =
                item != null || entity.Prefab.ContentPackage == ContentPackageManager.VanillaCorePackage ? GUIStyle.TextColorNormal : entity.ContentPackage.GetAccentColor();
            ItemInfoFrame.RectTransform.MinSize = new Point(Math.Max((int)itemName.TextSize.X + GUI.IntScale(30), 250), 150);
            upgradeList.Content.ClearChildren();
            for (var i = 0; i < upgrades.Count && i < maxUpgrades; i++)
            {
                Upgrade upgrade = upgrades[i];
                new GUITextBlock(rectT(1, 0.25f, upgradeList.Content), CreateListEntry(upgrade.Prefab.Name, upgrade.Level)) { AutoScaleHorizontal = true, UserData = Tuple.Create(upgrade.Level, upgrade.Prefab) };
            }

            if (!(Campaign?.UpgradeManager is { } upgradeManager)) { return; }

            // include pending upgrades into the tooltip
            foreach (var (prefab, category, level) in upgradeManager.PendingUpgrades)
            {
                if (item != null && category.CanBeApplied(item, prefab) || entity is Structure && category.IsWallUpgrade)
                {
                    bool found = false;
                    foreach (GUITextBlock textBlock in upgradeList.Content.Children.Where(c => c is GUITextBlock).Cast<GUITextBlock>())
                    {
                        if (textBlock.UserData is Tuple<int, UpgradePrefab> tuple && tuple.Item2 == prefab)
                        {
                            var tooltip = CreateListEntry(tuple.Item2.Name, level + tuple.Item1);
                            textBlock.Text = tooltip;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        upgradesCount++;
                        if (upgradeList.Content.CountChildren < maxUpgrades)
                        {
                            new GUITextBlock(rectT(1, 0.25f, upgradeList.Content), CreateListEntry(prefab.Name, level)) { AutoScaleHorizontal = true };
                        }
                    }
                }
            }

            if (!upgradeList.Content.Children.Any())
            {
                new GUITextBlock(rectT(1, 0.25f, upgradeList.Content), TextManager.Get("UpgradeUITooltip.NoUpgradesElement")) { AutoScaleHorizontal = true };
            }

            moreIndicator.Text = upgradesCount > maxUpgrades ? TextManager.GetWithVariable("upgradeuitooltip.moreindicator", "[amount]", $"{upgradesCount - maxUpgrades}") : string.Empty;

            itemName.CalculateHeightFromText();
            moreIndicator.CalculateHeightFromText();
            layout.Recalculate();

            static LocalizedString CreateListEntry(LocalizedString name, int level) => TextManager.GetWithVariables("upgradeuitooltip.upgradelistelement", ("[upgradename]", name), ("[level]", $"{level}"));

            static LocalizedString GetColorizedItemName(ItemPrefab prefab) =>
                prefab.ContentPackage == ContentPackageManager.VanillaCorePackage ?
                    prefab.Name :
                    $"‖color:{ContentPackage.GetAccentColor(prefab.ContentPackage).ToStringHex()}‖{prefab.Name}‖end‖";
        }

        public static IEnumerable<UpgradeCategory> GetApplicableCategories(Submarine drawnSubmarine)
        {
            Item[] entitiesOnSub = drawnSubmarine.GetItems(true).Where(i => drawnSubmarine.IsEntityFoundOnThisSub(i, true)).ToArray();
            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                //hide categories with no upgrades or swappables in them
                bool hasSwappableItems = HasSwappableItems(category);
                if (!hasSwappableItems && UpgradePrefab.Prefabs.None(p => p.UpgradeCategories.Contains(category))) { continue; }
                if (hasSwappableItems || entitiesOnSub.Any(item => category.CanBeApplied(item, null)))
                {
                    yield return category;
                }
            }
        }

        private void UpdateSubmarinePreview(float deltaTime, GUICustomComponent parent)
        {
            if (Campaign == null) { return; }

            if (!parent.Children.Any() || 
                Submarine.MainSub != null && Submarine.MainSub != drawnSubmarine || 
                GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
            {
                GameMain.GameSession?.SubmarineInfo?.CheckSubsLeftBehind();
                drawnSubmarine = Submarine.MainSub;
                if (drawnSubmarine != null)
                {
                    CreateSubmarinePreview(drawnSubmarine, parent);
                    CreateHullBorderVerticies(drawnSubmarine, parent);

                    applicableCategories.Clear();
                    applicableCategories.AddRange(GetApplicableCategories(drawnSubmarine));
                }
                
                screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                // this might be a bit spaghetti, we use the submarine preview's Update() function to refresh the upgrade list when the submarine changes
                // we also need this when we first load in so we know which category entries to disable since the CampaignUI is created before the submarine is loaded in.
                RefreshAll();
            }
            if (needsRefresh)
            {
                RefreshAll();
            }

            // accept an active confirmation popup if any
            if (PlayerInput.KeyHit(Keys.Enter) && GUIMessageBox.MessageBoxes.Any())
            {
                for (int i = GUIMessageBox.MessageBoxes.Count - 1; i >= 0; i--)
                {
                    if (GUIMessageBox.MessageBoxes[i] is GUIMessageBox msgBox && msgBox == currectConfirmation)
                    {
                        // first button is the ok button
                        GUIButton? firstButton = msgBox.Buttons.FirstOrDefault();
                        if (firstButton is null) { continue; }

                        firstButton.OnClicked.Invoke(firstButton, firstButton.UserData);
                    }
                }
            }

            bool found = false;
            foreach (var (item, frame) in itemPreviews)
            {
                if (GUI.MouseOn == frame)
                {
                    if (HoveredEntity != item) { CreateItemTooltip(item); }
                    HoveredEntity = item;
                    if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradeTab is UpgradeTab.Upgrade or UpgradeTab.Customize && currentStoreLayout != null)
                    {
                        if (customizeTabOpen)
                        {
                            if (selectedUpgradeCategoryLayout != null)
                            {
                                ScrollToCustomizeElement(item);
                            }
                        }
                        else
                        {
                            ScrollToCategory(data => data.Category.CanBeApplied(item, null));
                        }
                    }
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                bool isMouseOnStructure = false;
                if (GUI.MouseOn == submarinePreviewComponent || GUI.MouseOn == subPreviewFrame)
                {
                    // Every wall should have the same upgrades so we can just display the first one in the tooltip
                    Structure? firstStructure = submarineWalls.FirstOrDefault();
                    // use pnpoly algorithm to detect if our mouse is within any of the hull polygons
                    if (subHullVertices.Any(hullVertex => ToolBox.PointIntersectsWithPolygon(PlayerInput.MousePosition, hullVertex)))
                    {
                        if (HoveredEntity != firstStructure && !(firstStructure is null)) { CreateItemTooltip(firstStructure); }
                        HoveredEntity = firstStructure;
                        isMouseOnStructure = true;
                        GUI.MouseCursor = CursorState.Hand;

                        if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradeTab == UpgradeTab.Upgrade && currentStoreLayout != null)
                        {
                            ScrollToCategory(data => data.Category.IsWallUpgrade, GUIListBox.PlaySelectSound.Yes);
                        }
                    }
                }

                if (!isMouseOnStructure) { HoveredEntity = null; }
            }

            // flip the tooltip if it is outside of the screen
            ItemInfoFrame.RectTransform.ScreenSpaceOffset = (PlayerInput.MousePosition + new Vector2(20, 20)).ToPoint();
            if (ItemInfoFrame.Rect.Right > GameMain.GraphicsWidth)
            {
                ItemInfoFrame.RectTransform.ScreenSpaceOffset = (PlayerInput.MousePosition - new Vector2(20 + ItemInfoFrame.Rect.Width, -20)).ToPoint();
            }
        }

        private void CreateSubmarinePreview(Submarine submarine, GUIComponent parent)
        {
            if (mainStoreLayout == null) { return; }

            if (submarineInfoFrame != null && mainStoreLayout == submarineInfoFrame.Parent)
            {
                mainStoreLayout.RemoveChild(submarineInfoFrame);
            }

            parent.ClearChildren();
            
            /*                 SUBMARINE INFO BOX
             * |--------------------------------------------------|
             * |                                             name |
             * |--------------------------------------------------|
             * |                                     class + tier |
             * |--------------------------------------------------|
             * | description                                      |
             * |                                                  |
             * |                                                  |
             * |--------------------------------------------------|
             */
            submarineInfoFrame = new GUILayoutGroup(rectT(0.25f, 0.2f, mainStoreLayout, Anchor.TopRight)) { IgnoreLayoutGroups = true };
            // submarine name
            new GUITextBlock(rectT(1, 0, submarineInfoFrame), submarine.Info.DisplayName, textAlignment: Alignment.Right, font: GUIStyle.LargeFont);

            LocalizedString classText = $"{TextManager.GetWithVariable("submarineclass.classsuffixformat", "[type]", TextManager.Get($"submarineclass.{submarine.Info.SubmarineClass}"))}";
            // submarine class + tier
            new GUITextBlock(rectT(1.0f, 0.15f, submarineInfoFrame), classText, textAlignment: Alignment.Right, font: GUIStyle.Font)
            {
                ToolTip = TextManager.Get("submarineclass.description") + "\n\n" + TextManager.Get($"submarineclass.{submarine.Info.SubmarineClass}.description")
            };
            new GUITextBlock(rectT(1.0f, 0.15f, submarineInfoFrame), TextManager.Get($"submarinetier.{submarine.Info.Tier}"), textAlignment: Alignment.Right, font: GUIStyle.Font)
            {
                ToolTip = TextManager.Get("submarinetier.description")
            };

            var description = new GUITextBlock(rectT(1, 0, submarineInfoFrame), submarine.Info.Description, textAlignment: Alignment.Right, wrap: true);
            submarineInfoFrame.RectTransform.ScreenSpaceOffset = new Point(0, (int)(16 * GUI.Scale));

            description.Padding = new Vector4(description.Padding.X, 24 * GUI.Scale, description.Padding.Z, description.Padding.W);
            List<Entity> pointsOfInterest = (from category in UpgradeCategory.Categories from item in submarine.GetItems(UpgradeManager.UpgradeAlsoConnectedSubs)
                                             where (category.CanBeApplied(item, null) || HasSwappableItems(category, item)) && item.IsPlayerTeamInteractable select item).Cast<Entity>().Distinct().ToList();

            List<ushort> ids = GameMain.GameSession.SubmarineInfo?.LeftBehindDockingPortIDs ?? new List<ushort>();
            pointsOfInterest.AddRange(submarine.GetItems(UpgradeManager.UpgradeAlsoConnectedSubs).Where(item => ids.Contains(item.ID)));

            submarine.CreateMiniMap(parent, pointsOfInterest, ignoreOutpost: true);
            subPreviewFrame = parent.GetChild<GUIFrame>();
            Rectangle dockedBorders = submarine.GetDockedBorders();
            GUIFrame hullContainer = parent.GetChild<GUIFrame>();
            if (hullContainer == null) { return; }
            itemPreviews.Clear();

            foreach (Entity entity in pointsOfInterest)
            {
                GUIComponent component = parent.FindChild(entity, true);
                if (component != null && entity is Item item)
                {
                    GUIComponent itemFrame; 
                    if (item.Prefab.UpgradePreviewSprite is { } icon)
                    {
                        float spriteSize = 128f * item.Prefab.UpgradePreviewScale;
                        Point size = new Point((int) (spriteSize * item.Scale / dockedBorders.Width * hullContainer.Rect.Width));
                        itemFrame = new GUIImage(rectT(size, component, Anchor.Center), icon, scaleToFit: true)
                        {
                            SelectedColor = GUIStyle.Orange,
                            Color = previewWhite,
                            HoverCursor = CursorState.Hand,
                            SpriteEffects = item.Rotation > 90.0f && item.Rotation < 270.0f ? SpriteEffects.FlipVertically : SpriteEffects.None
                        };
                        if (item.Prefab.SwappableItem != null)
                        {
                            new GUIImage(new RectTransform(new Vector2(0.8f), itemFrame.RectTransform, Anchor.TopLeft) { RelativeOffset = new Vector2(-0.2f) }, "WeaponSwitchIcon.DropShadow", scaleToFit: true)
                            {
                                SelectedColor = GUIStyle.Orange,
                                Color = previewWhite,
                                CanBeFocused = false
                            };
                        }
                    }
                    else
                    { 
                        Point size = new Point((int) (item.Rect.Width * item.Scale / dockedBorders.Width * hullContainer.Rect.Width), (int) (item.Rect.Height * item.Scale / dockedBorders.Height * hullContainer.Rect.Height));
                        itemFrame = new GUIFrame(rectT(size, component, Anchor.Center), style: "ScanLines")
                        {
                            SelectedColor = GUIStyle.Orange,
                            OutlineColor = previewWhite,
                            Color = previewWhite,
                            OutlineThickness = 2,
                            HoverCursor = CursorState.Hand
                        };
                    }

                    if (!itemPreviews.ContainsKey(item))
                    {
                        itemPreviews.Add(item, itemFrame);
                    }
                }
            }
        }

        /// <summary>
        /// Creates vertices for the submarine border that we use to draw it and check mouse collision 
        /// </summary>
        /// <param name="sub"></param>
        /// <param name="parent"></param>
        /// <remarks>
        /// Most of this code is copied from the status terminal but instead of drawing a line from X to Y
        /// we create a rotated rectangle instead and store the 4 corners into the array.
        /// </remarks>
        private void CreateHullBorderVerticies(Submarine sub, GUIComponent parent)
        {
            submarineWalls = sub.GetWalls(UpgradeManager.UpgradeAlsoConnectedSubs);
            const float lineWidth = 10;

            if (sub.HullVertices == null) { return; }

            Rectangle dockedBorders = sub.GetDockedBorders();
            dockedBorders.Location += sub.WorldPosition.ToPoint();

            float scale = Math.Min(parent.Rect.Width / (float)dockedBorders.Width, parent.Rect.Height / (float)dockedBorders.Height) * 0.9f;

            float displayScale = ConvertUnits.ToDisplayUnits(scale);
            Vector2 offset = (sub.WorldPosition - new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)) * scale;
            Vector2 center = parent.Rect.Center.ToVector2();

            subHullVertices = new Vector2[sub.HullVertices.Count][];

            for (int i = 0; i < sub.HullVertices.Count; i++)
            {
                Vector2 start = sub.HullVertices[i] * displayScale + offset;
                start.Y = -start.Y;
                Vector2 end = sub.HullVertices[(i + 1) % sub.HullVertices.Count] * displayScale + offset;
                end.Y = -end.Y;

                Vector2 edge = end - start;
                float length = edge.Length();
                float angle = (float)Math.Atan2(edge.Y, edge.X);
                Matrix rotate = Matrix.CreateRotationZ(angle);

                subHullVertices[i] = new[]
                {
                    center + start + Vector2.Transform(new Vector2(length, -lineWidth), rotate),
                    center + end + Vector2.Transform(new Vector2(-length, -lineWidth), rotate),
                    center + end + Vector2.Transform(new Vector2(-length, lineWidth), rotate),
                    center + start + Vector2.Transform(new Vector2(length, lineWidth), rotate),
                };
            }
        }

        private void DrawSubmarine(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            foreach (Vector2[] hullVertex in subHullVertices)
            {
                // calculate the center point so we can draw a line from X to Y instead of drawing a rotated rectangle that is filled
                Vector2 point1 = hullVertex[1] + (hullVertex[2] - hullVertex[1]) / 2;
                Vector2 point2 = hullVertex[0] + (hullVertex[3] - hullVertex[0]) / 2;
                GUI.DrawLine(spriteBatch, point1, point2, (highlightWalls ? GUIStyle.Orange * 0.6f : Color.DarkCyan * 0.3f), width: 10);
                if (GameMain.DebugDraw)
                {
                    // the "collision box" is a bit bigger than the line we draw so this can be useful data (maybe)
                    GUI.DrawRectangle(spriteBatch, hullVertex, Color.Red);
                }
            }
        }

        public static void UpdateUpgradeEntry(GUIComponent prefabFrame, UpgradePrefab prefab, UpgradeCategory category, CampaignMode campaign)
        {
            int currentLevel = campaign.UpgradeManager.GetUpgradeLevel(prefab, category);

            int maxLevel = prefab.GetMaxLevelForCurrentSub();
            LocalizedString progressText = TextManager.GetWithVariables("upgrades.progressformat", ("[level]", currentLevel.ToString()), ("[maxlevel]", maxLevel.ToString()));
            if (prefabFrame.FindChild(UpgradeStoreUserData.ProgressBarLayout, true) is { } progressParent)
            {
                GUIProgressBar bar = progressParent.GetChild<GUIProgressBar>();
                if (bar != null)
                {
                    bar.BarSize = currentLevel / (float)maxLevel;
                    bar.Color = currentLevel >= maxLevel ? GUIStyle.Green : GUIStyle.Orange;
                }

                GUITextBlock block = progressParent.GetChild<GUITextBlock>();
                if (block != null) { block.Text = progressText; }
            }

            if (prefabFrame.FindChild(UpgradeStoreUserData.BuyButtonLayout, true) is not { } buttonParent) { return; }

            GUITextBlock priceLabel = (GUITextBlock)buttonParent.FindChild(UpgradeStoreUserData.PriceLabel, recursive: true);
            priceLabel.Visible = true;
            int price = prefab.Price.GetBuyPrice(prefab, campaign.UpgradeManager.GetUpgradeLevel(prefab, category), campaign.Map?.CurrentLocation, characterList);

            if (!WaitForServerUpdate)
            {
                priceLabel.Text = TextManager.FormatCurrency(price);
                if (currentLevel >= maxLevel)
                {
                    priceLabel.Text = TextManager.Get("Upgrade.MaxedUpgrade");
                }
            }

            if (buttonParent.FindChild(UpgradeStoreUserData.IncreaseLabel, recursive: true) is GUITextBlock increaseLabel && !WaitForServerUpdate)
            {
                UpdateUpgradePercentageText(increaseLabel, prefab, currentLevel);
            }

            bool isMax = currentLevel >= maxLevel;

            if (buttonParent.FindChild(UpgradeStoreUserData.BuyButton, recursive: true) is GUIButton button)
            {
                bool canBuy = !WaitForServerUpdate && HasPermission && !isMax && campaign.GetBalance() >= price && prefab.HasResourcesToUpgrade(Character.Controlled, currentLevel + 1);

                button.Enabled = canBuy;
            }

            if (prefabFrame.FindChild(UpgradeStoreUserData.MaterialCostList, true) is GUIListBox itemList)
            {
                if (isMax)
                {
                    itemList.Visible = false;
                }
                else
                {
                    CreateMaterialCostsElement(itemList, prefab.ResourceCosts, currentLevel + 1);
                }
            }
        }

        public static void CreateMaterialCostsElement(GUIListBox list, IEnumerable<UpgradeResourceCost> resourceCosts, int? targetLevel = null, bool clearList = true)
        {
            if (clearList) { list.Content.ClearChildren(); }
            var allItems = CargoManager.FindAllItemsOnPlayerAndAllowedSubs(Character.Controlled);

            var resources = UpgradeResourceCost.GetApplicableResources(resourceCosts, targetLevel);

            foreach (ApplicableResourceCollection collection in resources)
            {
                list.Visible = true;

                int length = collection.MatchingItems.Length;

                if (length is 0) { continue; }

                ItemPrefab defaultItemPrefab = collection.MatchingItems.First();

                GUILayoutGroup wrapperLayout = new GUILayoutGroup(rectT(0.25f, 1f, list.Content));

                GUIFrame itemFrame = new GUIFrame(rectT(1f, 1f, wrapperLayout), style: null)
                {
                    ToolTip = defaultItemPrefab.Name
                };

                bool hasItems = collection.Cost.Amount <= allItems.Count(collection.Cost.MatchesItem);

                Sprite? icon = defaultItemPrefab.InventoryIcon ?? defaultItemPrefab.Sprite;
                Color iconColor = defaultItemPrefab.InventoryIcon is null ? defaultItemPrefab.SpriteColor : defaultItemPrefab.InventoryIconColor;

                GUIImage itemIcon = new GUIImage(new RectTransform(Vector2.One, itemFrame.RectTransform, scaleBasis: ScaleBasis.Smallest, anchor: Anchor.Center), sprite: icon, scaleToFit: true)
                {
                    Color = hasItems ? iconColor : iconColor * 0.9f,
                    CanBeFocused = false
                };

                // item count text
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), itemIcon.RectTransform, anchor: Anchor.BottomRight), $"{collection.Count}", font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
                {
                    Shadow = true,
                    CanBeFocused = false,
                    Padding = Vector4.Zero,
                    TextColor = hasItems ? GUIStyle.TextColorBright : GUIStyle.Red,
                    UserData = "itemcounttext"
                };

                if (length is 1) { continue; }

                // we have more than 1 item, show a "slideshow" of the items

                float index = 0f;
                GUICustomComponent customComponent = new GUICustomComponent(rectT(1f, 1f, itemFrame), null, (deltaTime, component) =>
                {
                    index += deltaTime / 3f;
                    if (index > length) { index = 0; }

                    ItemPrefab currentPrefab = collection.MatchingItems[(int)MathF.Floor(index)];
                    Sprite? icon = currentPrefab.InventoryIcon ?? currentPrefab.Sprite;
                    Color iconColor = currentPrefab.InventoryIcon is null ? currentPrefab.SpriteColor : currentPrefab.InventoryIconColor;
                    itemIcon.Sprite = icon;
                    itemIcon.Color = hasItems ? iconColor : iconColor * 0.9f;
                    itemFrame.ToolTip = currentPrefab.Name;
                })
                {
                    CanBeFocused = false
                };
            }
        }

        private static void UpdateCategoryIndicators(
            GUIComponent indicators,
            GUIComponent parent,
            List<UpgradePrefab> prefabs,
            UpgradeCategory category,
            CampaignMode campaign,
            Submarine? drawnSubmarine,
            IEnumerable<UpgradeCategory> applicableCategories)
        {
            // Disables the parent and only re-enables if the submarine contains valid items
            if (!category.IsWallUpgrade && drawnSubmarine?.Info != null)
            {
                if (UpgradePrefab.Prefabs.None(p => p.UpgradeCategories.Contains(category) && p.GetMaxLevel(drawnSubmarine.Info) > 0) && !HasSwappableItems(category))
                {
                    parent.ToolTip = TextManager.Get("upgradecategorynotapplicable");
                    parent.Enabled = false;
                    parent.SelectedColor = GUIStyle.Red * 0.5f;
                }
                else if (applicableCategories.Contains(category))
                {
                    parent.Enabled = true;
                    parent.SelectedColor = parent.Style.SelectedColor;
                }
                else
                {
                    parent.Enabled = false;
                    parent.SelectedColor = GUIStyle.Red * 0.5f;
                }
            }

            foreach (GUIComponent component in indicators.Children)
            {
                if (component is not GUIImage image) { continue; }

                foreach (UpgradePrefab prefab in prefabs)
                {
                    if (component.UserData != prefab) { continue; }

                    int maxLevel = prefab.GetMaxLevelForCurrentSub();
                    if (maxLevel == 0)
                    {
                        component.Visible = false;
                        continue;
                    }

                    Dictionary<Identifier, GUIComponentStyle> styles = GUIStyle.GetComponentStyle("upgradeindicator").ChildStyles;
                    if (!styles.ContainsKey("upgradeindicatoron") || !styles.ContainsKey("upgradeindicatordim") || !styles.ContainsKey("upgradeindicatoroff")) { continue; }

                    GUIComponentStyle onStyle  = styles["upgradeindicatoron".ToIdentifier()];
                    GUIComponentStyle dimStyle = styles["upgradeindicatordim".ToIdentifier()];
                    GUIComponentStyle offStyle = styles["upgradeindicatoroff".ToIdentifier()];

                    if (campaign.UpgradeManager.GetUpgradeLevel(prefab, category) >= maxLevel)
                    {
                        // we check this to avoid flickering from re-applying the same style
                        if (image.Style == onStyle) { continue; }
                        image.ApplyStyle(onStyle);
                    }
                    else if (campaign.UpgradeManager.GetUpgradeLevel(prefab, category) > 0)
                    {
                        if (image.Style == dimStyle) { continue; }
                        image.ApplyStyle(dimStyle);
                    }
                    else
                    {
                        SetOff();
                    }

                    void SetOff()
                    {
                        if (image.Style == offStyle) { return; }
                        image.ApplyStyle(offStyle);
                    }
                }
            }
        }

        private void ScrollToCategory(Predicate<CategoryData> predicate, GUIListBox.PlaySelectSound playSelectSound = GUIListBox.PlaySelectSound.No)
        {
            if (currentStoreLayout == null) { return; }

            CategoryData? mostAppropriateCategory = null;
            GUIComponent? mostAppropriateChild = null;
            foreach (GUIComponent child in currentStoreLayout.Content.Children)
            {
                if (child.UserData is CategoryData data && predicate(data))
                {
                    //choose the category with least items in it as the "most appropriate"
                    //e.g. when selecting junction boxes, we want to select the "junction boxes" category instead of "electrical repairs" which contains many electrical devices
                    if (mostAppropriateCategory == null || 
                        data.Category.ItemTags.Count() < mostAppropriateCategory.Value.Category.ItemTags.Count())
                    {
                        mostAppropriateCategory = data;
                        mostAppropriateChild = child;
                    }
                }
            }
            if (mostAppropriateChild != null)
            {
                currentStoreLayout.ScrollToElement(mostAppropriateChild, playSelectSound);
            }
        }

        private void ScrollToCustomizeElement(Item item)
        {
            if (currentStoreLayout == null) { return; }

            foreach (GUIComponent child in currentStoreLayout.Content.Children)
            {
                if (child.UserData is Item it && it == item)
                {
                    currentStoreLayout.ScrollToElement(child);
                    break;
                }
            }
        }

        /// <summary>
        /// Gets all "points of interest" GUIFrames on the upgrade preview interface that match the corresponding upgrade category.
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        private GUIComponent[] GetFrames(UpgradeCategory category)
        {
            List<GUIComponent> frames = new List<GUIComponent>();
            foreach (var (item, guiFrame) in itemPreviews)
            {
                if (category.CanBeApplied(item, null))
                {
                    frames.Add(guiFrame);
                }
            }

            return frames.ToArray();
        }

        private static bool HasPermission => CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageCampaign);

        // just a shortcut to create new RectTransforms since all the new RectTransform and new Vector2 confuses my IDE (and me)
        private static RectTransform rectT(float x, float y, GUIComponent parentComponent, Anchor anchor = Anchor.TopLeft, ScaleBasis scaleBasis = ScaleBasis.Normal)
        {
            return new RectTransform(new Vector2(x, y), parentComponent.RectTransform, anchor, scaleBasis: scaleBasis);
        }
        
        private static RectTransform rectT(Point point, GUIComponent parentComponent, Anchor anchor = Anchor.TopLeft)
        {
            return new RectTransform(point, parentComponent.RectTransform, anchor);
        }
    }
}