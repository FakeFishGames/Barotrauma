using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class Store
    {
        private readonly CampaignUI campaignUI;
        private readonly GUIComponent parentComponent;
        private readonly List<GUIButton> storeTabButtons = new List<GUIButton>();
        private readonly List<GUIButton> itemCategoryButtons = new List<GUIButton>();
        private readonly Dictionary<StoreTab, GUIListBox> tabLists = new Dictionary<StoreTab, GUIListBox>();
        private readonly Dictionary<StoreTab, SortingMethod> tabSortingMethods = new Dictionary<StoreTab, SortingMethod>();
        private readonly List<PurchasedItem> itemsToSell = new List<PurchasedItem>();

        private StoreTab activeTab = StoreTab.Buy;
        private MapEntityCategory? selectedItemCategory;
        private bool suppressBuySell;
        private int buyTotal, sellTotal;

        private GUITextBlock merchantBalanceBlock;
        private GUILayoutGroup valueChangeGroup;
        private GUITextBlock currentSellValueBlock, newSellValueBlock;
        private GUIImage sellValueChangeArrow;
        private GUIDropDown sortingDropDown;
        private GUITextBox searchBox;
        private GUIListBox storeBuyList, storeSellList;
        /// <summary>
        /// Can be null when there are no deals at the current location
        /// </summary>
        private GUILayoutGroup storeDailySpecialsGroup, storeRequestedGoodGroup;
        private Color storeSpecialColor;

        private GUIListBox shoppingCrateBuyList, shoppingCrateSellList;
        private GUITextBlock shoppingCrateTotal;
        private GUIButton clearAllButton, confirmButton;

        private bool needsRefresh, needsBuyingRefresh, needsSellingRefresh, needsItemsToSellRefresh;

        private Point resolutionWhenCreated;
        private bool hadPermissions;

        private Dictionary<ItemPrefab, int> OwnedItems { get; } = new Dictionary<ItemPrefab, int>(); 

        private CargoManager CargoManager => campaignUI.Campaign.CargoManager;
        private Location CurrentLocation => campaignUI.Campaign.Map?.CurrentLocation;
        private int PlayerMoney => campaignUI.Campaign.Money;
        private bool HasPermissions => campaignUI.Campaign.AllowedToManageCampaign();
        private bool IsBuying => activeTab != StoreTab.Sell;
        private bool IsSelling => activeTab == StoreTab.Sell;
        private GUIListBox ActiveShoppingCrateList => IsBuying ? shoppingCrateBuyList : shoppingCrateSellList;

        private enum StoreTab
        {
            Buy,
            Sell
        }

        private enum SortingMethod
        {
            AlphabeticalAsc,
            AlphabeticalDesc,
            PriceAsc,
            PriceDesc,
            CategoryAsc
        }

        public Store(CampaignUI campaignUI, GUIComponent parentComponent)
        {
            this.campaignUI = campaignUI;
            this.parentComponent = parentComponent;

            hadPermissions = HasPermissions;

            CreateUI();

            campaignUI.Campaign.Map.OnLocationChanged += UpdateLocation;
            if (CurrentLocation?.Reputation != null)
            {
                CurrentLocation.Reputation.OnReputationValueChanged += () => { needsRefresh = true; };
            }
            campaignUI.Campaign.CargoManager.OnItemsInBuyCrateChanged += () => { needsBuyingRefresh = true; };
            campaignUI.Campaign.CargoManager.OnPurchasedItemsChanged += () => { needsRefresh = true; };
            campaignUI.Campaign.CargoManager.OnItemsInSellCrateChanged += () => { needsSellingRefresh = true; };
            campaignUI.Campaign.CargoManager.OnSoldItemsChanged += () =>
            {
                needsItemsToSellRefresh = true;
                needsRefresh = true;
            };
        }

        public void Refresh(bool updateOwned = true)
        {
            hadPermissions = HasPermissions;
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshBuying(updateOwned: false);
            RefreshSelling(updateOwned: false);
            needsRefresh = false;
        }

        private void RefreshBuying(bool updateOwned = true)
        {
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshShoppingCrateBuyList();
            RefreshStoreBuyList();
            var hasPermissions = HasPermissions;
            storeBuyList.Enabled = hasPermissions;
            shoppingCrateBuyList.Enabled = hasPermissions;
            needsBuyingRefresh = false;
        }

        private void RefreshSelling(bool updateOwned = true)
        {
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshShoppingCrateSellList();
            RefreshStoreSellList();
            var hasPermissions = HasPermissions;
            storeSellList.Enabled = hasPermissions;
            shoppingCrateSellList.Enabled = hasPermissions;
            needsSellingRefresh = false;
        }

        private void CreateUI()
        {
            if (parentComponent.FindChild(c => c.UserData as string == "glow") is GUIComponent glowChild)
            {
                parentComponent.RemoveChild(glowChild);
            }
            if (parentComponent.FindChild(c => c.UserData as string == "container") is GUIComponent containerChild)
            {
                parentComponent.RemoveChild(containerChild);
            }

            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), parentComponent.RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                CanBeFocused = false,
                UserData = "glow"
            };
            new GUIFrame(new RectTransform(new Vector2(0.95f), parentComponent.RectTransform, anchor: Anchor.Center), style: null)
            {
                CanBeFocused = false,
                UserData = "container"
            };

            var panelMaxWidth = (int)(GUI.xScale * (GUI.HorizontalAspectRatio < 1.4f ? 650 : 560));
            var storeContent = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1.0f), campaignUI.GetTabContainer(CampaignMode.InteractionType.Store).RectTransform)
                {
                    MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Store).Rect.Height)
                })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            // Store header ------------------------------------------------
            var headerGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), storeContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.005f
            };
            var imageWidth = (float)headerGroup.Rect.Height / headerGroup.Rect.Width;
            new GUIImage(new RectTransform(new Vector2(imageWidth, 1.0f), headerGroup.RectTransform), "StoreTradingIcon");
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("store"), font: GUI.LargeFont)
            {
                CanBeFocused = false,
                ForceUpperCase = true
            };

            // Merchant balance ------------------------------------------------
            var balanceAndValueGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), storeContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.005f
            };

            var merchantBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), balanceAndValueGroup.RectTransform))
            {
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), merchantBalanceContainer.RectTransform),
                TextManager.Get("campaignstore.storebalance"), font: GUI.Font, textAlignment: Alignment.BottomLeft)
            {
                AutoScaleVertical = true,
                ForceUpperCase = true
            };
            merchantBalanceBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), merchantBalanceContainer.RectTransform),
                "", font: GUI.SubHeadingFont)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = () =>
                {
                    if (CurrentLocation != null)
                    {
                        merchantBalanceBlock.TextColor = CurrentLocation.BalanceColor;
                        return GetCurrencyFormatted(CurrentLocation.StoreCurrentBalance);
                    }
                    else
                    {
                        merchantBalanceBlock.TextColor = Color.Red;
                        return GetCurrencyFormatted(0);
                    }
                } 
            };

            // Item sell value ------------------------------------------------
            var sellValueContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), balanceAndValueGroup.RectTransform))
            {
                CanBeFocused = false,
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), sellValueContainer.RectTransform),
                TextManager.Get("campaignstore.sellvalue"), font: GUI.Font, textAlignment: Alignment.BottomLeft)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                ForceUpperCase = true
            };

            valueChangeGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), sellValueContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = true,
                RelativeSpacing = 0.02f
            };
            float blockWidth = GUI.IsFourByThree() ? 0.32f : 0.28f;
            Point blockMaxSize = new Point((int)(GameSettings.TextScale * 60), valueChangeGroup.Rect.Height);
            currentSellValueBlock = new GUITextBlock(new RectTransform(new Vector2(blockWidth, 1.0f), valueChangeGroup.RectTransform) { MaxSize = blockMaxSize },
                "", font: GUI.SubHeadingFont)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                TextScale = 1.1f,
                TextGetter = () =>
                {
                    if (CurrentLocation != null)
                    {
                        int balanceAfterTransaction = IsBuying ?
                            CurrentLocation.StoreCurrentBalance + buyTotal :
                            CurrentLocation.StoreCurrentBalance - sellTotal;
                        if (balanceAfterTransaction != CurrentLocation.StoreCurrentBalance)
                        {
                            var newStatus = Location.GetStoreBalanceStatus(balanceAfterTransaction);
                            if (CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier != newStatus.SellPriceModifier)
                            {
                                string tooltipTag = newStatus.SellPriceModifier > CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier ?
                                    "campaingstore.valueincreasetooltip" : "campaingstore.valuedecreasetooltip";
                                valueChangeGroup.ToolTip = TextManager.Get(tooltipTag);
                                currentSellValueBlock.TextColor = newStatus.Color;
                                sellValueChangeArrow.Color = newStatus.Color;
                                sellValueChangeArrow.Visible = true;
                                newSellValueBlock.TextColor = newStatus.Color;
                                newSellValueBlock.Text = $"{(newStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                                return $"{(CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                            }
                        }
                        valueChangeGroup.ToolTip = null;
                        currentSellValueBlock.TextColor = CurrentLocation.BalanceColor;
                        sellValueChangeArrow.Visible = false;
                        newSellValueBlock.Text = null;
                        return $"{(CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                    }
                    else
                    {
                        valueChangeGroup.ToolTip = null;
                        sellValueChangeArrow.Visible = false;
                        newSellValueBlock.Text = null;
                        return null;
                    }
                }
            };
            Vector4 newPadding = currentSellValueBlock.Padding;
            newPadding.Z = 0;
            currentSellValueBlock.Padding = newPadding;
            float relativeHeight = 0.45f;
            float relativeWidth = (relativeHeight * valueChangeGroup.Rect.Height) / valueChangeGroup.Rect.Width;
            sellValueChangeArrow = new GUIImage(new RectTransform(new Vector2(relativeWidth, relativeHeight), valueChangeGroup.RectTransform), "StoreArrow", scaleToFit: true)
            {
                CanBeFocused = false,
                Visible = false
            };
            newSellValueBlock = new GUITextBlock(new RectTransform(new Vector2(blockWidth, 1.0f), valueChangeGroup.RectTransform) { MaxSize = blockMaxSize },
                "", font: GUI.SubHeadingFont)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                TextScale = 1.1f
            };
            newPadding = newSellValueBlock.Padding;
            newPadding.X = 0;
            newSellValueBlock.Padding = newPadding;

            // Store mode buttons ------------------------------------------------
            var modeButtonFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f / 14.0f), storeContent.RectTransform), style: null);
            var modeButtonContainer = new GUILayoutGroup(new RectTransform(Vector2.One, modeButtonFrame.RectTransform), isHorizontal: true);

            var tabs = Enum.GetValues(typeof(StoreTab));
            storeTabButtons.Clear();
            tabSortingMethods.Clear();
            foreach (StoreTab tab in tabs)
            {
                var tabButton = new GUIButton(new RectTransform(new Vector2(1.0f / (tabs.Length + 1), 1.0f), modeButtonContainer.RectTransform),
                    text: TextManager.Get("campaignstoretab." + tab), style: "GUITabButton")
                {
                    UserData = tab,
                    OnClicked = (button, userData) =>
                    {
                        ChangeStoreTab((StoreTab)userData);
                        return true;
                    }
                };
                storeTabButtons.Add(tabButton);
                tabSortingMethods.Add(tab, SortingMethod.AlphabeticalAsc);
            }

            var storeInventoryContainer = new GUILayoutGroup(
                new RectTransform(
                    new Vector2(0.9f, 0.95f),
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 11.9f / 14.0f), storeContent.RectTransform)).RectTransform,
                    anchor: Anchor.Center),
                isHorizontal: true)
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };

            // Item category buttons ------------------------------------------------
            var categoryButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.08f, 1.0f), storeInventoryContainer.RectTransform))
            {
                RelativeSpacing = 0.02f
            };

            List<MapEntityCategory> itemCategories = Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>().ToList();
            //don't show categories with no buyable items
            itemCategories.RemoveAll(c => !ItemPrefab.Prefabs.Any(ep => ep.Category.HasFlag(c) && ep.CanBeBought));
            itemCategoryButtons.Clear();
            foreach (MapEntityCategory category in itemCategories)
            {
                var categoryButton = new GUIButton(new RectTransform(new Point(categoryButtonContainer.Rect.Width, categoryButtonContainer.Rect.Width), categoryButtonContainer.RectTransform),
                    style: "CategoryButton." + category)
                {
                    ToolTip = TextManager.Get("MapEntityCategory." + category),
                    UserData = category,
                    OnClicked = (btn, userdata) =>
                    {
                        MapEntityCategory? newCategory = !btn.Selected ? (MapEntityCategory?)userdata : null;
                        if (newCategory.HasValue) { searchBox.Text = ""; }
                        if (newCategory != selectedItemCategory) { tabLists[activeTab].ScrollBar.BarScroll = 0f; }
                        FilterStoreItems(newCategory, searchBox.Text);
                        return true;
                    }
                };
                itemCategoryButtons.Add(categoryButton);
                categoryButton.RectTransform.SizeChanged += () =>
                {
                    var sprite = categoryButton.Frame.sprites[GUIComponent.ComponentState.None].First();
                    categoryButton.RectTransform.NonScaledSize =
                        new Point(categoryButton.Rect.Width, (int)(categoryButton.Rect.Width * ((float)sprite.Sprite.SourceRect.Height / sprite.Sprite.SourceRect.Width)));
                };
            }

            GUILayoutGroup sortFilterListContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 1.0f), storeInventoryContainer.RectTransform))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };
            GUILayoutGroup sortFilterGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), sortFilterListContainer.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };
            
            GUILayoutGroup sortGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), sortFilterGroup.RectTransform))
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), sortGroup.RectTransform), text: TextManager.Get("campaignstore.sortby"));
            sortingDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.5f), sortGroup.RectTransform), text: TextManager.Get("campaignstore.sortby"), elementCount: 3)
            {
                OnSelected = (child, userData) =>
                {
                    SortActiveTabItems((SortingMethod)userData);
                    return true;
                }
            };
            var tag = "sortingmethod.";
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.AlphabeticalAsc), userData: SortingMethod.AlphabeticalAsc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.PriceAsc), userData: SortingMethod.PriceAsc);
            sortingDropDown.AddItem(TextManager.Get(tag + SortingMethod.PriceDesc), userData: SortingMethod.PriceDesc);

            GUILayoutGroup filterGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), sortFilterGroup.RectTransform))
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), filterGroup.RectTransform), TextManager.Get("serverlog.filter"));
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), filterGroup.RectTransform), createClearButton: true);
            searchBox.OnTextChanged += (textBox, text) => { FilterStoreItems(null, text); return true; };

            var storeItemListContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.92f), sortFilterListContainer.RectTransform), style: null);
            tabLists.Clear();

            storeBuyList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
            storeDailySpecialsGroup = CreateDealsGroup(storeBuyList);
            tabLists.Add(StoreTab.Buy, storeBuyList);

            storeSellList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
            storeRequestedGoodGroup = CreateDealsGroup(storeSellList);
            tabLists.Add(StoreTab.Sell, storeSellList);

            // Shopping Crate ------------------------------------------------------------------------------------------------------------------------------------------

            var shoppingCrateContent = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1.0f), campaignUI.GetTabContainer(CampaignMode.InteractionType.Store).RectTransform, anchor: Anchor.TopRight)
                {
                    MaxSize = new Point(panelMaxWidth, campaignUI.GetTabContainer(CampaignMode.InteractionType.Store).Rect.Height)
                })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            // Shopping crate header ------------------------------------------------
            headerGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), shoppingCrateContent.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.005f
            };
            imageWidth = (float)headerGroup.Rect.Height / headerGroup.Rect.Width;
            new GUIImage(new RectTransform(new Vector2(imageWidth, 1.0f), headerGroup.RectTransform), "StoreShoppingCrateIcon");
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("campaignstore.shoppingcrate"), font: GUI.LargeFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                ForceUpperCase = true
            };

            // Player balance ------------------------------------------------
            var playerBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), shoppingCrateContent.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                TextManager.Get("campaignstore.balance"), font: GUI.Font, textAlignment: Alignment.BottomRight)
            {
                AutoScaleVertical = true,
                ForceUpperCase = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                "", font: GUI.SubHeadingFont, textAlignment: Alignment.TopRight)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = () => GetCurrencyFormatted(PlayerMoney)
            };

            // Divider ------------------------------------------------
            var dividerFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f / 14.0f), shoppingCrateContent.RectTransform), style: null);
            new GUIImage(new RectTransform(Vector2.One, dividerFrame.RectTransform, anchor: Anchor.BottomCenter), "HorizontalLine");

            var shoppingCrateInventoryContainer = new GUILayoutGroup(
                new RectTransform(
                    new Vector2(0.9f, 0.95f),
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 11.9f / 14.0f), shoppingCrateContent.RectTransform)).RectTransform,
                    anchor: Anchor.Center))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };
            var shoppingCrateListContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.85f), shoppingCrateInventoryContainer.RectTransform), style: null);
            shoppingCrateBuyList = new GUIListBox(new RectTransform(Vector2.One, shoppingCrateListContainer.RectTransform)) { Visible = false };
            shoppingCrateSellList = new GUIListBox(new RectTransform(Vector2.One, shoppingCrateListContainer.RectTransform)) { Visible = false };

            var totalContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), shoppingCrateInventoryContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), TextManager.Get("campaignstore.total"), font: GUI.Font)
            {
                CanBeFocused = false
            };
            shoppingCrateTotal = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), "", font: GUI.SubHeadingFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                TextScale = 1.1f
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), shoppingCrateInventoryContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight);
            confirmButton = new GUIButton(new RectTransform(new Vector2(0.35f, 1.0f), buttonContainer.RectTransform))
            {
                ForceUpperCase = true
            };
            SetConfirmButtonBehavior();
            clearAllButton = new GUIButton(new RectTransform(new Vector2(0.35f, 1.0f), buttonContainer.RectTransform), TextManager.Get("campaignstore.clearall"))
            {
                ClickSound = GUISoundType.DecreaseQuantity,
                Enabled = HasPermissions,
                ForceUpperCase = true,
                OnClicked = (button, userData) =>
                {
                    if (!HasPermissions) { return false; }
                    var itemsToRemove = new List<PurchasedItem>(IsBuying ? CargoManager.ItemsInBuyCrate : CargoManager.ItemsInSellCrate);
                    itemsToRemove.ForEach(i => ClearFromShoppingCrate(i));
                    return true;
                }
            };

            Refresh();
            ChangeStoreTab(activeTab);
            resolutionWhenCreated = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private GUILayoutGroup CreateDealsGroup(GUIListBox parentList)
        {
            var elementHeight = (int)(GUI.yScale * 80);
            var frame = new GUIFrame(new RectTransform(new Point(parentList.Content.Rect.Width, 4 * elementHeight + 3), parent: parentList.Content.RectTransform), style: null);
            frame.UserData = "deals";
            var dealsGroup = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter);
            var dealsHeader = new GUILayoutGroup(new RectTransform(new Point((int)(0.95f * parentList.Content.Rect.Width), elementHeight), parent: dealsGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            dealsHeader.UserData = "header";
            var iconWidth = (0.9f * dealsHeader.Rect.Height) / dealsHeader.Rect.Width;
            var dealsIcon = new GUIImage(new RectTransform(new Vector2(iconWidth, 0.9f), dealsHeader.RectTransform), "StoreDealIcon", scaleToFit: true);
            var text = TextManager.Get(parentList == storeBuyList ? "campaignstore.dailyspecials" : "campaignstore.requestedgoods");
            var dealsText = new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 0.9f), dealsHeader.RectTransform), text, font: GUI.LargeFont);
            storeSpecialColor = dealsIcon.Color;
            dealsText.TextColor = storeSpecialColor;
            var divider = new GUIImage(new RectTransform(new Point(dealsGroup.Rect.Width, 3), dealsGroup.RectTransform), "HorizontalLine");
            divider.UserData = "divider";
            frame.CanBeFocused = dealsGroup.CanBeFocused = dealsHeader.CanBeFocused = dealsIcon.CanBeFocused = dealsText.CanBeFocused = divider.CanBeFocused = false;
            return dealsGroup;
        }

        private void UpdateLocation(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) { return; }

            if (prevLocation?.Reputation != null)
            {
                prevLocation.Reputation.OnReputationValueChanged = null;
            }

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (itemPrefab.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo _))
                {
                    ChangeStoreTab(StoreTab.Buy);
                    if (newLocation?.Reputation != null)
                    {
                        newLocation.Reputation.OnReputationValueChanged += () => { needsRefresh = true; };
                    }
                    return;
                }
            }
        }

        private void ChangeStoreTab(StoreTab tab)
        {
            activeTab = tab;
            foreach (GUIButton tabButton in storeTabButtons)
            {
                tabButton.Selected = (StoreTab)tabButton.UserData == activeTab;
            }
            sortingDropDown.SelectItem(tabSortingMethods[tab]);
            SetShoppingCrateTotalText();
            SetClearAllButtonStatus();
            SetConfirmButtonBehavior();
            SetConfirmButtonStatus();
            FilterStoreItems();
            if (tab == StoreTab.Buy)
            {
                storeSellList.Visible = false;
                storeBuyList.Visible = true;
                shoppingCrateSellList.Visible = false;
                shoppingCrateBuyList.Visible = true;
            }
            else if (tab == StoreTab.Sell)
            {
                storeBuyList.Visible = false;
                storeSellList.Visible = true;
                shoppingCrateBuyList.Visible = false;
                shoppingCrateSellList.Visible = true;
            }
        }

        private void FilterStoreItems(MapEntityCategory? category, string filter)
        {
            selectedItemCategory = category;
            var list = tabLists[activeTab];
            filter = filter?.ToLower();
            foreach (GUIComponent child in list.Content.Children)
            {
                var item = child.UserData as PurchasedItem;
                if (item?.ItemPrefab?.Name == null) { continue; }
                child.Visible =
                    (IsBuying || item.Quantity > 0) &&
                    (!category.HasValue || item.ItemPrefab.Category.HasFlag(category.Value)) &&
                    (string.IsNullOrEmpty(filter) || item.ItemPrefab.Name.ToLower().Contains(filter));
            }
            foreach (GUIButton btn in itemCategoryButtons)
            {
                btn.Selected = category.HasValue && (MapEntityCategory)btn.UserData == selectedItemCategory;
            }
            list.UpdateScrollBarSize();
        }

        private void FilterStoreItems()
        {
            //only select a specific category if the search box is empty (items from all categories are shown when searching)
            MapEntityCategory? category = string.IsNullOrEmpty(searchBox.Text) ? selectedItemCategory : null;
            FilterStoreItems(category, searchBox.Text);
        }

        private void RefreshStoreBuyList()
        {
            float prevBuyListScroll = storeBuyList.BarScroll;
            float prevShoppingCrateScroll = shoppingCrateBuyList.BarScroll;

            bool hasPermissions = HasPermissions;
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();

            if ((storeDailySpecialsGroup != null) != CurrentLocation.DailySpecials.Any())
            {
                if (storeDailySpecialsGroup == null)
                {
                    storeDailySpecialsGroup = CreateDealsGroup(storeBuyList);
                    storeDailySpecialsGroup.Parent.SetAsFirstChild();
                }
                else
                {
                    storeBuyList.RemoveChild(storeDailySpecialsGroup.Parent);
                    storeDailySpecialsGroup = null;
                }
                storeBuyList.RecalculateChildren();
            }

            foreach (PurchasedItem item in CurrentLocation.StoreStock)
            {
                CreateOrUpdateItemFrame(item.ItemPrefab, item.Quantity);
            }

            foreach (ItemPrefab itemPrefab in CurrentLocation.DailySpecials)
            {
                if (CurrentLocation.StoreStock.Any(pi => pi.ItemPrefab == itemPrefab)) { continue; }
                CreateOrUpdateItemFrame(itemPrefab, 0);
            }

            void CreateOrUpdateItemFrame(ItemPrefab itemPrefab, int quantity)
            {
                if (itemPrefab.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo priceInfo))
                {
                    var isDailySpecial = CurrentLocation.DailySpecials.Contains(itemPrefab);
                    var itemFrame = isDailySpecial ?
                        storeDailySpecialsGroup.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab) :
                        storeBuyList.Content.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab);
                    if (CargoManager.PurchasedItems.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem purchasedItem)
                    {
                        quantity = Math.Max(quantity - purchasedItem.Quantity, 0);
                    }
                    if (CargoManager.ItemsInBuyCrate.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem itemInBuyCrate)
                    {
                        quantity = Math.Max(quantity - itemInBuyCrate.Quantity, 0);
                    }
                    if (itemFrame == null)
                    {
                        var parentComponent = isDailySpecial ? storeDailySpecialsGroup : storeBuyList as GUIComponent;
                        itemFrame = CreateItemFrame(new PurchasedItem(itemPrefab, quantity), priceInfo, parentComponent, forceDisable: !hasPermissions);
                    }
                    else
                    {
                        (itemFrame.UserData as PurchasedItem).Quantity = quantity;
                        SetQuantityLabelText(StoreTab.Buy, itemFrame);
                        SetOwnedLabelText(itemFrame);
                        SetPriceGetters(itemFrame, true);
                    }
                    SetItemFrameStatus(itemFrame, hasPermissions && quantity > 0);
                    existingItemFrames.Add(itemFrame);
                }
            }

            var removedItemFrames = storeBuyList.Content.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList();
            if (storeDailySpecialsGroup != null)
            {
                removedItemFrames.AddRange(storeDailySpecialsGroup.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList());
            }
            removedItemFrames.ForEach(f => f.RectTransform.Parent = null);
            if (IsBuying) { FilterStoreItems(); }
            SortItems(StoreTab.Buy);

            storeBuyList.BarScroll = prevBuyListScroll;
            shoppingCrateBuyList.BarScroll = prevShoppingCrateScroll;
        }

        private void RefreshStoreSellList()
        {
            float prevSellListScroll = storeSellList.BarScroll;
            float prevShoppingCrateScroll = shoppingCrateSellList.BarScroll;
            bool hasPermissions = HasPermissions;
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();

            if ((storeRequestedGoodGroup != null) != CurrentLocation.RequestedGoods.Any())
            {
                if (storeRequestedGoodGroup == null)
                {
                    storeRequestedGoodGroup = CreateDealsGroup(storeSellList);
                    storeRequestedGoodGroup.Parent.SetAsFirstChild();
                }
                else
                {
                    storeSellList.RemoveChild(storeRequestedGoodGroup.Parent);
                    storeRequestedGoodGroup = null;
                }
                storeSellList.RecalculateChildren();
            }

            foreach (PurchasedItem item in itemsToSell)
            {
                CreateOrUpdateItemFrame(item.ItemPrefab, item.Quantity);
            }

            foreach (var requestedGood in CurrentLocation.RequestedGoods)
            {
                if (itemsToSell.Any(pi => pi.ItemPrefab == requestedGood)) { continue; }
                CreateOrUpdateItemFrame(requestedGood, 0);
            }

            void CreateOrUpdateItemFrame(ItemPrefab itemPrefab, int itemQuantity)
            {
                PriceInfo priceInfo = itemPrefab.GetPriceInfo(CurrentLocation);
                if (priceInfo == null) { return; }
                var isRequestedGood = CurrentLocation.RequestedGoods.Contains(itemPrefab);
                var itemFrame = isRequestedGood ?
                    storeRequestedGoodGroup.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab) :
                    storeSellList.Content.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab);
                if (CargoManager.ItemsInSellCrate.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem itemInSellCrate)
                {
                    itemQuantity = Math.Max(itemQuantity - itemInSellCrate.Quantity, 0);
                }
                if (itemFrame == null)
                {
                    var parentComponent = isRequestedGood ? storeRequestedGoodGroup : storeSellList as GUIComponent;
                    itemFrame = CreateItemFrame(new PurchasedItem(itemPrefab, itemQuantity), priceInfo, parentComponent, forceDisable: !hasPermissions);
                }
                else
                {
                    (itemFrame.UserData as PurchasedItem).Quantity = itemQuantity;
                    SetQuantityLabelText(StoreTab.Sell, itemFrame);
                    SetOwnedLabelText(itemFrame);
                    SetPriceGetters(itemFrame, false);
                }
                SetItemFrameStatus(itemFrame, hasPermissions && itemQuantity > 0);
                if (itemQuantity < 1 && !isRequestedGood)
                {
                    itemFrame.Visible = false;
                }
                existingItemFrames.Add(itemFrame);
            }

            var removedItemFrames = storeSellList.Content.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList();
            if (storeRequestedGoodGroup != null)
            {
                removedItemFrames.AddRange(storeRequestedGoodGroup.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList());
            }
            removedItemFrames.ForEach(f => f.RectTransform.Parent = null);
            if (IsSelling) { FilterStoreItems(); }
            SortItems(StoreTab.Sell);

            storeSellList.BarScroll = prevSellListScroll;
            shoppingCrateSellList.BarScroll = prevShoppingCrateScroll;
        }

        private void SetPriceGetters(GUIComponent itemFrame, bool buying)
        {
            if (itemFrame == null || !(itemFrame.UserData is PurchasedItem pi)) { return; }

            if (itemFrame.FindChild("undiscountedprice", recursive: true) is GUITextBlock undiscountedPriceBlock)
            {
                if (buying)
                {
                    undiscountedPriceBlock.TextGetter = () => GetCurrencyFormatted(
                         CurrentLocation?.GetAdjustedItemBuyPrice(pi.ItemPrefab, considerDailySpecials: false) ?? 0);
                }
                else
                {
                    undiscountedPriceBlock.TextGetter = () => GetCurrencyFormatted(
                       CurrentLocation?.GetAdjustedItemSellPrice(pi.ItemPrefab, considerRequestedGoods: false) ?? 0);
                }
            }

            if (itemFrame.FindChild("price", recursive: true) is GUITextBlock priceBlock)
            {
                if (buying)
                {
                    priceBlock.TextGetter = () => GetCurrencyFormatted(CurrentLocation?.GetAdjustedItemBuyPrice(pi.ItemPrefab) ?? 0);
                }
                else
                {
                    priceBlock.TextGetter = () => GetCurrencyFormatted(CurrentLocation?.GetAdjustedItemSellPrice(pi.ItemPrefab) ?? 0);
                }
            }
        }

        public void RefreshItemsToSell()
        {
            itemsToSell.Clear();
            var playerItems = CargoManager.GetSellableItems(Character.Controlled);
            foreach (Item playerItem in playerItems)
            {
                if (itemsToSell.FirstOrDefault(i => i.ItemPrefab == playerItem.Prefab) is PurchasedItem item)
                {
                    item.Quantity += 1;
                }
                else if (playerItem.Prefab.GetPriceInfo(CurrentLocation) != null)
                {
                    itemsToSell.Add(new PurchasedItem(playerItem.Prefab, 1));
                }
            }

            // Remove items from sell crate if they aren't in player inventory anymore
            var itemsInCrate = new List<PurchasedItem>(CargoManager.ItemsInSellCrate);
            foreach (PurchasedItem crateItem in itemsInCrate)
            {
                var playerItem = itemsToSell.Find(i => i.ItemPrefab == crateItem.ItemPrefab);
                var playerItemQuantity = playerItem != null ? playerItem.Quantity : 0;
                if (crateItem.Quantity > playerItemQuantity)
                {
                    CargoManager.ModifyItemQuantityInSellCrate(crateItem.ItemPrefab, playerItemQuantity - crateItem.Quantity);
                }
            }
            needsItemsToSellRefresh = false;
        }

        private void RefreshShoppingCrateList(List<PurchasedItem> items, GUIListBox listBox)
        {
            bool hasPermissions = HasPermissions;
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();
            int totalPrice = 0;
            foreach (PurchasedItem item in items)
            {
                PriceInfo priceInfo = item.ItemPrefab.GetPriceInfo(CurrentLocation);
                if (priceInfo == null) { continue; }

                var itemFrame = listBox.Content.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab.Identifier == item.ItemPrefab.Identifier);
                GUINumberInput numInput = null;
                if (itemFrame == null)
                {
                    itemFrame = CreateItemFrame(item, priceInfo, listBox, forceDisable: !hasPermissions);
                    numInput = itemFrame.FindChild(c => c is GUINumberInput, recursive: true) as GUINumberInput;
                }
                else
                {
                    itemFrame.UserData = item;
                    numInput = itemFrame.FindChild(c => c is GUINumberInput, recursive: true) as GUINumberInput;
                    if (numInput != null)
                    {
                        numInput.UserData = item;
                        numInput.Enabled = hasPermissions;
                    }
                    SetOwnedLabelText(itemFrame);
                    SetItemFrameStatus(itemFrame, hasPermissions);
                }
                existingItemFrames.Add(itemFrame);

                suppressBuySell = true;
                if (numInput != null)
                {
                    if (numInput.IntValue != item.Quantity) { itemFrame.Flash(GUI.Style.Green); }
                    numInput.IntValue = item.Quantity;
                }
                suppressBuySell = false;

                var price = listBox == shoppingCrateBuyList ?
                    CurrentLocation.GetAdjustedItemBuyPrice(item.ItemPrefab, priceInfo: priceInfo) :
                    CurrentLocation.GetAdjustedItemSellPrice(item.ItemPrefab, priceInfo: priceInfo);
                totalPrice += item.Quantity * price;
            }

            var removedItemFrames = listBox.Content.Children.Except(existingItemFrames).ToList();
            removedItemFrames.ForEach(f => listBox.Content.RemoveChild(f));

            SortItems(listBox, SortingMethod.CategoryAsc);
            listBox.UpdateScrollBarSize();       
            if (listBox == shoppingCrateBuyList)
            {
                buyTotal = totalPrice;
                if (IsBuying) { SetShoppingCrateTotalText(); }
            }
            else
            {
                sellTotal = totalPrice;
                if(IsSelling) { SetShoppingCrateTotalText(); }
            }
            SetClearAllButtonStatus();
            SetConfirmButtonStatus();
        }

        private void RefreshShoppingCrateBuyList() => RefreshShoppingCrateList(CargoManager.ItemsInBuyCrate, shoppingCrateBuyList);

        private void RefreshShoppingCrateSellList() => RefreshShoppingCrateList(CargoManager.ItemsInSellCrate, shoppingCrateSellList);

        private void SortItems(GUIListBox list, SortingMethod sortingMethod)
        {
            if (CurrentLocation == null) { return; }

            if (sortingMethod == SortingMethod.AlphabeticalAsc || sortingMethod == SortingMethod.AlphabeticalDesc)
            {
                list.Content.RectTransform.SortChildren(CompareByName);
                if (GetSpecialsGroup() is GUILayoutGroup specialsGroup)
                {
                    specialsGroup.RectTransform.SortChildren(CompareByName);
                    specialsGroup.Recalculate();
                }

                int CompareByName(RectTransform x, RectTransform y)
                {
                    if (x.GUIComponent.UserData is PurchasedItem itemX && y.GUIComponent.UserData is PurchasedItem itemY)
                    {
                        var sortResult = itemX.ItemPrefab.Name.CompareTo(itemY.ItemPrefab.Name);
                        if (sortingMethod == SortingMethod.AlphabeticalDesc) { sortResult *= -1; }
                        return sortResult;
                    }
                    else
                    {
                        return CompareByElement(x, y);
                    }
                }
            }
            else if (sortingMethod == SortingMethod.PriceAsc || sortingMethod == SortingMethod.PriceDesc)
            {
                SortItems(list, SortingMethod.AlphabeticalAsc);
                if (list == storeSellList || list == shoppingCrateSellList)
                {
                    list.Content.RectTransform.SortChildren(CompareBySellPrice);
                    if (GetSpecialsGroup() is GUILayoutGroup specialsGroup)
                    {
                        specialsGroup.RectTransform.SortChildren(CompareBySellPrice);
                        specialsGroup.Recalculate();
                    }

                    int CompareBySellPrice(RectTransform x, RectTransform y)
                    {
                        if (x.GUIComponent.UserData is PurchasedItem itemX && y.GUIComponent.UserData is PurchasedItem itemY)
                        {
                            var sortResult = CurrentLocation.GetAdjustedItemSellPrice(itemX.ItemPrefab).CompareTo(
                                CurrentLocation.GetAdjustedItemSellPrice(itemY.ItemPrefab));
                            if (sortingMethod == SortingMethod.PriceDesc) { sortResult *= -1; }
                            return sortResult;
                        }
                        else
                        {
                            return CompareByElement(x, y);
                        }
                    }
                }
                else
                {
                    list.Content.RectTransform.SortChildren(CompareByBuyPrice);
                    if (GetSpecialsGroup() is GUILayoutGroup specialsGroup)
                    {
                        specialsGroup.RectTransform.SortChildren(CompareByBuyPrice);
                        specialsGroup.Recalculate();
                    }

                    int CompareByBuyPrice(RectTransform x, RectTransform y)
                    {
                        if (x.GUIComponent.UserData is PurchasedItem itemX && y.GUIComponent.UserData is PurchasedItem itemY)
                        {
                            var sortResult = CurrentLocation.GetAdjustedItemBuyPrice(itemX.ItemPrefab).CompareTo(
                                CurrentLocation.GetAdjustedItemBuyPrice(itemY.ItemPrefab));
                            if (sortingMethod == SortingMethod.PriceDesc) { sortResult *= -1; }
                            return sortResult;
                        }
                        else
                        {
                            return CompareByElement(x, y);
                        }
                    }
                }
            }
            else if (sortingMethod == SortingMethod.CategoryAsc)
            {
                SortItems(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren(CompareByCategory);
                if (GetSpecialsGroup() is GUILayoutGroup specialsGroup)
                {
                    specialsGroup.RectTransform.SortChildren(CompareByCategory);
                    specialsGroup.Recalculate();
                }

                static int CompareByCategory(RectTransform x, RectTransform y)
                {
                    if (x.GUIComponent.UserData is PurchasedItem itemX && y.GUIComponent.UserData is PurchasedItem itemY)
                    {
                        return itemX.ItemPrefab.Category.CompareTo(itemY.ItemPrefab.Category);
                    }
                    else
                    {
                        return CompareByElement(x, y);
                    }
                }
            }

            GUILayoutGroup GetSpecialsGroup()
            {
                if (list == storeBuyList)
                {
                    return storeDailySpecialsGroup;
                }
                else if (list == storeSellList)
                {
                    return storeRequestedGoodGroup;
                }
                else
                {
                    return null;
                }
            }

            static int CompareByElement(RectTransform x, RectTransform y)
            {
                if (ShouldBeOnTop(x) || ShouldBeOnBottom(y))
                {
                    return -1;
                }
                else if (ShouldBeOnBottom(x) || ShouldBeOnTop(y))
                {
                    return 1;
                }
                else
                {
                    return 0;
                }

                static bool ShouldBeOnTop(RectTransform rt) =>
                            rt.GUIComponent.UserData is string id && (id == "deals" || id == "header");

                static bool ShouldBeOnBottom(RectTransform rt) =>
                    rt.GUIComponent.UserData is string id && id == "divider";
            }
        }

        private void SortItems(StoreTab tab, SortingMethod sortingMethod)
        {
            tabSortingMethods[tab] = sortingMethod;
            SortItems(tabLists[tab], sortingMethod);
        }

        private void SortItems(StoreTab tab) => SortItems(tab, tabSortingMethods[tab]);

        private void SortActiveTabItems(SortingMethod sortingMethod) => SortItems(activeTab, sortingMethod);

        private GUIComponent CreateItemFrame(PurchasedItem pi, PriceInfo priceInfo, GUIComponent parentComponent, bool forceDisable = false)
        {
            var tooltip = pi.ItemPrefab.Name;
            if (!string.IsNullOrWhiteSpace(pi.ItemPrefab.Description))
            {
                tooltip += "\n" + pi.ItemPrefab.Description;
            }

            GUIListBox parentListBox = parentComponent as GUIListBox;
            int width = 0;
            RectTransform parent = null;
            if (parentListBox != null)
            {
                width = parentListBox.Content.Rect.Width;
                parent = parentListBox.Content.RectTransform;
            }
            else
            {
                width = parentComponent.Rect.Width;
                parent = parentComponent.RectTransform;
            }

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(width, (int)(GUI.yScale * 80)), parent: parent), style: "ListBoxElement")
            {
                ToolTip = tooltip,
                UserData = pi
            };

            GUILayoutGroup mainGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), frame.RectTransform, Anchor.Center),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            var nameAndIconRelativeWidth = 0.635f;
            var iconRelativeWidth = 0.0f;
            var priceAndButtonRelativeWidth = 1.0f - nameAndIconRelativeWidth;

            Sprite itemIcon = pi.ItemPrefab.InventoryIcon ?? pi.ItemPrefab.sprite;
            if (itemIcon != null)
            {
                iconRelativeWidth = (0.9f * mainGroup.Rect.Height) / mainGroup.Rect.Width;
                GUIImage img = new GUIImage(new RectTransform(new Vector2(iconRelativeWidth, 0.9f), mainGroup.RectTransform), itemIcon, scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = (itemIcon == pi.ItemPrefab.InventoryIcon ? pi.ItemPrefab.InventoryIconColor : pi.ItemPrefab.SpriteColor) * (forceDisable ? 0.5f : 1.0f),
                    UserData = "icon"
                };
                img.RectTransform.MaxSize = img.Rect.Size;
            }

            GUIFrame nameAndQuantityFrame = new GUIFrame(new RectTransform(new Vector2(nameAndIconRelativeWidth - iconRelativeWidth, 1.0f), mainGroup.RectTransform), style: null)
            {
                CanBeFocused = false
            };
            GUILayoutGroup nameAndQuantityGroup = new GUILayoutGroup(new RectTransform(Vector2.One, nameAndQuantityFrame.RectTransform))
            {
                CanBeFocused = false,
                Stretch = true
            };
            var isSellingRelatedList = parentComponent == storeSellList || parentComponent == storeRequestedGoodGroup || parentComponent == shoppingCrateSellList;
            var locationHasDealOnItem = isSellingRelatedList ?
                CurrentLocation.RequestedGoods.Contains(pi.ItemPrefab) : CurrentLocation.DailySpecials.Contains(pi.ItemPrefab);
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), nameAndQuantityGroup.RectTransform),
                pi.ItemPrefab.Name, font: GUI.SubHeadingFont, textAlignment: Alignment.BottomLeft)
            {
                CanBeFocused = false,
                Shadow = locationHasDealOnItem,
                TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                TextScale = 0.85f,
                UserData = "name"
            };
            if (locationHasDealOnItem)
            {
                var relativeWidth = (0.9f * nameAndQuantityFrame.Rect.Height) / nameAndQuantityFrame.Rect.Width;
                var dealIcon = new GUIImage(
                    new RectTransform(new Vector2(relativeWidth, 0.9f), nameAndQuantityFrame.RectTransform, anchor: Anchor.CenterLeft)
                    {
                        AbsoluteOffset = new Point((int)nameBlock.Padding.X, 0)
                    },
                    "StoreDealIcon", scaleToFit: true)
                {
                    CanBeFocused = false
                };
                dealIcon.SetAsFirstChild();
            }
            var isParentOnLeftSideOfInterface = parentComponent == storeBuyList || parentComponent == storeDailySpecialsGroup ||
                parentComponent == storeSellList || parentComponent == storeRequestedGoodGroup;
            GUILayoutGroup shoppingCrateAmountGroup = null;
            GUINumberInput amountInput = null;
            if (isParentOnLeftSideOfInterface)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), nameAndQuantityGroup.RectTransform),
                    CreateQuantityLabelText(isSellingRelatedList ? StoreTab.Sell : StoreTab.Buy, pi.Quantity), font: GUI.Font, textAlignment: Alignment.BottomLeft)
                {
                    CanBeFocused = false,
                    Shadow = locationHasDealOnItem,
                    TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                    TextScale = 0.85f,
                    UserData = "quantitylabel"
                };
            }
            else if (!isParentOnLeftSideOfInterface)
            {
                var relativePadding = nameBlock.Padding.X / nameBlock.Rect.Width;
                shoppingCrateAmountGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - relativePadding, 0.6f), nameAndQuantityGroup.RectTransform) { RelativeOffset = new Vector2(relativePadding, 0) },
                    isHorizontal: true)
                {
                    RelativeSpacing = 0.02f
                };
                amountInput = new GUINumberInput(new RectTransform(new Vector2(0.4f, 1.0f), shoppingCrateAmountGroup.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = GetMaxAvailable(pi.ItemPrefab, isSellingRelatedList ? StoreTab.Sell : StoreTab.Buy),
                    UserData = pi,
                    IntValue = pi.Quantity
                };
                amountInput.Enabled = !forceDisable;
                amountInput.TextBox.OnSelected += (sender, key) => { suppressBuySell = true; };
                amountInput.TextBox.OnDeselected += (sender, key) => { suppressBuySell = false; amountInput.OnValueChanged?.Invoke(amountInput); };
                amountInput.OnValueChanged += (numberInput) =>
                {
                    if (suppressBuySell) { return; }
                    PurchasedItem purchasedItem = numberInput.UserData as PurchasedItem;
                    if (!HasPermissions)
                    {
                        numberInput.IntValue = purchasedItem.Quantity;
                        return;
                    }
                    AddToShoppingCrate(purchasedItem, quantity: numberInput.IntValue - purchasedItem.Quantity);
                };
                amountInput.PlusButton.ClickSound = GUISoundType.IncreaseQuantity;
                amountInput.MinusButton.ClickSound = GUISoundType.DecreaseQuantity;
                frame.HoverColor = frame.SelectedColor = Color.Transparent;
            }

            // Amount in players' inventories and on the sub
            var rectTransform = shoppingCrateAmountGroup == null ?
                new RectTransform(new Vector2(1.0f, 0.3f), nameAndQuantityGroup.RectTransform) :
                new RectTransform(new Vector2(0.6f, 1.0f), shoppingCrateAmountGroup.RectTransform);
            new GUITextBlock(rectTransform, CreateOwnedLabelText(OwnedItems.GetValueOrDefault(pi.ItemPrefab, 0)), font: GUI.Font,
                textAlignment: shoppingCrateAmountGroup == null ? Alignment.TopLeft : Alignment.CenterLeft)
            {
                CanBeFocused = false,
                Shadow = locationHasDealOnItem,
                TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                TextScale = 0.85f,
                UserData = "owned"
            };
            shoppingCrateAmountGroup?.Recalculate();

            var buttonRelativeWidth = (0.9f * mainGroup.Rect.Height) / mainGroup.Rect.Width;

            var priceFrame = new GUIFrame(new RectTransform(new Vector2(priceAndButtonRelativeWidth - buttonRelativeWidth, 1.0f), mainGroup.RectTransform), style: null)
            {
                CanBeFocused = false
            };
            var priceBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), priceFrame.RectTransform, anchor: Anchor.Center),
                "0 MK", font: GUI.SubHeadingFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                TextColor = locationHasDealOnItem ? storeSpecialColor : Color.White,
                UserData = "price"
            };
            priceBlock.Color *= (forceDisable ? 0.5f : 1.0f);
            priceBlock.CalculateHeightFromText();
            if (locationHasDealOnItem)
            {
                var undiscounterPriceBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1.0f, 0.25f), priceFrame.RectTransform, anchor: Anchor.Center)
                    {
                        AbsoluteOffset = new Point(0, priceBlock.RectTransform.ScaledSize.Y)
                    }, "", font: GUI.SmallFont, textAlignment: Alignment.Center)
                {
                    CanBeFocused = false,
                    Strikethrough = new GUITextBlock.StrikethroughSettings(color: priceBlock.TextColor, expand: 1),
                    TextColor = priceBlock.TextColor,
                    UserData = "undiscountedprice"
                };
            }
            SetPriceGetters(frame, !isSellingRelatedList);

            if (isParentOnLeftSideOfInterface)
            {
                new GUIButton(new RectTransform(new Vector2(buttonRelativeWidth, 0.9f), mainGroup.RectTransform), style: "StoreAddToCrateButton")
                {
                    ClickSound = GUISoundType.IncreaseQuantity,
                    Enabled = !forceDisable && pi.Quantity > 0,
                    ForceUpperCase = true,
                    UserData = "addbutton",
                    OnClicked = (button, userData) => AddToShoppingCrate(pi)
                };
            }
            else
            {
                new GUIButton(new RectTransform(new Vector2(buttonRelativeWidth, 0.9f), mainGroup.RectTransform), style: "StoreRemoveFromCrateButton")
                {
                    ClickSound = GUISoundType.DecreaseQuantity,
                    Enabled = !forceDisable,
                    ForceUpperCase = true,
                    UserData = "removebutton",
                    OnClicked = (button, userData) => ClearFromShoppingCrate(pi)
                };
            }

            if (parentListBox != null)
            {
                parentListBox.RecalculateChildren();
            }
            else if (parentComponent is GUILayoutGroup parentLayoutGroup)
            {
                parentLayoutGroup.Recalculate();
            }
            mainGroup.Recalculate();
            mainGroup.RectTransform.RecalculateChildren(true, true);
            amountInput?.LayoutGroup.Recalculate();
            nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);
            mainGroup.RectTransform.Children.ForEach(c => c.IsFixedSize = true);

            return frame;
        }

        private void UpdateOwnedItems()
        {
            OwnedItems.Clear();

            // Add items on the sub(s)
            Submarine.MainSub?.GetItems(true)
                .Where(i => i.Components.All(c => !(c is Holdable h) || !h.Attachable || !h.Attached) &&
                            i.Components.All(c => !(c is Wire w) || w.Connections.All(c => c == null)) &&
                            ItemAndAllContainersInteractable(i))
                .ForEach(i => AddToOwnedItems(i.Prefab));

            // Add items in character inventories
            foreach (var item in Item.ItemList)
            {
                if (item == null || item.Removed) { continue; }
                var rootInventoryOwner = item.GetRootInventoryOwner();
                var ownedByCrewMember = GameMain.GameSession.CrewManager.GetCharacters().Any(c => c == rootInventoryOwner);
                if (!ownedByCrewMember) { continue; }
                AddToOwnedItems(item.Prefab);
            }

            // Add items already purchased
            CargoManager?.PurchasedItems?.ForEach(pi => AddToOwnedItems(pi.ItemPrefab, amount: pi.Quantity));

            ownedItemsUpdateTimer = 0.0f;

            static bool ItemAndAllContainersInteractable(Item item)
            {
                do
                {
                    if (!item.IsPlayerTeamInteractable) { return false; }
                    item = item.Container;
                } while (item != null);
                return true;
            }

            void AddToOwnedItems(ItemPrefab itemPrefab, int amount = 1)
            {
                if (OwnedItems.ContainsKey(itemPrefab))
                {
                    OwnedItems[itemPrefab] += amount;
                }
                else
                {
                    OwnedItems.Add(itemPrefab, amount);
                }
            }
        }

        private void SetItemFrameStatus(GUIComponent itemFrame, bool enabled)
        {
            if (itemFrame == null || !(itemFrame.UserData is PurchasedItem pi)) { return; }

            if (itemFrame.FindChild("icon", recursive: true) is GUIImage icon)
            {
                if (pi.ItemPrefab?.InventoryIcon != null)
                {
                    icon.Color = pi.ItemPrefab.InventoryIconColor * (enabled ? 1.0f: 0.5f);
                }
                else if (pi.ItemPrefab?.sprite != null)
                {
                    icon.Color = pi.ItemPrefab.SpriteColor * (enabled ? 1.0f : 0.5f);
                }
            };

            var color = Color.White * (enabled ? 1.0f : 0.5f);

            if (itemFrame.FindChild("name", recursive: true) is GUITextBlock name)
            {
                name.TextColor = color;
            }

            if (itemFrame.FindChild("quantitylabel", recursive: true) is GUITextBlock qty)
            {
                qty.TextColor = color;
            }
            else if (itemFrame.FindChild(c => c is GUINumberInput, recursive: true) is GUINumberInput numberInput)
            {
                numberInput.Enabled = enabled;
            }

            if (itemFrame.FindChild("owned", recursive: true) is GUITextBlock ownedBlock)
            {
                ownedBlock.TextColor = color;
            }

            var isDiscounted = false;
            if (itemFrame.FindChild("undiscountedprice", recursive: true) is GUITextBlock undiscountedPriceBlock)
            {
                undiscountedPriceBlock.TextColor = color;
                undiscountedPriceBlock.Strikethrough.Color = color;
                isDiscounted = true;
            }

            if (itemFrame.FindChild("price", recursive: true) is GUITextBlock priceBlock)
            {
                priceBlock.TextColor = isDiscounted ? storeSpecialColor * (enabled ? 1.0f : 0.5f) : color;
            }

            if (itemFrame.FindChild("addbutton", recursive: true) is GUIButton addButton)
            {
                addButton.Enabled = enabled;
            }
            else if (itemFrame.FindChild("removebutton", recursive: true) is GUIButton removeButton)
            {
                removeButton.Enabled = enabled;
            }
        }

        private void SetQuantityLabelText(StoreTab mode, GUIComponent itemFrame)
        {
            if (itemFrame == null) { return; }
            if (itemFrame.FindChild("quantitylabel", recursive: true) is GUITextBlock label)
            {
                label.Text = CreateQuantityLabelText(mode, (itemFrame.UserData as PurchasedItem).Quantity);
            }
        }

        private string CreateQuantityLabelText(StoreTab mode, int quantity) => mode == StoreTab.Sell ?
            TextManager.GetWithVariable("campaignstore.quantity", "[amount]", quantity.ToString()) :
            TextManager.GetWithVariable("campaignstore.instock", "[amount]", quantity.ToString());

        private void SetOwnedLabelText(GUIComponent itemComponent)
        {
            if (itemComponent == null) { return; }
            var itemCount = 0;
            if (itemComponent.UserData is PurchasedItem pi)
            {
                itemCount = OwnedItems.GetValueOrDefault(pi.ItemPrefab, itemCount);
            }
            if (itemComponent.FindChild("owned", recursive: true) is GUITextBlock label)
            {
                label.Text = CreateOwnedLabelText(itemCount);
            }
        }

        private string CreateOwnedLabelText(int itemCount) => itemCount > 0 ?
            TextManager.GetWithVariable("campaignstore.owned", "[amount]", itemCount.ToString()) : "";

        private int GetMaxAvailable(ItemPrefab itemPrefab, StoreTab mode)
        {
            var list = mode == StoreTab.Sell ? itemsToSell : CurrentLocation.StoreStock;
            if (list.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem item)
            {
                if (mode != StoreTab.Sell)
                {
                    var purchasedItem = CargoManager.PurchasedItems.Find(i => i.ItemPrefab == item.ItemPrefab);
                    if (purchasedItem != null) { return Math.Max(item.Quantity - purchasedItem.Quantity, 0); }
                }
                return item.Quantity;
            }
            else
            {
                return 0;
            }
        }

        private string GetCurrencyFormatted(int amount) =>
            TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount));

        private bool ModifyBuyQuantity(PurchasedItem item, int quantity)
        {
            if (item == null || item.ItemPrefab == null) { return false; }
            if (!HasPermissions) { return false; }
            if (quantity > 0)
            {
                var itemInCrate = CargoManager.ItemsInBuyCrate.Find(i => i.ItemPrefab == item.ItemPrefab);
                if (itemInCrate != null && itemInCrate.Quantity >= CargoManager.MaxQuantity) { return false; }
                // Make sure there's enough available in the store
                var totalQuantityToBuy = itemInCrate != null ? itemInCrate.Quantity + quantity : quantity;
                if (totalQuantityToBuy > GetMaxAvailable(item.ItemPrefab, StoreTab.Buy)) { return false; }
            }
            CargoManager.ModifyItemQuantityInBuyCrate(item.ItemPrefab, quantity);
            GameMain.Client?.SendCampaignState();
            return false;
        }

        private bool ModifySellQuantity(PurchasedItem item, int quantity)
        {
            if (item == null || item.ItemPrefab == null) { return false; }
            if (!HasPermissions) { return false; }
            if (quantity > 0)
            {
                // Make sure there's enough available to sell
                var itemToSell = CargoManager.ItemsInSellCrate.Find(i => i.ItemPrefab == item.ItemPrefab);
                var totalQuantityToSell = itemToSell != null ? itemToSell.Quantity + quantity : quantity;
                if (totalQuantityToSell > GetMaxAvailable(item.ItemPrefab, StoreTab.Sell)) { return false; }
            }
            CargoManager.ModifyItemQuantityInSellCrate(item.ItemPrefab, quantity);
            //GameMain.Client?.SendCampaignState();
            return false;
        }

        private bool AddToShoppingCrate(PurchasedItem item, int quantity = 1) => IsBuying ?
            ModifyBuyQuantity(item, quantity) : ModifySellQuantity(item, quantity);

        private bool ClearFromShoppingCrate(PurchasedItem item) => IsBuying ?
            ModifyBuyQuantity(item, -item.Quantity) : ModifySellQuantity(item, -item.Quantity);

        private bool BuyItems()
        {
            if (!HasPermissions) { return false; }

            var itemsToPurchase = new List<PurchasedItem>(CargoManager.ItemsInBuyCrate);
            var itemsToRemove = new List<PurchasedItem>();
            var totalPrice = 0;
            foreach (PurchasedItem item in itemsToPurchase)
            {
                if (item?.ItemPrefab == null || !item.ItemPrefab.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo priceInfo))
                {
                    itemsToRemove.Add(item);
                    continue;
                }
                totalPrice += item.Quantity * CurrentLocation.GetAdjustedItemBuyPrice(item.ItemPrefab, priceInfo: priceInfo);
            }
            itemsToRemove.ForEach(i => itemsToPurchase.Remove(i));

            if (itemsToPurchase.None() || totalPrice > PlayerMoney) { return false; }

            CargoManager.PurchaseItems(itemsToPurchase, true);
            GameMain.Client?.SendCampaignState();

            var dialog = new GUIMessageBox(
                TextManager.Get("newsupplies"),
                TextManager.GetWithVariable("suppliespurchasedmessage", "[location]", campaignUI?.Campaign?.Map?.CurrentLocation?.Name),
                new string[] { TextManager.Get("Ok") });
            dialog.Buttons[0].OnClicked += dialog.Close;

            return false;
        }

        private bool SellItems()
        {
            if (!HasPermissions) { return false; }

            var itemsToSell = new List<PurchasedItem>(CargoManager.ItemsInSellCrate);
            var itemsToRemove = new List<PurchasedItem>();
            var totalValue = 0;
            foreach (PurchasedItem item in itemsToSell)
            {
                if (item?.ItemPrefab == null)
                {
                    itemsToRemove.Add(item);
                    continue;
                }
                if (item.ItemPrefab.GetPriceInfo(CurrentLocation) is PriceInfo priceInfo)
                {
                    totalValue += item.Quantity * CurrentLocation.GetAdjustedItemSellPrice(item.ItemPrefab, priceInfo: priceInfo);
                }
                else
                {
                    itemsToRemove.Add(item);
                }
            }
            itemsToRemove.ForEach(i => itemsToSell.Remove(i));

            if (itemsToSell.None() || totalValue > CurrentLocation.StoreCurrentBalance) { return false; }

            CargoManager.SellItems(itemsToSell);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private void SetShoppingCrateTotalText()
        {
            if (IsBuying)
            {
                shoppingCrateTotal.Text = GetCurrencyFormatted(buyTotal);
                shoppingCrateTotal.TextColor = buyTotal > PlayerMoney ? Color.Red : Color.White;
            }
            else
            {
                shoppingCrateTotal.Text = GetCurrencyFormatted(sellTotal);
                shoppingCrateTotal.TextColor = CurrentLocation != null && sellTotal > CurrentLocation.StoreCurrentBalance ? Color.Red : Color.White;
            }
        }

        private void SetConfirmButtonBehavior()
        {
            if (IsBuying)
            {
                confirmButton.Text = TextManager.Get("CampaignStore.Purchase");
                confirmButton.OnClicked = (b, o) => BuyItems();
            }
            else
            {
                confirmButton.Text = TextManager.Get("CampaignStoreTab.Sell");
                confirmButton.OnClicked = (b, o) =>
                {
                    var confirmDialog = new GUIMessageBox(
                        TextManager.Get("FireWarningHeader"),
                        TextManager.Get("CampaignStore.SellWarningText"),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    confirmDialog.Buttons[0].OnClicked = (b, o) => SellItems();
                    confirmDialog.Buttons[0].OnClicked += confirmDialog.Close;
                    confirmDialog.Buttons[1].OnClicked = confirmDialog.Close;
                    return true;
                };
            }
        }

        private void SetConfirmButtonStatus() => confirmButton.Enabled =
            HasPermissions && ActiveShoppingCrateList.Content.RectTransform.Children.Any() &&
            ((IsBuying && buyTotal <= PlayerMoney) || (IsSelling && CurrentLocation != null && sellTotal <= CurrentLocation.StoreCurrentBalance));

        private void SetClearAllButtonStatus() => clearAllButton.Enabled =
            HasPermissions && ActiveShoppingCrateList.Content.RectTransform.Children.Any();

        private float ownedItemsUpdateTimer = 0.0f;
        private readonly float ownedItemsUpdateInterval = 1.5f;

        public void Update(float deltaTime)
        {
            if (GameMain.GraphicsWidth != resolutionWhenCreated.X || GameMain.GraphicsHeight != resolutionWhenCreated.Y)
            {
                CreateUI();
            }
            else
            {
                // Update the owned items at short intervals and check if the interface should be refreshed
                ownedItemsUpdateTimer += deltaTime;
                if (ownedItemsUpdateTimer >= ownedItemsUpdateInterval)
                {
                    var prevOwnedItems = new Dictionary<ItemPrefab, int>(OwnedItems);
                    UpdateOwnedItems();
                    var refresh = (prevOwnedItems.Count != OwnedItems.Count) ||
                        (prevOwnedItems.Select(kvp => kvp.Value).Sum() != OwnedItems.Select(kvp => kvp.Value).Sum()) ||
                        (OwnedItems.Any(kvp => kvp.Value > 0 && !prevOwnedItems.ContainsKey(kvp.Key)) ||
                         prevOwnedItems.Any(kvp => !OwnedItems.TryGetValue(kvp.Key, out var itemCount) || kvp.Value != itemCount));
                    if (refresh)
                    {
                        needsItemsToSellRefresh = true;
                        needsRefresh = true;
                    }
                }
            }

            if (needsItemsToSellRefresh) { RefreshItemsToSell(); }
            if (needsRefresh || hadPermissions != HasPermissions) { Refresh(updateOwned: ownedItemsUpdateTimer > 0.0f); }
            if (needsBuyingRefresh) { RefreshBuying(); }
            if (needsSellingRefresh) { RefreshSelling(); }
        }
    }
}
