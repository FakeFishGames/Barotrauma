#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

// ReSharper disable UnusedVariable

namespace Barotrauma
{
    
    internal class UpgradeStore
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
        private int AvailableMoney => Campaign?.Money ?? 0;
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

        public MapEntity? HoveredItem;
        private bool highlightWalls;

        private UpgradeCategory? currentUpgradeCategory;
        private GUIButton? activeItemSwapSlideDown;

        private readonly Dictionary<Item, GUIComponent> itemPreviews = new Dictionary<Item, GUIComponent>();

        private static readonly Color previewWhite = Color.White * 0.5f;
        
        private Point screenResolution;

        /// <summary>
        /// While set to true any call to <see cref="RefreshUpgradeList"/> will cause the buy button to be disabled and to not update the prices.
        /// This is to prevent us from buying another upgrade before the server has given us the new prices and causing potential syncing issues.
        /// </summary>
        public static bool WaitForServerUpdate;

        private enum UpgradeTab
        {
            Upgrade,
            Repairs
        }

        public UpgradeStore(CampaignUI campaignUI, GUIComponent parent)
        {
            WaitForServerUpdate = false;
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
            Campaign.UpgradeManager.OnUpgradesChanged += RefreshAll;
            Campaign.CargoManager.OnPurchasedItemsChanged += RefreshAll;
            Campaign.CargoManager.OnSoldItemsChanged += RefreshAll;
        }

