using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SpriteBatch = Microsoft.Xna.Framework.Graphics.SpriteBatch;

namespace Barotrauma
{
    
    internal class UpgradeStore
    {
        private readonly struct CategoryData
        {
            public readonly UpgradeCategory Category;
            public readonly List<UpgradePrefab> Prefabs;
            public readonly UpgradePrefab SinglePrefab;

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
        private CampaignMode Campaign => campaignUI?.Campaign;
        private UpgradeTab selectedUpgradTab = UpgradeTab.Upgrade;

        private GUIMessageBox currectConfirmation;

        public readonly GUIFrame ItemInfoFrame;
        private GUIComponent selectedUpgradeCategoryLayout;
        private GUILayoutGroup topHeaderLayout;
        private GUILayoutGroup mainStoreLayout;
        private GUILayoutGroup storeLayout;
        private GUILayoutGroup categoryButtonLayout;
        private GUILayoutGroup submarineInfoFrame;
        private GUIListBox currentStoreLayout;
        private GUICustomComponent submarinePreviewComponent;
        private GUIFrame subPreviewFrame;
        private Submarine drawnSubmarine;
        private readonly List<UpgradeCategory> applicableCategories = new List<UpgradeCategory>();
        private Vector2[][] subHullVerticies = new Vector2[0][];
        private List<Structure> submarineWalls = new List<Structure>();

        public MapEntity HoveredItem;
        private bool highlightWalls;

        private readonly Dictionary<Item, GUIFrame> itemPreviews = new Dictionary<Item, GUIFrame>();

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

            Campaign.UpgradeManager.OnUpgradesChanged += RefreshAll;
            Campaign.CargoManager.OnPurchasedItemsChanged += RefreshAll;
            Campaign.CargoManager.OnSoldItemsChanged += RefreshAll;
        }

        public void RefreshAll()
        {
            switch (selectedUpgradTab)
            {
                case UpgradeTab.Repairs:
                {
                    SelectTab(UpgradeTab.Repairs);
                    break;
                }
                case UpgradeTab.Upgrade:
                {
                    RefreshUpgradeList();
                    break;
                }
            }
        }