        public void RefreshAll()
        {
            switch (selectedUpgradeTab)
            {
                case UpgradeTab.Repairs:
                    SelectTab(UpgradeTab.Repairs);
                    break;
                case UpgradeTab.Upgrade:
                    RefreshUpgradeList();
                    foreach (var itemPreview in itemPreviews)
                    {
                        if (!(itemPreview.Value is GUIImage image) || itemPreview.Key == null) { continue; }
                        if (itemPreview.Key.PendingItemSwap == null)
                        {
                            image.Sprite = itemPreview.Key.Prefab.UpgradePreviewSprite;
                        }
                        else if (itemPreview.Key.PendingItemSwap.UpgradePreviewSprite != null)
                        {
                            image.Sprite = itemPreview.Key.PendingItemSwap.UpgradePreviewSprite;
                        }
                    }
                    break;
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
                    if (activeItemSwapSlideDown?.UserData is Item prevOpenedItem)
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
            foreach (GUIComponent component in categoryList.Content.Children)
            {
                if (!(component.UserData is CategoryData data)) { continue; }
                if (component.FindChild("indicators", true) is { } indicators && data.Prefabs != null)
                {
                    // ReSharper disable once PossibleMultipleEnumeration
                    UpdateCategoryIndicators(indicators, component, data.Prefabs, data.Category, campaign, drawnSubmarine, applicableCategories);
                }
                var customizeButton = component.FindChild("customizebutton", true);
                if (customizeButton != null)
                {
                    customizeButton.Visible = HasSwappableItems(data.Category);
                }
            }

            // reset the order first
            foreach (UpgradeCategory category in UpgradeCategory.Categories)
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
            new GUITextBlock(rectT(1, 0, tooltipLayout), string.Empty, font: GUI.SubHeadingFont) { UserData = "itemname" };
            new GUITextBlock(rectT(1, 0, tooltipLayout), TextManager.Get("UpgradeUITooltip.UpgradeListHeader"));
            new GUIListBox(rectT(1, 0.5f, tooltipLayout), style: null) { ScrollBarVisible = false, AutoHideScrollBar = false, SmoothScroll = true, UserData = "upgradelist"};
            new GUITextBlock(rectT(1, 0, tooltipLayout), string.Empty) { UserData = "moreindicator" };
            ItemInfoFrame.Children.ForEach(c => { c.CanBeFocused = false; c.Children.ForEach(c2 => c2.CanBeFocused = false); });

            GUIFrame paddedLayout = new GUIFrame(rectT(0.95f, GUI.IsFourByThree() ? 0.98f : 0.95f, parent, Anchor.Center), style: null);
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
            GUILayoutGroup leftLayout = new GUILayoutGroup(rectT(0.5f, 1, topHeaderLayout)) { RelativeSpacing = 0.05f };
                GUILayoutGroup locationLayout = new GUILayoutGroup(rectT(1, 0.5f, leftLayout), isHorizontal: true);
                    GUIImage submarineIcon = new GUIImage(rectT(new Point(locationLayout.Rect.Height, locationLayout.Rect.Height), locationLayout), style: "SubmarineIcon", scaleToFit: true);
                    new GUITextBlock(rectT(1.0f - submarineIcon.RectTransform.RelativeSize.X, 1, locationLayout), TextManager.Get("UpgradeUI.Title"), font: GUI.LargeFont);
                categoryButtonLayout = new GUILayoutGroup(rectT(0.4f, 0.3f, leftLayout), isHorizontal: true) { Stretch = true };
                    GUIButton upgradeButton = new GUIButton(rectT(1, 1f, categoryButtonLayout), TextManager.Get("UICategory.Upgrades"), style: "GUITabButton") { UserData = UpgradeTab.Upgrade, Selected = selectedUpgradeTab == UpgradeTab.Upgrade };
                    GUIButton repairButton = new GUIButton(rectT(1, 1f, categoryButtonLayout), TextManager.Get("UICategory.Maintenance"), style: "GUITabButton") { UserData = UpgradeTab.Repairs, Selected = selectedUpgradeTab == UpgradeTab.Repairs };

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
                GUILayoutGroup priceLayout = new GUILayoutGroup(rectT(1, 0.8f, rightLayout), childAnchor: Anchor.Center) { RelativeSpacing = 0.08f };
                    new GUITextBlock(rectT(1f, 0f, priceLayout), TextManager.Get("CampaignStore.Balance"), font: GUI.SubHeadingFont, textAlignment: Alignment.Right);
                    new GUITextBlock(rectT(1f, 0f, priceLayout), FormatCurrency(AvailableMoney, format: true), font: GUI.SubHeadingFont, textAlignment: Alignment.Right) { TextGetter = () => FormatCurrency(AvailableMoney, format: true) };
                new GUIFrame(rectT(0.5f, 0.1f, rightLayout, Anchor.BottomRight), style: "HorizontalLine") { IgnoreLayoutGroups = true };

            repairButton.OnClicked = upgradeButton.OnClicked = (button, o) =>
            {
                if (o is UpgradeTab upgradeTab)
                {
                    if (upgradeTab != selectedUpgradeTab || currentStoreLayout == null || currentStoreLayout.Parent != storeLayout)
                    {
                        selectedUpgradeTab = upgradeTab;
                        SelectTab(selectedUpgradeTab);
                        storeLayout?.Recalculate();
                    }

                    repairButton.Selected = (UpgradeTab) repairButton.UserData == selectedUpgradeTab;
                    upgradeButton.Selected = (UpgradeTab) upgradeButton.UserData == selectedUpgradeTab;

                    return true;
                }

                return false;
            };

            // submarine preview
            submarinePreviewComponent = new GUICustomComponent(rectT(0.75f, 0.75f, mainStoreLayout, Anchor.BottomRight), onUpdate: UpdateSubmarinePreview, onDraw: DrawSubmarine)
            {
                IgnoreLayoutGroups = true
            };

            SelectTab(UpgradeTab.Upgrade);

            var itemSwapPreview = new GUICustomComponent(new RectTransform(new Vector2(0.27f, 0.4f), mainStoreLayout.RectTransform, Anchor.TopLeft) { RelativeOffset = new Vector2(GUI.IsFourByThree() ? 0.5f : 0.47f, 0.0f) }, DrawItemSwapPreview)
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = true
            };

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
                activeItemSwapSlideDown?.UserData as Item ?? HoveredItem as Item : 
                HoveredItem as Item;
            if (selectedItem?.Prefab.SwappableItem == null) { return; }

            Sprite schematicsSprite = selectedItem.Prefab.SwappableItem.SchematicSprite;
            if (schematicsSprite == null) { return; }
            float schematicsScale = Math.Min(component.Rect.Width / 2 / schematicsSprite.size.X, component.Rect.Height / schematicsSprite.size.Y);
            Vector2 center = new Vector2(component.Rect.Center.X, component.Rect.Center.Y);
            schematicsSprite.Draw(spriteBatch, new Vector2(component.Rect.X, center.Y), GUI.Style.Green, new Vector2(0, schematicsSprite.size.Y / 2), 
                scale: schematicsScale);

            var swappableItemList = selectedUpgradeCategoryLayout?.FindChild("prefablist", true) as GUIListBox;
            var highlightedElement = swappableItemList?.Content.FindChild(c => c.UserData is ItemPrefab && c.IsParentOf(GUI.MouseOn)) ?? GUI.MouseOn;
            ItemPrefab swapTo = highlightedElement?.UserData as ItemPrefab ?? selectedItem.PendingItemSwap;
            if (swapTo?.SwappableItem == null) { return; }
            Sprite? schematicsSprite2 = swapTo.SwappableItem?.SchematicSprite;
            schematicsSprite2?.Draw(spriteBatch, new Vector2(component.Rect.Right, center.Y), GUI.Style.Orange, new Vector2(schematicsSprite2.size.X, schematicsSprite2.size.Y / 2),
                scale: Math.Min(component.Rect.Width / 2 / schematicsSprite2.size.X, component.Rect.Height / schematicsSprite2.size.Y));

            var arrowSprite = GUI.Style?.GetComponentStyle("GUIButtonToggleRight")?.GetDefaultSprite();
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
            int hullRepairCost      = location?.GetAdjustedMechanicalCost(CampaignMode.HullRepairCost)     ?? CampaignMode.HullRepairCost;
            int itemRepairCost      = location?.GetAdjustedMechanicalCost(CampaignMode.ItemRepairCost)     ?? CampaignMode.ItemRepairCost;
            int shuttleRetrieveCost = location?.GetAdjustedMechanicalCost(CampaignMode.ShuttleReplaceCost) ?? CampaignMode.ShuttleReplaceCost;

            CreateRepairEntry(currentStoreLayout.Content, TextManager.Get("repairallwalls"), "RepairHullButton", hullRepairCost, (button, o) =>
            {
                if (Campaign.PurchasedHullRepairs)
                {
                    button.Enabled = false;
                    return false;
                }

                if (AvailableMoney >= hullRepairCost)
                {
                    string body = TextManager.GetWithVariable("WallRepairs.PurchasePromptBody", "[amount]", hullRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (AvailableMoney >= hullRepairCost)
                        {
                            Campaign.Money -= hullRepairCost;
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
                    });
                }
                else
                {
                    button.Enabled = false;
                    return false;
                }
                return true;
            }, Campaign.PurchasedHullRepairs || !HasPermission, isHovered =>
            {
                highlightWalls = isHovered;
                return true;
            });

            CreateRepairEntry(currentStoreLayout.Content, TextManager.Get("repairallitems"), "RepairItemsButton", itemRepairCost, (button, o) =>
            {
                if (AvailableMoney >= itemRepairCost && !Campaign.PurchasedItemRepairs)
                {
                    string body = TextManager.GetWithVariable("ItemRepairs.PurchasePromptBody", "[amount]", itemRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (AvailableMoney >= itemRepairCost && !Campaign.PurchasedItemRepairs)
                        {
                            Campaign.Money -= itemRepairCost;
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
                    });
                }
                else
                {
                    button.Enabled = false;
                    return false;
                }

                return true;
            }, Campaign.PurchasedItemRepairs || !HasPermission, isHovered =>
            {
                foreach (var (item, itemFrame) in itemPreviews)
                {
                    itemFrame.OutlineColor = itemFrame.Color = isHovered && item.GetComponent<DockingPort>() == null ? GUI.Style.Orange : previewWhite;
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

                if (AvailableMoney >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
                {
                    string body = TextManager.GetWithVariable("ReplaceLostShuttles.PurchasePromptBody", "[amount]", shuttleRetrieveCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (AvailableMoney >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
                        {
                            Campaign.Money -= shuttleRetrieveCost;
                            Campaign.PurchasedLostShuttles = true;
                            button.Enabled = false;
                            SelectTab(UpgradeTab.Repairs);
                            GameMain.Client?.SendCampaignState();
                        }
                        return true;
                    });
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
                        itemFrame.OutlineColor = itemFrame.Color = subInfo.BlockedDockingPortIDs.Contains(item.ID) ? GUI.Style.Red : GUI.Style.Green;
                    }
                    else
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                    }
                }
                return true;
            }, disableElement: true);
        }

        private void CreateRepairEntry(GUIComponent parent, string title, string imageStyle, int price, GUIButton.OnClickedHandler onPressed, bool isDisabled, Func<bool, bool>? onHover = null, bool disableElement = false)
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
                    new GUITextBlock(rectT(1, 0, textLayout), title, font: GUI.SubHeadingFont) { CanBeFocused = false, AutoScaleHorizontal = true };
                    new GUITextBlock(rectT(1, 0, textLayout), FormatCurrency(price));
                GUILayoutGroup buyButtonLayout = new GUILayoutGroup(rectT(0.2f, 1, contentLayout), childAnchor: Anchor.Center) { UserData = "buybutton" };
                    new GUIButton(rectT(0.7f, 0.5f, buyButtonLayout), string.Empty, style: "RepairBuyButton") { ClickSound = GUISoundType.HireRepairClick, Enabled = AvailableMoney >= price && !isDisabled, OnClicked = onPressed };
            contentLayout.Recalculate();
            buyButtonLayout.Recalculate();

            if (disableElement)
            {
                frameChild.Enabled = AvailableMoney >= price && !isDisabled;
            }

            if (!HasPermission)
            {
                frameChild.Enabled = false;
            }
            
            void UpdateHover(float deltaTime, GUICustomComponent component)
            {
                onHover?.Invoke(GUI.MouseOn != null && frameChild.IsParentOf(GUI.MouseOn) || GUI.MouseOn == frameChild);
            }
        }

        //TODO: put this somewhere else
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
                Spacing = 8
            };

            Dictionary<UpgradeCategory, List<UpgradePrefab>> upgrades = new Dictionary<UpgradeCategory, List<UpgradePrefab>>();

            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                foreach (UpgradePrefab prefab in UpgradePrefab.Prefabs)
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
            }

            foreach (var (category, prefabs) in upgrades)
            {
                var frameChild = new GUIFrame(rectT(1, 0.15f, upgradeCategoryList.Content), style: "UpgradeUIFrame")
                {
                    UserData = new CategoryData(category, prefabs),
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
                var itemCategoryLabel = new GUITextBlock(rectT(1, 1, contentLayout), category.Name, font: GUI.SubHeadingFont) { CanBeFocused = false };
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

            selectedUpgradeCategoryLayout = new GUIFrame(rectT(GUI.IsFourByThree() ? 0.3f : 0.25f, 1, mainStoreLayout), style: null) { CanBeFocused = false };

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

                var customizeCategoryButton = selectedUpgradeCategoryLayout?.FindChild("customizebutton", recursive: true) as GUIButton;
                customizeCategoryButton?.OnClicked(customizeCategoryButton, customizeCategoryButton.UserData);
                
                return true;
            };
        }

        // This was supposed to have some logic for fancy animations to slide the previous tab out but maybe another time
        private void TrySelectCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine) => SelectUpgradeCategory(prefabs, category, submarine);

        private bool customizeTabOpen;

        private static bool HasSwappableItems(UpgradeCategory category)
        {
            if (Submarine.MainSub == null) { return false; }
            return Submarine.MainSub.GetItems(true).Any(i =>
                i.Prefab.SwappableItem != null &&
                !i.HiddenInGame && i.AllowSwapping &&
                (i.Prefab.SwappableItem.CanBeBought || ItemPrefab.Prefabs.Any(ip => ip.SwappableItem?.ReplacementOnUninstall == i.Prefab.Identifier)) &&
                Submarine.MainSub.IsEntityFoundOnThisSub(i, true) && category.ItemTags.Any(t => i.HasTag(t)));
        }

        private void SelectUpgradeCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine)
        {
            if (selectedUpgradeCategoryLayout == null) { return; }

            customizeTabOpen = false;

            GUIComponent[] categoryFrames = GetFrames(category);
            foreach (GUIComponent itemFrame in itemPreviews.Values)
            {
                itemFrame.OutlineColor = itemFrame.Color = categoryFrames.Contains(itemFrame) ? GUI.Style.Orange : previewWhite;
                itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
            }

            highlightWalls = category.IsWallUpgrade;

            selectedUpgradeCategoryLayout.ClearChildren();
            GUIFrame frame = new GUIFrame(rectT(1.0f, 0.4f, selectedUpgradeCategoryLayout));
            GUIFrame paddedFrame = new GUIFrame(rectT(0.93f, 0.9f, frame, Anchor.Center), style: null);

            bool hasSwappableItems = HasSwappableItems(category);

            float listHeight = hasSwappableItems ? 0.9f : 1.0f;

            GUIListBox prefabList = new GUIListBox(rectT(1.0f, listHeight, paddedFrame, Anchor.BottomLeft))
            {
                UserData = "prefablist",
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            if (hasSwappableItems)
            {
                GUILayoutGroup buttonLayout = new GUILayoutGroup(rectT(1.0f, 0.1f, paddedFrame, anchor: Anchor.TopLeft), isHorizontal: true);

                GUIButton customizeButton = new GUIButton(rectT(0.5f, 1f, buttonLayout), text: TextManager.Get("uicategory.customize"), style: "GUITabButton")
                {
                    UserData = "customizebutton"
                };
                new GUIImage(new RectTransform(new Vector2(1.0f, 0.75f), customizeButton.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.Smallest) { RelativeOffset = new Vector2(0.015f, 0.0f) }, "WeaponSwitchIcon", scaleToFit: true);
                customizeButton.TextBlock.RectTransform.RelativeSize = new Vector2(0.7f, 1.0f);

                GUIButton upgradeButton = new GUIButton(rectT(0.5f, 1f, buttonLayout), text: TextManager.Get("uicategory.upgrades"), style: "GUITabButton")
                {
                    Selected = true
                };

                GUITextBlock.AutoScaleAndNormalize(upgradeButton.TextBlock, customizeButton.TextBlock);

                upgradeButton.OnClicked = delegate
                {
                    customizeTabOpen = false;
                    customizeButton.Selected = false;
                    upgradeButton.Selected = true;
                    CreateUpgradePrefabList(prefabList, category, prefabs, submarine);
                    GUIComponent[] categoryFrames = GetFrames(category);
                    foreach (GUIComponent itemFrame in itemPreviews.Values)
                    {
                        itemFrame.OutlineColor = itemFrame.Color = categoryFrames.Contains(itemFrame) ? GUI.Style.Orange : previewWhite;
                        itemFrame.Children.ForEach(c => c.Color = itemFrame.Color);
                    }
                    return true;
                };

                customizeButton.OnClicked = delegate
                {
                    customizeTabOpen = true;
                    customizeButton.Selected = true;
                    upgradeButton.Selected = false;
                    CreateSwappableItemList(prefabList, category, submarine);
                    return true;
                };
            }

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
                CreateUpgradeEntry(prefab, category, parent.Content, entitiesOnSub);
            }
        }

        private void CreateSwappableItemList(GUIListBox parent, UpgradeCategory category, Submarine submarine)
        {
            parent.Content.ClearChildren();
            currentUpgradeCategory = category;
            var entitiesOnSub = submarine.GetItems(true).Where(i => submarine.IsEntityFoundOnThisSub(i, true) && !i.HiddenInGame && i.AllowSwapping && i.Prefab.SwappableItem != null && category.ItemTags.Any(t => i.HasTag(t))).ToList();

            foreach (Item item in entitiesOnSub)
            {
                CreateSwappableItemSlideDown(parent, item, entitiesOnSub, submarine);
            }
        }

        private void CreateSwappableItemSlideDown(GUIListBox parent, Item item, List<Item> swappableEntities, Submarine submarine)
        {
            if (Campaign == null || submarine == null) { return; }

            IEnumerable<ItemPrefab> availableReplacements = MapEntityPrefab.List.Where(p =>
                p is ItemPrefab itemPrefab &&
                itemPrefab.SwappableItem != null &&
                itemPrefab.SwappableItem.CanBeBought &&
                itemPrefab.SwappableItem.SwapIdentifier.Equals(item.Prefab.SwappableItem.SwapIdentifier, StringComparison.OrdinalIgnoreCase)).Cast<ItemPrefab>();

            var linkedItems = Campaign.UpgradeManager.GetLinkedItemsToSwap(item) ?? new List<Item>() { item };
            //create the swap entry only for one of the items (the one with the smallest ID)
            if (linkedItems.Min(it => it.ID) < item.ID) { return; }

            var currentOrPending = item.PendingItemSwap ?? item.Prefab;
            string name = currentOrPending.Name;
            string quantityText = "";
            if (linkedItems.Count > 1)
            {
                quantityText = " " + TextManager.GetWithVariable("campaignstore.quantity", "[amount]", (linkedItems.Count).ToString());
                name += quantityText;
            }

            bool isOpen = false;
            GUIButton toggleButton = new GUIButton(rectT(1f, 0.1f, parent.Content), text: string.Empty, style: "SlideDown")
            {
                UserData = item
            };
            GUILayoutGroup buttonLayout = new GUILayoutGroup(rectT(1f, 1f, toggleButton.Frame), isHorizontal: true);

            string slotText = "";
            if (linkedItems.Count > 1)
            {
                slotText = TextManager.GetWithVariable("weaponslot", "[number]", string.Join(", ", linkedItems.Select(it => (swappableEntities.IndexOf(it) + 1).ToString())));
            }
            else
            {
                slotText = TextManager.GetWithVariable("weaponslot", "[number]", (swappableEntities.IndexOf(item) + 1).ToString());
            }

            new GUITextBlock(rectT(0.3f, 1f, buttonLayout), text: slotText, font: GUI.SubHeadingFont);
            GUILayoutGroup group = new GUILayoutGroup(rectT(0.7f, 1f, buttonLayout), isHorizontal: true) { Stretch = true };

            string title = item.PendingItemSwap != null ? TextManager.GetWithVariable("upgrades.pendingitem", "[itemname]", name) : (linkedItems.Count > 1 ? item.Name + quantityText : item.Name);
            GUITextBlock text = new GUITextBlock(rectT(0.7f, 1f, group), text: title, font: GUI.SubHeadingFont, textAlignment: Alignment.Right, parseRichText: true)
            {
                TextColor = GUI.Style.Orange
            };
            GUIImage arrowImage = new GUIImage(rectT(0.5f, 1f, group, scaleBasis: ScaleBasis.BothHeight), style: "SlideDownArrow", scaleToFit: true);

            group.Recalculate();
            if (text.TextSize.X > text.Rect.Width)
            {
                text.ToolTip = text.Text;
                text.Text = ToolBox.LimitString(text.Text, text.Font, text.Rect.Width);
            }

            List<GUIFrame> frames = new List<GUIFrame>();
            if (currentOrPending != null)
            {
                bool canUninstall = item.PendingItemSwap != null || !string.IsNullOrEmpty(currentOrPending.SwappableItem?.ReplacementOnUninstall);

                bool isUninstallPending = item.Prefab.SwappableItem != null && item.PendingItemSwap?.Identifier == item.Prefab.SwappableItem.ReplacementOnUninstall;
                if (isUninstallPending) { canUninstall = false; }

                frames.Add(CreateUpgradeEntry(rectT(1f, 0.25f, parent.Content), currentOrPending.UpgradePreviewSprite,
                                TextManager.GetWithVariable(item.PendingItemSwap != null ? "upgrades.pendingitem" : "upgrades.installeditem", "[itemname]", name),
                                currentOrPending.Description,
                                0, null, addBuyButton: canUninstall, addProgressBar: false, buttonStyle: "WeaponUninstallButton"));

                if (canUninstall && frames.Last().FindChild(c => c is GUIButton, recursive: true) is GUIButton refundButton)
                {
                    refundButton.Enabled = true;
                    refundButton.OnClicked += (button, o) =>
                    {
                        string textTag = item.PendingItemSwap != null ? "upgrades.cancelitemswappromptbody" : "upgrades.itemuninstallpromptbody";
                        if (isUninstallPending) { textTag = "upgrades.cancelitemuninstallpromptbody"; }
                        string promptBody = TextManager.GetWithVariables(textTag,
                            new[] { "[itemtouninstall]" },
                            new[] { isUninstallPending ? item.Name : currentOrPending.Name });
                        currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("upgrades.refundprompttitle"), promptBody, () =>
                        {
                            if (GameMain.NetworkMember != null)
                            {
                                WaitForServerUpdate = true;
                            }
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
            }

            foreach (ItemPrefab replacement in availableReplacements)
            {
                if (replacement == currentOrPending) { continue; }

                bool isPurchased = item.AvailableSwaps.Contains(replacement);

                int price = isPurchased || replacement == item.Prefab ? 0 : replacement.SwappableItem.GetPrice(Campaign.Map?.CurrentLocation) * linkedItems.Count;

                frames.Add(CreateUpgradeEntry(rectT(1f, 0.25f, parent.Content), replacement.UpgradePreviewSprite, replacement.Name, replacement.Description, 
                    price, replacement, 
                    addBuyButton: true, 
                    addProgressBar: false,
                    buttonStyle: isPurchased ? "WeaponInstallButton" : "StoreAddToCrateButton"));

                if (!(frames.Last().FindChild(c => c is GUIButton, recursive: true) is GUIButton buyButton)) { continue; }
                if (Campaign.Money >= price)
                {
                    buyButton.Enabled = true;
                    buyButton.OnClicked += (button, o) =>
                    {
                        string promptBody = TextManager.GetWithVariables(isPurchased ? "upgrades.itemswappromptbody" : "upgrades.purchaseitemswappromptbody", 
                            new[] { "[itemtoinstall]", "[amount]" }, 
                            new[] { replacement.Name, (replacement.SwappableItem.GetPrice(Campaign?.Map?.CurrentLocation) * linkedItems.Count).ToString() });
                        currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), promptBody, () =>
                        {
                            if (GameMain.NetworkMember != null)
                            {
                                WaitForServerUpdate = true;
                            }
                            if (item.Prefab == replacement && item.PendingItemSwap != null)
                            {
                                Campaign?.UpgradeManager.CancelItemSwap(item);
                            }
                            else
                            {
                                Campaign?.UpgradeManager.PurchaseItemSwap(item, replacement);
                            }
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

            foreach (GUIFrame frame in frames)
            {
                frame.Visible = false;
            }

            toggleButton.OnClicked = delegate
            {
                if (Campaign == null) { return false; }
                isOpen = !isOpen;
                toggleButton.Selected = !toggleButton.Selected;
                foreach (GUIFrame frame in frames)
                {
                    frame.Visible = toggleButton.Selected;
                }
                if (toggleButton.Selected)
                {
                    var linkedItems = Campaign.UpgradeManager.GetLinkedItemsToSwap(item);
                    foreach (var itemPreview in itemPreviews)
                    {
                        itemPreview.Value.OutlineColor = itemPreview.Value.Color = linkedItems.Contains(itemPreview.Key) ? GUI.Style.Orange : previewWhite;
                    }
                    foreach (GUIComponent otherComponent in toggleButton.Parent.Children)
                    {
                        if (otherComponent == toggleButton || frames.Contains(otherComponent)) { continue; }
                        if (otherComponent is GUIButton otherButton)
                        {
                            var otherArrowImage = otherComponent.FindChild(c => c is GUIImage, recursive: true);
                            otherArrowImage.SpriteEffects = SpriteEffects.None;
                            otherButton.Selected = false;
                        }
                        else
                        {
                            otherComponent.Visible = false;
                        }
                    }
                }
                else
                {
                    foreach (var itemPreview in itemPreviews)
                    {
                        if (currentStoreLayout?.SelectedData is CategoryData categoryData && !categoryData.Category.ItemTags.Any(t => itemPreview.Key.HasTag(t))) { continue; }
                        itemPreview.Value.OutlineColor = itemPreview.Value.Color = GUI.Style.Orange;
                    }
                }
                activeItemSwapSlideDown = toggleButton.Selected ? toggleButton : null;
                arrowImage.SpriteEffects = toggleButton.Selected ? SpriteEffects.FlipVertically : SpriteEffects.None;
                parent.RecalculateChildren();
                parent.UpdateScrollBarSize();
                return true;
            };
        }

        public static GUIFrame CreateUpgradeFrame(UpgradePrefab prefab, UpgradeCategory category, CampaignMode campaign, RectTransform rectTransform, bool addBuyButton = true)
        {
            int price = prefab.Price.GetBuyprice(campaign.UpgradeManager.GetUpgradeLevel(prefab, category), campaign.Map?.CurrentLocation);
            return CreateUpgradeEntry(rectTransform, prefab.Sprite, prefab.Name, prefab.Description, price, new CategoryData(category, prefab), addBuyButton);
        }

        public static GUIFrame CreateUpgradeEntry(RectTransform parent, Sprite sprite, string title, string body, int price, object? userData, bool addBuyButton = true, bool addProgressBar = true, string buttonStyle = "UpgradeBuyButton")
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
                GUILayoutGroup prefabLayout = new GUILayoutGroup(rectT(0.98f, 0.95f, prefabFrame, Anchor.Center), isHorizontal: true) { Stretch = true };
                    GUILayoutGroup imageLayout = new GUILayoutGroup(rectT(new Point(prefabLayout.Rect.Height, prefabLayout.Rect.Height), prefabLayout), childAnchor: Anchor.Center);
                        var icon = new GUIImage(rectT(0.9f, 0.9f, imageLayout, scaleBasis: ScaleBasis.BothHeight), sprite, scaleToFit: true) { CanBeFocused = false };
                    GUILayoutGroup textLayout = new GUILayoutGroup(rectT(0.8f - imageLayout.RectTransform.RelativeSize.X, 1, prefabLayout));
                        var name = new GUITextBlock(rectT(1, 0.25f, textLayout), title, font: GUI.SubHeadingFont, parseRichText: true) { AutoScaleHorizontal = true, AutoScaleVertical = true, Padding = Vector4.Zero };
                        GUILayoutGroup descriptionLayout = new GUILayoutGroup(rectT(1, 0.75f - progressBarHeight, textLayout));
                            var description = new GUITextBlock(rectT(1, 1, descriptionLayout), body, font: GUI.SmallFont, wrap: true, textAlignment: Alignment.TopLeft) { Padding = Vector4.Zero };
                            GUILayoutGroup? progressLayout = null;
                    GUILayoutGroup? buyButtonLayout = null;

            if (addProgressBar)
            { 
                progressLayout = new GUILayoutGroup(rectT(1, 0.25f, textLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft) { UserData = "progressbar" };
                new GUIProgressBar(rectT(0.8f, 0.75f, progressLayout), 0.0f, GUI.Style.Orange);
                new GUITextBlock(rectT(0.2f, 1, progressLayout), string.Empty, font: GUI.SmallFont, textAlignment: Alignment.Center) { Padding = Vector4.Zero };
            }

            if (addBuyButton)
            {
                string formattedPrice = FormatCurrency(Math.Abs(price));
                //negative price = refund
                if (price < 0) { formattedPrice = "+" + formattedPrice; }
                buyButtonLayout = new GUILayoutGroup(rectT(0.2f, 1, prefabLayout), childAnchor: Anchor.TopCenter) { UserData = "buybutton" };
                var priceText = new GUITextBlock(rectT(1, 0.4f, buyButtonLayout), formattedPrice, textAlignment: Alignment.Center);
                if (price < 0)
                {
                    priceText.TextColor = GUI.Style.Green;
                }
                else if (price == 0)
                {
                    priceText.Text = string.Empty;
                }
                new GUIButton(rectT(0.7f, 0.5f, buyButtonLayout), string.Empty, style: buttonStyle) { Enabled = false };
            }

            description.CalculateHeightFromText();
            // cut the description if it overflows and add a tooltip to it
            for (int i = 100; i > 0 && description.Rect.Height > descriptionLayout.Rect.Height; i--)
            {
                string[] lines = description.WrappedText.Split('\n');
                var newString = string.Join('\n', lines.Take(lines.Length - 1));
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

            return prefabFrame;
        }

        private void CreateUpgradeEntry(UpgradePrefab prefab, UpgradeCategory category, GUIComponent parent, List<Item>? itemsOnSubmarine)
        {
            if (Campaign is null) { return; }

            GUIFrame prefabFrame = CreateUpgradeFrame(prefab, category, Campaign, rectT(1f, 0.25f, parent));
                var prefabLayout = prefabFrame.GetChild<GUILayoutGroup>();
                    GUILayoutGroup[] childLayouts = prefabLayout.GetAllChildren<GUILayoutGroup>().ToArray();
                    var imageLayout = childLayouts[0];
                        var icon = imageLayout.GetChild<GUIImage>();
                    var textLayout = childLayouts[1];
                        var name = textLayout.GetChild<GUITextBlock>();
                        GUILayoutGroup[] textChildLayouts = textLayout.GetAllChildren<GUILayoutGroup>().ToArray();
                            var descriptionLayout = textChildLayouts[0];
                                var description = descriptionLayout.GetChild<GUITextBlock>();
                            var progressLayout = textChildLayouts[1];
                    var buyButtonLayout = childLayouts[2];
                        var buyButton = buyButtonLayout.GetChild<GUIButton>();

            if (!HasPermission || (itemsOnSubmarine != null && !itemsOnSubmarine.Any(it => category.CanBeApplied(it, prefab))))
            {
                prefabFrame.Enabled = false;
                description.Enabled = false;
                name.Enabled = false;
                icon.Color = Color.Gray;
                buyButton.Enabled = false;
                buyButtonLayout.UserData = null; // prevent UpdateUpgradeEntry() from enabling the button
            }

            buyButton.OnClicked += (button, o) =>
            {
                string promptBody = TextManager.GetWithVariables("Upgrades.PurchasePromptBody", new []{ "[upgradename]", "[amount]"}, new []{ prefab.Name, prefab.Price.GetBuyprice(Campaign.UpgradeManager.GetUpgradeLevel(prefab, category), Campaign.Map?.CurrentLocation).ToString() });
                currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), promptBody, () =>
                {
                    if (GameMain.NetworkMember != null)
                    {
                        WaitForServerUpdate = true;
                    }
                    Campaign.UpgradeManager.PurchaseUpgrade(prefab, category);
                    GameMain.Client?.SendCampaignState();
                    return true;
                });

                return true;
            };

            UpdateUpgradeEntry(prefabFrame, prefab, category, Campaign);
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
            
            itemName.Text = entity is Item ? entity.Name : TextManager.Get("upgradecategory.walls");
            if (slotIndex > -1)
            {
                itemName.Text = TextManager.GetWithVariables("weaponslotwithname", new string[] { "[number]", "[weaponname]" }, new string[] { slotIndex.ToString(), itemName.Text });
            }
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
                if (entity is Item item && category.CanBeApplied(item, prefab) || entity is Structure && category.IsWallUpgrade)
                {
                    bool found = false;
                    foreach (GUITextBlock textBlock in upgradeList.Content.Children.Where(c => c is GUITextBlock).Cast<GUITextBlock>())
                    {
                        if (textBlock.UserData is Tuple<int, UpgradePrefab> tuple && tuple.Item2 == prefab)
                        {
                            string tooltip = CreateListEntry(tuple.Item2.Name, level + tuple.Item1);
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

            static string CreateListEntry(string name, int level) => TextManager.GetWithVariables("upgradeuitooltip.upgradelistelement", new[] { "[upgradename]", "[level]" }, new[] { name, $"{level}" });
        }

        public static IEnumerable<UpgradeCategory> GetApplicableCategories(Submarine drawnSubmarine)
        {
            Item[] entitiesOnSub = drawnSubmarine.GetItems(true).Where(i => drawnSubmarine.IsEntityFoundOnThisSub(i, true)).ToArray();
            foreach (UpgradeCategory category in UpgradeCategory.Categories)
            {
                if (entitiesOnSub.Any(item => category.CanBeApplied(item, null)))
                {
                    yield return category;
                }
            }
        }

        private void UpdateSubmarinePreview(float deltaTime, GUICustomComponent parent)
        {
            if (Campaign == null) { return; }

            if (!parent.Children.Any() || Submarine.MainSub != null && Submarine.MainSub != drawnSubmarine || GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
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
                    if (HoveredItem != item) { CreateItemTooltip(item); }
                    HoveredItem = item;
                    if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradeTab == UpgradeTab.Upgrade && currentStoreLayout != null)
                    {
                        if (customizeTabOpen)
                        {
                            if (selectedUpgradeCategoryLayout != null)
                            {
                                var linkedItems = HoveredItem is Item ? Campaign.UpgradeManager.GetLinkedItemsToSwap((Item)HoveredItem) : new List<Item>();
                                if (selectedUpgradeCategoryLayout.FindChild(c => c.UserData as Item == HoveredItem || linkedItems.Contains(c.UserData as Item), recursive: true) is GUIButton itemElement)
                                {
                                    if (!itemElement.Selected) { itemElement.OnClicked(itemElement, itemElement.UserData); }
                                    (itemElement.Parent?.Parent?.Parent as GUIListBox)?.ScrollToElement(itemElement);
                                }
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
                        if (HoveredItem != firstStructure && !(firstStructure is null)) { CreateItemTooltip(firstStructure); }
                        HoveredItem = firstStructure;
                        isMouseOnStructure = true;
                        GUI.MouseCursor = CursorState.Hand;

                        if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradeTab == UpgradeTab.Upgrade && currentStoreLayout != null)
                        {
                            ScrollToCategory(data => data.Category.IsWallUpgrade);
                        }
                    }
                }

                if (!isMouseOnStructure) { HoveredItem = null; }
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
             * |                                            class |
             * |--------------------------------------------------|
             * | description                                      |
             * |                                                  |
             * |                                                  |
             * |--------------------------------------------------|
             */
            submarineInfoFrame = new GUILayoutGroup(rectT(0.25f, 0.2f, mainStoreLayout, Anchor.TopRight)) { IgnoreLayoutGroups = true };
            // submarine name
            new GUITextBlock(rectT(1, 0, submarineInfoFrame), submarine.Info.DisplayName, textAlignment: Alignment.Right, font: GUI.LargeFont);
            // submarine class
            new GUITextBlock(rectT(1, 0, submarineInfoFrame), $"{TextManager.GetWithVariable("submarineclass.classsuffixformat", "[type]", TextManager.Get($"submarineclass.{submarine.Info.SubmarineClass}"))}", textAlignment: Alignment.Right, font: GUI.Font);
            var description = new GUITextBlock(rectT(1, 0, submarineInfoFrame), submarine.Info.Description, textAlignment: Alignment.Right, wrap: true);
            submarineInfoFrame.RectTransform.ScreenSpaceOffset = new Point(0, (int)(16 * GUI.Scale));
            
            description.Padding = new Vector4(description.Padding.X, 24 * GUI.Scale, description.Padding.Z, description.Padding.W);
            List<Entity> pointsOfInterest = (from category in UpgradeCategory.Categories from item in submarine.GetItems(UpgradeManager.UpgradeAlsoConnectedSubs) where category.CanBeApplied(item, null) && item.IsPlayerTeamInteractable select item).Cast<Entity>().ToList();

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
                            SelectedColor = GUI.Style.Orange,
                            Color = previewWhite,
                            HoverCursor = CursorState.Hand,
                            SpriteEffects = item.Rotation > 90.0f && item.Rotation < 270.0f ? SpriteEffects.FlipVertically : SpriteEffects.None
                        };
                        if (item.Prefab.SwappableItem != null)
                        {
                            new GUIImage(new RectTransform(new Vector2(0.8f), itemFrame.RectTransform, Anchor.TopLeft) { RelativeOffset = new Vector2(-0.2f) }, "WeaponSwitchIcon.DropShadow", scaleToFit: true)
                            {
                                SelectedColor = GUI.Style.Orange,
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
                            SelectedColor = GUI.Style.Orange,
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
                GUI.DrawLine(spriteBatch, point1, point2, (highlightWalls ? GUI.Style.Orange * 0.6f : Color.DarkCyan * 0.3f), width: 10);
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

            string progressText = TextManager.GetWithVariables("upgrades.progressformat", new[] { "[level]", "[maxlevel]" }, new[] { currentLevel.ToString(), prefab.MaxLevel.ToString() });
            if (prefabFrame.FindChild("progressbar", true) is { } progressParent)
            {
                GUIProgressBar bar = progressParent.GetChild<GUIProgressBar>();
                if (bar != null)
                {
                    bar.BarSize = currentLevel / (float) prefab.MaxLevel;
                    bar.Color = currentLevel >= prefab.MaxLevel ? GUI.Style.Green : GUI.Style.Orange;
                }

                GUITextBlock block = progressParent.GetChild<GUITextBlock>();
                if (block != null) { block.Text = progressText; }
            }

            if (prefabFrame.FindChild("buybutton", true) is { } buttonParent)
            {
                GUITextBlock priceLabel = buttonParent.GetChild<GUITextBlock>();
                int price = prefab.Price.GetBuyprice(campaign.UpgradeManager.GetUpgradeLevel(prefab, category), campaign.Map?.CurrentLocation);

                if (priceLabel != null && !WaitForServerUpdate)
                {
                    priceLabel.Text = FormatCurrency(price);
                    if (currentLevel >= prefab.MaxLevel)
                    {
                        priceLabel.Text = TextManager.Get("Upgrade.MaxedUpgrade");
                    }
                }
                
                GUIButton button = buttonParent.GetChild<GUIButton>();
                if (button != null)
                {
                    button.Enabled = currentLevel < prefab.MaxLevel;
                    if (WaitForServerUpdate || !campaign.AllowedToManageCampaign() || price > campaign.Money)
                    {
                        button.Enabled = false;
                    }
                }
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
            if (!category.IsWallUpgrade && drawnSubmarine != null)
            {
                if (applicableCategories.Contains(category))
                {
                    parent.Enabled = true;
                    parent.SelectedColor = parent.Style.SelectedColor;
                }
                else
                {
                    parent.Enabled = false;
                    parent.SelectedColor = GUI.Style.Red * 0.5f;
                }
            }

            foreach (GUIComponent component in indicators.Children)
            {
                if (!(component is GUIImage image)) { continue; }

                foreach (UpgradePrefab prefab in prefabs)
                {
                    if (component.UserData != prefab) { continue; }

                    Dictionary<string, GUIComponentStyle> styles = GUI.Style.GetComponentStyle("upgradeindicator").ChildStyles;
                    if (!styles.ContainsKey("upgradeindicatoron") || !styles.ContainsKey("upgradeindicatordim") || !styles.ContainsKey("upgradeindicatoroff")) { continue; }

                    GUIComponentStyle onStyle  = styles["upgradeindicatoron"];
                    GUIComponentStyle dimStyle = styles["upgradeindicatordim"];
                    GUIComponentStyle offStyle = styles["upgradeindicatoroff"];

                    if (campaign.UpgradeManager.GetUpgradeLevel(prefab, category) >= prefab.MaxLevel)
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
                        if (image.Style == offStyle) { continue; }
                        image.ApplyStyle(offStyle);
                    }
                }
            }
        }
        
        private void ScrollToCategory(Predicate<CategoryData> predicate)
        {
            if (currentStoreLayout == null) { return; }

            foreach (GUIComponent child in currentStoreLayout.Content.Children)
            {
                if (child.UserData is CategoryData data && predicate(data))
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

        private bool HasPermission => campaignUI.Campaign.AllowedToManageCampaign();

        private static string FormatCurrency(int money, bool format = true)
        {
            return TextManager.GetWithVariable("CurrencyFormat", "[credits]", format ? string.Format(CultureInfo.InvariantCulture, "{0:N0}", money) : money.ToString());
        }

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