        private void RefreshUpgradeList()
        {
            // Updates the progress bar / text and disables the buy button if we reached max level
            if (selectedUpgradeCategoryLayout?.Parent != null && selectedUpgradeCategoryLayout.FindChild("prefablist", true) is GUIListBox listBox)
            {
                foreach (var component in listBox.Content.Children)
                {
                    if (component.UserData is CategoryData data)
                    {
                        UpdateUpgradeEntry(component, data.SinglePrefab, data.Category);
                    }
                }
            }

            // update the small indicator icons on the list
            if (currentStoreLayout?.Parent != null)
            {
                foreach (GUIComponent component in currentStoreLayout.Content.Children)
                {
                    if (!(component.UserData is CategoryData data)) { continue; }
                    if (component.FindChild("indicators", true) is { } indicators)
                    {
                        UpdateCategoryIndicators(indicators, component, data.Prefabs, data.Category);
                    }
                }

                // reset the order first
                foreach (UpgradeCategory category in UpgradeCategory.Categories)
                {
                    GUIComponent component = currentStoreLayout.Content.FindChild(c => c.UserData is CategoryData categoryData && categoryData.Category == category);
                    component?.SetAsLastChild();
                }

                // send the disabled components to the bottom
                List<GUIComponent> lastChilds = currentStoreLayout.Content.Children.Where(component => !component.Enabled).ToList();

                foreach (var lastChild in lastChilds)
                {
                    lastChild.SetAsLastChild();
                }
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
            selectedUpgradTab = UpgradeTab.Upgrade;
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
            new GUIListBox(rectT(1, 0.5f, tooltipLayout), style: null) { ScrollBarVisible = false, AutoHideScrollBar = false, UserData = "upgradelist"};
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
                    GUIButton upgradeButton = new GUIButton(rectT(1, 1f, categoryButtonLayout), TextManager.Get("UICategory.Upgrades"), style: "GUITabButton") { UserData = UpgradeTab.Upgrade, Selected = selectedUpgradTab == UpgradeTab.Upgrade };
                    GUIButton repairButton = new GUIButton(rectT(1, 1f, categoryButtonLayout), TextManager.Get("UICategory.Maintenance"), style: "GUITabButton") { UserData = UpgradeTab.Repairs, Selected = selectedUpgradTab == UpgradeTab.Repairs };

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
                    new GUITextBlock(rectT(1f, 0f, priceLayout), FormatCurrency(Campaign.Money, format: true), font: GUI.SubHeadingFont, textAlignment: Alignment.Right) { TextGetter = () => FormatCurrency(Campaign.Money, format: true) };
                new GUIFrame(rectT(0.5f, 0.1f, rightLayout, Anchor.BottomRight), style: "HorizontalLine") { IgnoreLayoutGroups = true };

            repairButton.OnClicked = upgradeButton.OnClicked = (button, o) =>
            {
                if (o is UpgradeTab upgradeTab)
                {
                    if (upgradeTab != selectedUpgradTab || currentStoreLayout == null || currentStoreLayout.Parent != storeLayout)
                    {
                        selectedUpgradTab = upgradeTab;
                        SelectTab(selectedUpgradTab);
                        storeLayout?.Recalculate();
                    }

                    repairButton.Selected = (UpgradeTab) repairButton.UserData == selectedUpgradTab;
                    upgradeButton.Selected = (UpgradeTab) upgradeButton.UserData == selectedUpgradTab;

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

        private void SelectTab(UpgradeTab tab)
        {
            if (currentStoreLayout != null)
            {
                storeLayout.RemoveChild(currentStoreLayout);
            }

            if (selectedUpgradeCategoryLayout != null)
            {
                mainStoreLayout.RemoveChild(selectedUpgradeCategoryLayout);
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
            highlightWalls = false;
            foreach (GUIFrame itemFrame in itemPreviews.Values)
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

                if (Campaign.Money >= hullRepairCost)
                {
                    string body = TextManager.GetWithVariable("WallRepairs.PurchasePromptBody", "[amount]", hullRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (Campaign.Money >= hullRepairCost)
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
                if (Campaign.Money >= itemRepairCost && !Campaign.PurchasedItemRepairs)
                {
                    string body = TextManager.GetWithVariable("ItemRepairs.PurchasePromptBody", "[amount]", itemRepairCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (Campaign.Money >= itemRepairCost && !Campaign.PurchasedItemRepairs)
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

                if (Campaign.Money >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
                {
                    string body = TextManager.GetWithVariable("ReplaceLostShuttles.PurchasePromptBody", "[amount]", shuttleRetrieveCost.ToString());
                    currectConfirmation = EventEditorScreen.AskForConfirmation(TextManager.Get("Upgrades.PurchasePromptTitle"), body, () =>
                    {
                        if (Campaign.Money >= shuttleRetrieveCost && !Campaign.PurchasedLostShuttles)
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

                foreach (var (item, itemFrame) in itemPreviews)
                {
                    if (GameMain.GameSession.SubmarineInfo.LeftBehindDockingPortIDs.Contains(item.ID))
                    {
                        itemFrame.OutlineColor = itemFrame.Color = GameMain.GameSession.SubmarineInfo.BlockedDockingPortIDs.Contains(item.ID) ? GUI.Style.Red : GUI.Style.Green;
                    }
                    else
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                    }
                }
                return true;
            }, disableElement: true);
        }

        private void CreateRepairEntry(GUIComponent parent, string title, string imageStyle, int price, GUIButton.OnClickedHandler onPressed, bool isDisabled, Func<bool, bool> onHover = null, bool disableElement = false)
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
                    new GUIButton(rectT(0.7f, 0.5f, buyButtonLayout), string.Empty, style: "RepairBuyButton") { ClickSound = GUISoundType.HireRepairClick, Enabled = Campaign.Money >= price && !isDisabled, OnClicked = onPressed };
            contentLayout.Recalculate();
            buyButtonLayout.Recalculate();

            if (disableElement)
            {
                frameChild.Enabled = Campaign.Money >= price && !isDisabled;
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

        private void CreateUpgradeTab()
        {
            currentStoreLayout = new GUIListBox(rectT(1.0f, 1.5f, storeLayout), style: null)
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
                var frameChild = new GUIFrame(rectT(1, 0.15f, currentStoreLayout.Content), style: "UpgradeUIFrame")
                {
                    UserData = new CategoryData(category, prefabs),
                    GlowOnSelect = true
                };

                frameChild.DefaultColor = frameChild.Color;
                frameChild.Color = Color.Transparent;

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

            selectedUpgradeCategoryLayout = new GUIFrame(rectT(GUI.IsFourByThree() ? 0.3f : 0.25f, 1, mainStoreLayout), style: null) { CanBeFocused = false };

            RefreshUpgradeList();

            currentStoreLayout.OnSelected += (component, userData) =>
            {
                if (!component.Enabled) 
                {
                    selectedUpgradeCategoryLayout?.ClearChildren();
                    foreach (GUIFrame itemFrame in itemPreviews.Values)
                    {
                        itemFrame.OutlineColor = itemFrame.Color = previewWhite;
                    }
                    return true;
                }

                if (userData is CategoryData categoryData && Submarine.MainSub != null)
                {
                    TrySelectCategory(categoryData.Prefabs, categoryData.Category, Submarine.MainSub);
                }

                return true;
            };
        }

        // This was supposed to have some logic for fancy animations to slide the previous tab out but maybe another time
        private void TrySelectCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine) => SelectUpgradeCategory(prefabs, category, submarine);

        private void SelectUpgradeCategory(List<UpgradePrefab> prefabs, UpgradeCategory category, Submarine submarine)
        {
            if (selectedUpgradeCategoryLayout == null || submarine == null) { return; }

            GUIFrame[] categoryFrames = GetFrames(category);
            foreach (GUIFrame itemFrame in itemPreviews.Values)
            {
                itemFrame.OutlineColor = itemFrame.Color = categoryFrames.Contains(itemFrame) ? GUI.Style.Orange : previewWhite;
            }

            highlightWalls = category.IsWallUpgrade;

            selectedUpgradeCategoryLayout?.ClearChildren();
            GUIFrame frame = new GUIFrame(rectT(1, 0.4f, selectedUpgradeCategoryLayout));
            GUIListBox prefabList = new GUIListBox(rectT(0.93f, 0.9f, frame, Anchor.Center)) { UserData = "prefablist" };
            foreach (UpgradePrefab prefab in prefabs)
            {
                CreateUpgradeEntry(prefab, category, prefabList.Content);
            }
        }

        private void CreateUpgradeEntry(UpgradePrefab prefab, UpgradeCategory category, GUIComponent parent)
        {
            /*                        UPGRADE PREFAB ENTRY
             * |------------------------------------------------------------------|
             * |               | title                            |     price     |
             * |               |----------------------------------|_______________|
             * |     icon      | description                      |               |
             * |               |----------------------------------|    buy btn.   |
             * |               | progress bar             | x / y |               |
             * |------------------------------------------------------------------|
             */
            GUIFrame prefabFrame = new GUIFrame(rectT(1f, 0.25f, parent), style: "ListBoxElement") { SelectedColor = Color.Transparent, UserData = new CategoryData(category, prefab) };
                GUILayoutGroup prefabLayout = new GUILayoutGroup(rectT(0.98f,0.95f, prefabFrame, Anchor.Center), isHorizontal: true);
                    GUILayoutGroup imageLayout = new GUILayoutGroup(rectT(new Point(prefabLayout.Rect.Height, prefabLayout.Rect.Height), prefabLayout), childAnchor: Anchor.Center);
                        var icon = new GUIImage(rectT(0.9f, 0.9f, imageLayout), prefab.Sprite, scaleToFit: true) { CanBeFocused = false };
                    GUILayoutGroup textLayout = new GUILayoutGroup(rectT(0.8f - imageLayout.RectTransform.RelativeSize.X, 1, prefabLayout));
                        var name = new GUITextBlock(rectT(1, 0.25f, textLayout), prefab.Name, font: GUI.SubHeadingFont) { AutoScaleHorizontal = true, AutoScaleVertical = true, Padding = Vector4.Zero };
                        GUILayoutGroup descriptionLayout = new GUILayoutGroup(rectT(1, 0.50f, textLayout));
                            var description = new GUITextBlock(rectT(1, 1, descriptionLayout), prefab.Description, font: GUI.SmallFont, wrap: true, textAlignment: Alignment.TopLeft) { Padding = Vector4.Zero };
                        GUILayoutGroup progressLayout = new GUILayoutGroup(rectT(1, 0.25f, textLayout), isHorizontal: true, childAnchor: Anchor.CenterLeft) { UserData = "progressbar" };
                            new GUIProgressBar(rectT(0.8f, 0.75f, progressLayout), 0.0f, GUI.Style.Orange);
                            new GUITextBlock(rectT(0.2f, 1, progressLayout), string.Empty, font: GUI.SmallFont, textAlignment: Alignment.Center) { Padding = Vector4.Zero };
                    GUILayoutGroup buyButtonLayout = new GUILayoutGroup(rectT(0.2f, 1, prefabLayout), childAnchor: Anchor.TopCenter) { UserData = "buybutton" };
                        new GUITextBlock(rectT(1, 0.4f, buyButtonLayout), FormatCurrency(prefab.Price.GetBuyprice(Campaign.UpgradeManager.GetUpgradeLevel(prefab, category), Campaign.Map?.CurrentLocation)), textAlignment: Alignment.Center) { Padding = Vector4.Zero };
                        var buyButton = new GUIButton(rectT(0.7f, 0.5f, buyButtonLayout), string.Empty, style: "UpgradeBuyButton") { Enabled = false };

            description.CalculateHeightFromText();
            // cut the description if it overflows and add a tooltip to it
            for (int i = 100; i > 0 && description.Rect.Height > descriptionLayout.Rect.Height; i--)
            {
                string[] lines = description.WrappedText.Split('\n');
                var newString = string.Join('\n', lines.Take(lines.Length - 1));
                if (0 >= newString.Length - 4) { break; }

                description.Text = newString.Substring(0, newString.Length - 4) + "...";
                description.CalculateHeightFromText();
                description.ToolTip = prefab.Description;
            }

            // Recalculate everything to prevent jumping
            if (parent is GUILayoutGroup group) { group.Recalculate(); }

            descriptionLayout.Recalculate();
            prefabLayout.Recalculate();
            imageLayout.Recalculate();
            textLayout.Recalculate();
            progressLayout.Recalculate();
            buyButtonLayout.Recalculate();

            if (!HasPermission)
            {
                prefabFrame.Enabled = false;
                description.Enabled = false;
                name.Enabled = false;
                icon.Color = Color.Gray;
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

            UpdateUpgradeEntry(prefabFrame, prefab, category);
        }

        private void CreateItemTooltip(MapEntity entity)
        {
            GUITextBlock itemName = ItemInfoFrame.FindChild("itemname", true) as GUITextBlock;
            GUIListBox upgradeList = ItemInfoFrame.FindChild("upgradelist", true) as GUIListBox;
            GUITextBlock moreIndicator = ItemInfoFrame.FindChild("moreindicator", true) as GUITextBlock;
            GUILayoutGroup layout = ItemInfoFrame.GetChild<GUILayoutGroup>();
            Debug.Assert(itemName != null && upgradeList != null && moreIndicator != null && layout != null, "One ore more tooltip elements not found");

            List<Upgrade> upgrades = entity.GetUpgrades();
            int upgradesCount = upgrades.Count;
            const int maxUpgrades = 4;
            
            itemName.Text = entity is Item ? entity.Name : TextManager.Get("upgradecategory.walls");
            upgradeList.Content.ClearChildren();
            for (var i = 0; i < upgrades.Count && i < maxUpgrades; i++)
            {
                Upgrade upgrade = upgrades[i];
                new GUITextBlock(rectT(1, 0.25f, upgradeList.Content), CreateListEntry(upgrade.Prefab.Name, upgrade.Level)) { AutoScaleHorizontal = true, UserData = Tuple.Create(upgrade.Level, upgrade.Prefab) };
            }

            // include pending upgrades into the tooltip
            foreach (var (prefab, category, level) in Campaign.UpgradeManager.PendingUpgrades)
            {
                if (entity is Item item && category.CanBeApplied(item) || entity is Structure && category.IsWallUpgrade)
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

        private void UpdateSubmarinePreview(float deltaTime, GUICustomComponent parent)
        {
            if (!parent.Children.Any() || Submarine.MainSub != null && Submarine.MainSub != drawnSubmarine || GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y)
            {
                GameMain.GameSession?.SubmarineInfo?.CheckSubsLeftBehind();
                drawnSubmarine = Submarine.MainSub;
                if (drawnSubmarine != null)
                {
                    CreateSubmarinePreview(drawnSubmarine, parent);
                    CreateHullBorderVerticies(drawnSubmarine, parent);

                    List<Item> entitiesOnSub = drawnSubmarine.GetItems(true).Where(i => drawnSubmarine.IsEntityFoundOnThisSub(i, true)).ToList();
                    applicableCategories.Clear();

                    foreach (UpgradeCategory category in UpgradeCategory.Categories)
                    {
                        if (entitiesOnSub.Any(item => category.CanBeApplied(item) && !item.disallowedUpgrades.Contains(category.Identifier)))
                        {
                            applicableCategories.Add(category);
                        }
                    }
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
                        GUIButton firstButton = msgBox.Buttons.FirstOrDefault();
                        if (firstButton == null) { continue; }

                        firstButton.OnClicked.Invoke(firstButton, firstButton.UserData);
                    }
                }
            }

            if (itemPreviews == null) { return; }

            bool found = false;
            foreach (var (item, frame) in itemPreviews)
            {
                if (GUI.MouseOn == frame)
                {
                    if (HoveredItem != item) { CreateItemTooltip(item); }
                    HoveredItem = item;
                    if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradTab == UpgradeTab.Upgrade && currentStoreLayout != null)
                    {
                        ScrollToCategory(data => data.Category.CanBeApplied(item));
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
                    Structure firstStructure = submarineWalls.FirstOrDefault();
                    // use pnpoly algorithm to detect if our mouse is within any of the hull polygons
                    if (subHullVerticies.Any(hullVertex => ToolBox.PointIntersectsWithPolygon(PlayerInput.MousePosition, hullVertex)))
                    {
                        if (HoveredItem != firstStructure) { CreateItemTooltip(firstStructure); }
                        HoveredItem = firstStructure;
                        isMouseOnStructure = true;
                        GUI.MouseCursor = CursorState.Hand;

                        if (PlayerInput.PrimaryMouseButtonClicked() && selectedUpgradTab == UpgradeTab.Upgrade && currentStoreLayout != null)
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
            List<Entity> pointsOfInterest = (from category in UpgradeCategory.Categories from item in submarine.GetItems(UpgradeManager.UpgradeAlsoConnectedSubs) where category.CanBeApplied(item) && !item.NonInteractable select item).Cast<Entity>().ToList();

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
                    Point size = new Point((int) (item.Rect.Width * item.Scale / dockedBorders.Width * hullContainer.Rect.Width), (int) (item.Rect.Height * item.Scale / dockedBorders.Height * hullContainer.Rect.Height));
                    GUIFrame itemFrame = new GUIFrame(rectT(size, component, Anchor.Center), style: "ScanLines")
                    {
                        SelectedColor = GUI.Style.Orange,
                        OutlineColor = previewWhite,
                        Color = previewWhite,
                        OutlineThickness = 2,
                        HoverCursor = CursorState.Hand
                    };
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

            subHullVerticies = new Vector2[sub.HullVertices.Count][];

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

                subHullVerticies[i] = new[]
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
            foreach (Vector2[] hullVertex in subHullVerticies)
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

        private void UpdateUpgradeEntry(GUIComponent prefabFrame, UpgradePrefab prefab, UpgradeCategory category)
        {
            int currentLevel = Campaign.UpgradeManager.GetUpgradeLevel(prefab, category);

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
                int price = prefab.Price.GetBuyprice(Campaign.UpgradeManager.GetUpgradeLevel(prefab, category), Campaign.Map?.CurrentLocation);

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
                    if (WaitForServerUpdate || !HasPermission || price > Campaign.Money)
                    {
                        button.Enabled = false;
                    }
                }
            }
        }

        private void UpdateCategoryIndicators(GUIComponent indicators, GUIComponent parent, List<UpgradePrefab> prefabs, UpgradeCategory category)
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

                    if (Campaign.UpgradeManager.GetUpgradeLevel(prefab, category) >= prefab.MaxLevel)
                    {
                        // we check this to avoid flickering from re-applying the same style
                        if (image.Style == onStyle) { continue; }
                        image.ApplyStyle(onStyle);
                    }
                    else if (Campaign.UpgradeManager.GetUpgradeLevel(prefab, category) > 0)
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
        private GUIFrame[] GetFrames(UpgradeCategory category)
        {
            List<GUIFrame> frames = new List<GUIFrame>();
            foreach (var (item, guiFrame) in itemPreviews)
            {
                if (category.CanBeApplied(item))
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
        private static RectTransform rectT(float x, float y, GUIComponent parentComponent, Anchor anchor = Anchor.TopLeft)
        {
            return new RectTransform(new Vector2(x, y), parentComponent.RectTransform, anchor);
        }
        
        private static RectTransform rectT(Point point, GUIComponent parentComponent, Anchor anchor = Anchor.TopLeft)
        {
            return new RectTransform(point, parentComponent.RectTransform, anchor);
        }
    }
}