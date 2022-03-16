using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class Store
    {
        class ItemQuantity
        {
            public int Total { get; private set; }
            public int NonEmpty { get; private set; }
            public bool AllNonEmpty => NonEmpty == Total;

            public ItemQuantity(int total, bool areNonEmpty = true)
            {
                Total = total;
                NonEmpty = areNonEmpty ? total : 0;
            }

            public void Add(int amount, bool areNonEmpty)
            {
                Total += amount;
                if (areNonEmpty) { NonEmpty += amount; }
            }
        }

        private readonly CampaignUI campaignUI;
        private readonly GUIComponent parentComponent;
        private readonly List<GUIButton> storeTabButtons = new List<GUIButton>();
        private readonly List<GUIButton> itemCategoryButtons = new List<GUIButton>();
        private readonly Dictionary<StoreTab, GUIListBox> tabLists = new Dictionary<StoreTab, GUIListBox>();
        private readonly Dictionary<StoreTab, SortingMethod> tabSortingMethods = new Dictionary<StoreTab, SortingMethod>();
        private readonly List<PurchasedItem> itemsToSell = new List<PurchasedItem>();
        private readonly List<PurchasedItem> itemsToSellFromSub = new List<PurchasedItem>();

        private StoreTab activeTab = StoreTab.Buy;
        private MapEntityCategory? selectedItemCategory;
        private bool suppressBuySell;
        private int buyTotal, sellTotal, sellFromSubTotal;

        private GUITextBlock merchantBalanceBlock;
        private GUITextBlock currentSellValueBlock, newSellValueBlock;
        private GUIImage sellValueChangeArrow;
        private GUIDropDown sortingDropDown;
        private GUITextBox searchBox;
        private GUIListBox storeBuyList, storeSellList, storeSellFromSubList;
        /// <summary>
        /// Can be null when there are no deals at the current location
        /// </summary>
        private GUILayoutGroup storeDailySpecialsGroup, storeRequestedGoodGroup, storeRequestedSubGoodGroup;
        private Color storeSpecialColor;

        private GUIListBox shoppingCrateBuyList, shoppingCrateSellList, shoppingCrateSellFromSubList;
        private GUITextBlock relevantBalanceName, shoppingCrateTotal;
        private GUIButton clearAllButton, confirmButton;

        private bool needsRefresh, needsBuyingRefresh, needsSellingRefresh, needsItemsToSellRefresh, needsSellingFromSubRefresh, needsItemsToSellFromSubRefresh;

        private Point resolutionWhenCreated;

        private Dictionary<ItemPrefab, ItemQuantity> OwnedItems { get; } = new Dictionary<ItemPrefab, ItemQuantity>();

        private CargoManager CargoManager => campaignUI.Campaign.CargoManager;
        private Location CurrentLocation => campaignUI.Campaign.Map?.CurrentLocation;
        private Wallet PlayerWallet => campaignUI.Campaign.Wallet;
        private bool IsBuying => activeTab switch
        {
            StoreTab.Buy => true,
            StoreTab.Sell => false,
            StoreTab.SellSub => false,
            _ => throw new NotImplementedException()
        };
        private GUIListBox ActiveShoppingCrateList => activeTab switch
        {
            StoreTab.Buy => shoppingCrateBuyList,
            StoreTab.Sell => shoppingCrateSellList,
            StoreTab.SellSub => shoppingCrateSellFromSubList,
            _ => throw new NotImplementedException()
        };

        public enum StoreTab
        {
            /// <summary>
            /// Buy items from the store
            /// </summary>
            Buy,
            /// <summary>
            /// Sell items from the character inventory
            /// </summary>
            Sell,
            /// <summary>
            /// Sell items from the sub
            /// </summary>
            SellSub
        }

        private enum SortingMethod
        {
            AlphabeticalAsc,
            AlphabeticalDesc,
            PriceAsc,
            PriceDesc,
            CategoryAsc
        }

        #region Permissions

        private bool hadPermissions, hadBuyPermissions, hadSellInventoryPermissions, hadSellSubPermissions;

        private bool HasPermissions
        {
            get => GetPermissions();
            set => hadPermissions = value;
        }
        private bool HasBuyPermissions
        {
            get => HasPermissions || GetPermissions(StoreTab.Buy);
            set => hadBuyPermissions = value;
        }
        private bool HasSellInventoryPermissions
        {
            get => HasPermissions || GetPermissions(StoreTab.Sell);
            set => hadSellInventoryPermissions = value;
        }
        private bool HasSellSubPermissions
        {
            get => HasPermissions || GetPermissions(StoreTab.SellSub);
            set => hadSellSubPermissions = value;
        }

        private bool GetPermissions(StoreTab? tab = null)
        {
            if (!tab.HasValue)
            {
                return campaignUI.Campaign.AllowedToManageCampaign() || campaignUI.Campaign.AllowedToManageCampaign(Networking.ClientPermissions.CampaignStore);
            }
            else
            {
                return tab.Value switch
                {
                    StoreTab.Buy => campaignUI.Campaign.AllowedToManageCampaign(Networking.ClientPermissions.BuyItems),
                    StoreTab.Sell => campaignUI.Campaign.AllowedToManageCampaign(Networking.ClientPermissions.SellInventoryItems),
                    StoreTab.SellSub => campaignUI.Campaign.AllowedToManageCampaign(Networking.ClientPermissions.SellSubItems),
                    _ => false,
                };
            }
        }

        private void UpdatePermissions(StoreTab? tab = null)
        {
            HasPermissions = GetPermissions();
            if (!tab.HasValue)
            {
                HasBuyPermissions = GetPermissions(StoreTab.Buy);
                HasSellInventoryPermissions = GetPermissions(StoreTab.Sell);
                HasSellSubPermissions = GetPermissions(StoreTab.SellSub);
            }
            else
            {
                switch (tab.Value)
                {
                    case StoreTab.Buy:
                        HasBuyPermissions = GetPermissions(tab.Value);
                        break;
                    case StoreTab.Sell:
                        HasSellInventoryPermissions = GetPermissions(tab.Value);
                        break;
                    case StoreTab.SellSub:
                        HasSellSubPermissions = GetPermissions(tab.Value);
                        break;
                }
            }
        }

        private bool HasTabPermissions(StoreTab tab)
        {
            return tab switch
            {
                StoreTab.Buy => HasBuyPermissions,
                StoreTab.Sell => HasSellInventoryPermissions,
                StoreTab.SellSub => HasSellSubPermissions,
                _ => false
            };
        }

        private bool HasActiveTabPermissions()
        {
            return HasTabPermissions(activeTab);
        }

        private bool HavePermissionsChanged(StoreTab? tab = null)
        {
            if (!tab.HasValue)
            {
                return hadPermissions != HasPermissions;
            }
            else
            {
                bool hadTabPermissions = tab.Value switch
                {
                    StoreTab.Buy => hadBuyPermissions,
                    StoreTab.Sell => hadSellInventoryPermissions,
                    StoreTab.SellSub => hadSellSubPermissions,
                    _ => false
                };
                return hadTabPermissions != HasTabPermissions(tab.Value);
            }
        }

        #endregion

        public Store(CampaignUI campaignUI, GUIComponent parentComponent)
        {
            this.campaignUI = campaignUI;
            this.parentComponent = parentComponent;
            UpdatePermissions();
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
                needsItemsToSellFromSubRefresh = true;
                needsRefresh = true;
            };
            campaignUI.Campaign.CargoManager.OnItemsInSellFromSubCrateChanged += () => { needsSellingFromSubRefresh = true; };
        }

        public void Refresh(bool updateOwned = true)
        {
            UpdatePermissions();
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshBuying(updateOwned: false);
            RefreshSelling(updateOwned: false);
            RefreshSellingFromSub(updateOwned: false);
            needsRefresh = false;
        }

        private void RefreshBuying(bool updateOwned = true)
        {
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshShoppingCrateBuyList();
            RefreshStoreBuyList();
            bool hasPermissions = HasTabPermissions(StoreTab.Buy);
            storeBuyList.Enabled = hasPermissions;
            shoppingCrateBuyList.Enabled = hasPermissions;
            needsBuyingRefresh = false;
        }

        private void RefreshSelling(bool updateOwned = true)
        {
            if (updateOwned) { UpdateOwnedItems(); }
            RefreshShoppingCrateSellList();
            RefreshStoreSellList();
            bool hasPermissions = HasTabPermissions(StoreTab.Sell);
            storeSellList.Enabled = hasPermissions;
            shoppingCrateSellList.Enabled = hasPermissions;
            needsSellingRefresh = false;
        }

        private void RefreshSellingFromSub(bool updateOwned = true, bool updateItemsToSellFromSub = true)
        {
            if (updateOwned) { UpdateOwnedItems(); }
            if (updateItemsToSellFromSub) RefreshItemsToSellFromSub();
            RefreshShoppingCrateSellFromSubList();
            RefreshStoreSellFromSubList();
            bool hasPermissions = HasTabPermissions(StoreTab.SellSub);
            storeSellFromSubList.Enabled = hasPermissions;
            shoppingCrateSellFromSubList.Enabled = hasPermissions;
            needsSellingFromSubRefresh = false;
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
            var headerGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.95f / 14.0f), storeContent.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.005f
            };
            var imageWidth = (float)headerGroup.Rect.Height / headerGroup.Rect.Width;
            new GUIImage(new RectTransform(new Vector2(imageWidth, 1.0f), headerGroup.RectTransform), "StoreTradingIcon");
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("store"), font: GUIStyle.LargeFont)
            {
                CanBeFocused = false,
                ForceUpperCase = ForceUpperCase.Yes
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
                TextManager.Get("campaignstore.storebalance"), font: GUIStyle.Font, textAlignment: Alignment.BottomLeft)
            {
                AutoScaleVertical = true,
                ForceUpperCase = ForceUpperCase.Yes
            };
            merchantBalanceBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), merchantBalanceContainer.RectTransform),
                "", font: GUIStyle.SubHeadingFont)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = () =>
                {
                    merchantBalanceBlock.TextColor = CurrentLocation?.BalanceColor ?? Color.Red;
                    return GetMerchantBalanceText();
                } 
            };

            // Item sell value ------------------------------------------------
            var sellValueContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), balanceAndValueGroup.RectTransform))
            {
                CanBeFocused = true,
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), sellValueContainer.RectTransform),
                TextManager.Get("campaignstore.sellvalue"), font: GUIStyle.Font, textAlignment: Alignment.BottomLeft)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                ForceUpperCase = ForceUpperCase.Yes
            };

            var valueChangeGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), sellValueContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                RelativeSpacing = 0.02f
            };
            float blockWidth = GUI.IsFourByThree() ? 0.32f : 0.28f;
            Point blockMaxSize = new Point((int)(GameSettings.CurrentConfig.Graphics.TextScale * 60), valueChangeGroup.Rect.Height);
            currentSellValueBlock = new GUITextBlock(new RectTransform(new Vector2(blockWidth, 1.0f), valueChangeGroup.RectTransform) { MaxSize = blockMaxSize },
                "", font: GUIStyle.SubHeadingFont)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                TextScale = 1.1f,
                TextGetter = () =>
                {
                    if (CurrentLocation != null)
                    {
                        int balanceAfterTransaction = activeTab switch
                        {
                            StoreTab.Buy => CurrentLocation.StoreCurrentBalance + buyTotal,
                            StoreTab.Sell => CurrentLocation.StoreCurrentBalance - sellTotal,
                            StoreTab.SellSub => CurrentLocation.StoreCurrentBalance - sellFromSubTotal,
                            _ => throw new NotImplementedException(),
                        };
                        if (balanceAfterTransaction != CurrentLocation.StoreCurrentBalance)
                        {
                            var newStatus = CurrentLocation.GetStoreBalanceStatus(balanceAfterTransaction);
                            if (CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier != newStatus.SellPriceModifier)
                            {
                                string tooltipTag = newStatus.SellPriceModifier > CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier ?
                                    "campaingstore.valueincreasetooltip" : "campaingstore.valuedecreasetooltip";
                                sellValueContainer.ToolTip = TextManager.Get(tooltipTag);
                                currentSellValueBlock.TextColor = newStatus.Color;
                                sellValueChangeArrow.Color = newStatus.Color;
                                sellValueChangeArrow.Visible = true;
                                newSellValueBlock.TextColor = newStatus.Color;
                                newSellValueBlock.Text = $"{(newStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                                return $"{(CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                            }
                        }
                        sellValueContainer.ToolTip = TextManager.Get("campaignstore.sellvaluetooltip");
                        currentSellValueBlock.TextColor = CurrentLocation.BalanceColor;
                        sellValueChangeArrow.Visible = false;
                        newSellValueBlock.Text = null;
                        return $"{(CurrentLocation.ActiveStoreBalanceStatus.SellPriceModifier * 100).FormatZeroDecimal()} %";
                    }
                    else
                    {
                        sellValueContainer.ToolTip = null;
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
                "", font: GUIStyle.SubHeadingFont)
            {
                AutoScaleVertical = true,
                CanBeFocused = false,
                TextScale = 1.1f
            };
            newPadding = newSellValueBlock.Padding;
            newPadding.X = 0;
            newSellValueBlock.Padding = newPadding;

            // Store mode buttons ------------------------------------------------
            var modeButtonFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.4f / 14.0f), storeContent.RectTransform), style: null);
            var modeButtonContainer = new GUILayoutGroup(new RectTransform(Vector2.One, modeButtonFrame.RectTransform), isHorizontal: true);

            var tabs = Enum.GetValues(typeof(StoreTab));
            storeTabButtons.Clear();
            tabSortingMethods.Clear();
            foreach (StoreTab tab in tabs)
            {
                LocalizedString text = tab switch
                {
                    StoreTab.SellSub => TextManager.Get("submarine"),
                    _ => TextManager.Get("campaignstoretab." + tab)
                };
                var tabButton = new GUIButton(new RectTransform(new Vector2(1.0f / (tabs.Length + 1), 1.0f), modeButtonContainer.RectTransform),
                    text: text, style: "GUITabButton")
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
            var categoryButton = new GUIButton(new RectTransform(new Point(categoryButtonContainer.Rect.Width, categoryButtonContainer.Rect.Width), categoryButtonContainer.RectTransform), style: "CategoryButton.All")
            {
                ToolTip = TextManager.Get("MapEntityCategory.All"),
                OnClicked = OnClickedCategoryButton
            };
            itemCategoryButtons.Add(categoryButton);
            foreach (MapEntityCategory category in itemCategories)
            {
                categoryButton = new GUIButton(new RectTransform(new Point(categoryButtonContainer.Rect.Width, categoryButtonContainer.Rect.Width), categoryButtonContainer.RectTransform),
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
                if (newCategory.HasValue) { searchBox.Text = ""; }
                if (newCategory != selectedItemCategory) { tabLists[activeTab].ScrollBar.BarScroll = 0f; }
                FilterStoreItems(newCategory, searchBox.Text);
                return true;
            }
            foreach (var btn in itemCategoryButtons)
            {
                btn.RectTransform.SizeChanged += () =>
                {
                    var sprite = btn.Frame.sprites[GUIComponent.ComponentState.None].First();
                    btn.RectTransform.NonScaledSize = new Point(btn.Rect.Width, (int)(btn.Rect.Width * ((float)sprite.Sprite.SourceRect.Height / sprite.Sprite.SourceRect.Width)));
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

            storeSellFromSubList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
            storeRequestedSubGoodGroup = CreateDealsGroup(storeSellFromSubList);
            tabLists.Add(StoreTab.SellSub, storeSellFromSubList);

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
            new GUITextBlock(new RectTransform(new Vector2(1.0f - imageWidth, 1.0f), headerGroup.RectTransform), TextManager.Get("campaignstore.shoppingcrate"), font: GUIStyle.LargeFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                ForceUpperCase = ForceUpperCase.Yes
            };

            // Player balance ------------------------------------------------
            var playerBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), shoppingCrateContent.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.005f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                TextManager.Get("campaignstore.balance"), font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
            {
                AutoScaleVertical = true,
                ForceUpperCase = ForceUpperCase.Yes
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), playerBalanceContainer.RectTransform),
                "", textColor: Color.White, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.TopRight)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = GetPlayerBalanceText
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
            var shoppingCrateListContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), shoppingCrateInventoryContainer.RectTransform), style: null);
            shoppingCrateBuyList = new GUIListBox(new RectTransform(Vector2.One, shoppingCrateListContainer.RectTransform)) { Visible = false };
            shoppingCrateSellList = new GUIListBox(new RectTransform(Vector2.One, shoppingCrateListContainer.RectTransform)) { Visible = false };
            shoppingCrateSellFromSubList = new GUIListBox(new RectTransform(Vector2.One, shoppingCrateListContainer.RectTransform)) { Visible = false };

            var relevantBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), shoppingCrateInventoryContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            relevantBalanceName = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), relevantBalanceContainer.RectTransform), "", font: GUIStyle.Font)
            {
                CanBeFocused = false
            };
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), relevantBalanceContainer.RectTransform), "", textColor: Color.White, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                TextScale = 1.1f,
                TextGetter = () => IsBuying ? GetPlayerBalanceText() : GetMerchantBalanceText()
            };

            var totalContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), shoppingCrateInventoryContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), TextManager.Get("campaignstore.total"), font: GUIStyle.Font)
            {
                CanBeFocused = false
            };
            shoppingCrateTotal = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), "", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Right)
            {
                CanBeFocused = false,
                TextScale = 1.1f
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), shoppingCrateInventoryContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight);
            confirmButton = new GUIButton(new RectTransform(new Vector2(0.35f, 1.0f), buttonContainer.RectTransform))
            {
                ForceUpperCase = ForceUpperCase.Yes
            };
            SetConfirmButtonBehavior();
            clearAllButton = new GUIButton(new RectTransform(new Vector2(0.35f, 1.0f), buttonContainer.RectTransform), TextManager.Get("campaignstore.clearall"))
            {
                ClickSound = GUISoundType.DecreaseQuantity,
                Enabled = HasActiveTabPermissions(),
                ForceUpperCase = ForceUpperCase.Yes,
                OnClicked = (button, userData) =>
                {
                    if (!HasActiveTabPermissions()) { return false; }
                    var itemsToRemove = activeTab switch
                    {
                        StoreTab.Buy => new List<PurchasedItem>(CargoManager.ItemsInBuyCrate),
                        StoreTab.Sell => new List<PurchasedItem>(CargoManager.ItemsInSellCrate),
                        StoreTab.SellSub => new List<PurchasedItem>(CargoManager.ItemsInSellFromSubCrate),
                        _ => throw new NotImplementedException(),
                    };
                    itemsToRemove.ForEach(i => ClearFromShoppingCrate(i));
                    return true;
                }
            };

            Refresh();
            ChangeStoreTab(activeTab);
            resolutionWhenCreated = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private LocalizedString GetMerchantBalanceText() => GetCurrencyFormatted(CurrentLocation?.StoreCurrentBalance ?? 0);

        private LocalizedString GetPlayerBalanceText() => GetCurrencyFormatted(PlayerWallet.Balance);

        private GUILayoutGroup CreateDealsGroup(GUIListBox parentList, int elementCount = 4)
        {
            var elementHeight = (int)(GUI.yScale * 80);
            var frame = new GUIFrame(new RectTransform(new Point(parentList.Content.Rect.Width, elementCount * elementHeight + 3), parent: parentList.Content.RectTransform), style: null)
            {
                UserData = "deals"
            };
            var dealsGroup = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter);
            var dealsHeader = new GUILayoutGroup(new RectTransform(new Point((int)(0.95f * parentList.Content.Rect.Width), elementHeight), parent: dealsGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                UserData = "header"
            };
            var iconWidth = (0.9f * dealsHeader.Rect.Height) / dealsHeader.Rect.Width;
            var dealsIcon = new GUIImage(new RectTransform(new Vector2(iconWidth, 0.9f), dealsHeader.RectTransform), "StoreDealIcon", scaleToFit: true);
            var text = TextManager.Get(parentList == storeBuyList ? "campaignstore.dailyspecials" : "campaignstore.requestedgoods");
            var dealsText = new GUITextBlock(new RectTransform(new Vector2(1.0f - iconWidth, 0.9f), dealsHeader.RectTransform), text, font: GUIStyle.LargeFont);
            storeSpecialColor = dealsIcon.Color;
            dealsText.TextColor = storeSpecialColor;
            var divider = new GUIImage(new RectTransform(new Point(dealsGroup.Rect.Width, 3), dealsGroup.RectTransform), "HorizontalLine")
            {
                UserData = "divider"
            };
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
            if (ItemPrefab.Prefabs.Any(p => p.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo _)))
            {
                selectedItemCategory = null;
                searchBox.Text = "";
                ChangeStoreTab(StoreTab.Buy);
                if (newLocation?.Reputation != null)
                {
                    newLocation.Reputation.OnReputationValueChanged += () => { needsRefresh = true; };
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
            relevantBalanceName.Text = IsBuying ? TextManager.Get("campaignstore.balance") : TextManager.Get("campaignstore.storebalance");
            SetShoppingCrateTotalText();
            SetClearAllButtonStatus();
            SetConfirmButtonBehavior();
            SetConfirmButtonStatus();
            FilterStoreItems();
            switch (tab)
            {
                case StoreTab.Buy:
                    storeSellList.Visible = false;
                    if (storeSellFromSubList != null)
                    {
                        storeSellFromSubList.Visible = false;
                    }
                    storeBuyList.Visible = true;
                    shoppingCrateSellList.Visible = false;
                    if (shoppingCrateSellFromSubList != null)
                    {
                        shoppingCrateSellFromSubList.Visible = false;
                    }
                    shoppingCrateBuyList.Visible = true;
                    break;
                case StoreTab.Sell:
                    storeBuyList.Visible = false;
                    if (storeSellFromSubList != null)
                    {
                        storeSellFromSubList.Visible = false;
                    }
                    storeSellList.Visible = true;
                    shoppingCrateBuyList.Visible = false;
                    if (shoppingCrateSellFromSubList != null)
                    {
                        shoppingCrateSellFromSubList.Visible = false;
                    }
                    shoppingCrateSellList.Visible = true;
                    break;
                case StoreTab.SellSub:
                    storeBuyList.Visible = false;
                    storeSellList.Visible = false;
                    if (storeSellFromSubList != null)
                    {
                        storeSellFromSubList.Visible = true;
                    }
                    shoppingCrateBuyList.Visible = false;
                    shoppingCrateSellList.Visible = false;
                    if (shoppingCrateSellFromSubList != null)
                    {
                        shoppingCrateSellFromSubList.Visible = true;
                    }
                    break;
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
                    (string.IsNullOrEmpty(filter) || item.ItemPrefab.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }
            foreach (GUIButton btn in itemCategoryButtons)
            {
                btn.Selected = (MapEntityCategory?)btn.UserData == selectedItemCategory;
            }
            list.UpdateScrollBarSize();
        }

        private void FilterStoreItems()
        {
            //only select a specific category if the search box is empty (items from all categories are shown when searching)
            MapEntityCategory? category = string.IsNullOrEmpty(searchBox.Text) ? selectedItemCategory : null;
            FilterStoreItems(category, searchBox.Text);
        }

        int prevDailySpecialCount;

        private void RefreshStoreBuyList()
        {
            float prevBuyListScroll = storeBuyList.BarScroll;
            float prevShoppingCrateScroll = shoppingCrateBuyList.BarScroll;

            bool hasPermissions = HasBuyPermissions;
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();

            int dailySpecialCount = CurrentLocation?.DailySpecials.Count() ?? 3;

            if ((storeDailySpecialsGroup != null) != CurrentLocation.DailySpecials.Any() || dailySpecialCount != prevDailySpecialCount)
            {
                if (storeDailySpecialsGroup == null || dailySpecialCount != prevDailySpecialCount)
                {
                    storeBuyList.RemoveChild(storeDailySpecialsGroup?.Parent);
                    storeDailySpecialsGroup = CreateDealsGroup(storeBuyList, 1 + dailySpecialCount);
                    storeDailySpecialsGroup.Parent.SetAsFirstChild();
                }
                else
                {
                    storeBuyList.RemoveChild(storeDailySpecialsGroup.Parent);
                    storeDailySpecialsGroup = null;
                }
                storeBuyList.RecalculateChildren();
                prevDailySpecialCount = dailySpecialCount;
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
                        itemFrame = CreateItemFrame(new PurchasedItem(itemPrefab, quantity), parentComponent, StoreTab.Buy, forceDisable: !hasPermissions);
                    }
                    else
                    {
                        (itemFrame.UserData as PurchasedItem).Quantity = quantity;
                        SetQuantityLabelText(StoreTab.Buy, itemFrame);
                        SetOwnedText(itemFrame);
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
            if (activeTab == StoreTab.Buy) { FilterStoreItems(); }
            SortItems(StoreTab.Buy);

            storeBuyList.BarScroll = prevBuyListScroll;
            shoppingCrateBuyList.BarScroll = prevShoppingCrateScroll;
        }

        private void RefreshStoreSellList()
        {
            float prevSellListScroll = storeSellList.BarScroll;
            float prevShoppingCrateScroll = shoppingCrateSellList.BarScroll;
            bool hasPermissions = HasTabPermissions(StoreTab.Sell);
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
                    itemFrame = CreateItemFrame(new PurchasedItem(itemPrefab, itemQuantity), parentComponent, StoreTab.Sell, forceDisable: !hasPermissions);
                }
                else
                {
                    (itemFrame.UserData as PurchasedItem).Quantity = itemQuantity;
                    SetQuantityLabelText(StoreTab.Sell, itemFrame);
                    SetOwnedText(itemFrame);
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
            if (activeTab == StoreTab.Sell) { FilterStoreItems(); }
            SortItems(StoreTab.Sell);

            storeSellList.BarScroll = prevSellListScroll;
            shoppingCrateSellList.BarScroll = prevShoppingCrateScroll;
        }

        private void RefreshStoreSellFromSubList()
        {
            float prevSellListScroll = storeSellFromSubList.BarScroll;
            float prevShoppingCrateScroll = shoppingCrateSellFromSubList.BarScroll;
            bool hasPermissions = HasSellSubPermissions;
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();

            if ((storeRequestedSubGoodGroup != null) != CurrentLocation.RequestedGoods.Any())
            {
                if (storeRequestedSubGoodGroup == null)
                {
                    storeRequestedSubGoodGroup = CreateDealsGroup(storeSellList);
                    storeRequestedSubGoodGroup.Parent.SetAsFirstChild();
                }
                else
                {
                    storeSellFromSubList.RemoveChild(storeRequestedSubGoodGroup.Parent);
                    storeRequestedSubGoodGroup = null;
                }
                storeSellFromSubList.RecalculateChildren();
            }

            foreach (PurchasedItem item in itemsToSellFromSub)
            {
                CreateOrUpdateItemFrame(item.ItemPrefab, item.Quantity);
            }

            foreach (var requestedGood in CurrentLocation.RequestedGoods)
            {
                if (itemsToSellFromSub.Any(pi => pi.ItemPrefab == requestedGood)) { continue; }
                CreateOrUpdateItemFrame(requestedGood, 0);
            }

            void CreateOrUpdateItemFrame(ItemPrefab itemPrefab, int itemQuantity)
            {
                PriceInfo priceInfo = itemPrefab.GetPriceInfo(CurrentLocation);
                if (priceInfo == null) { return; }
                var isRequestedGood = CurrentLocation.RequestedGoods.Contains(itemPrefab);
                var itemFrame = isRequestedGood ?
                    storeRequestedSubGoodGroup.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab) :
                    storeSellFromSubList.Content.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == itemPrefab);
                if (CargoManager.ItemsInSellFromSubCrate.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem itemInSellFromSubCrate)
                {
                    itemQuantity = Math.Max(itemQuantity - itemInSellFromSubCrate.Quantity, 0);
                }
                if (itemFrame == null)
                {
                    var parentComponent = isRequestedGood ? storeRequestedSubGoodGroup : storeSellFromSubList as GUIComponent;
                    itemFrame = CreateItemFrame(new PurchasedItem(itemPrefab, itemQuantity), parentComponent, StoreTab.SellSub, forceDisable: !hasPermissions);
                }
                else
                {
                    (itemFrame.UserData as PurchasedItem).Quantity = itemQuantity;
                    SetQuantityLabelText(StoreTab.SellSub, itemFrame);
                    SetOwnedText(itemFrame);
                    SetPriceGetters(itemFrame, false);
                }
                SetItemFrameStatus(itemFrame, hasPermissions && itemQuantity > 0);
                if (itemQuantity < 1 && !isRequestedGood)
                {
                    itemFrame.Visible = false;
                }
                existingItemFrames.Add(itemFrame);
            }

            var removedItemFrames = storeSellFromSubList.Content.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList();
            if (storeRequestedSubGoodGroup != null)
            {
                removedItemFrames.AddRange(storeRequestedSubGoodGroup.Children.Where(c => c.UserData is PurchasedItem).Except(existingItemFrames).ToList());
            }
            removedItemFrames.ForEach(f => f.RectTransform.Parent = null);
            if (activeTab == StoreTab.SellSub) { FilterStoreItems(); }
            SortItems(StoreTab.SellSub);

            storeSellFromSubList.BarScroll = prevSellListScroll;
            shoppingCrateSellFromSubList.BarScroll = prevShoppingCrateScroll;
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

        public void RefreshItemsToSellFromSub()
        {
            itemsToSellFromSub.Clear();
            var subItems = CargoManager.GetSellableItemsFromSub();
            foreach (Item subItem in subItems)
            {
                if (itemsToSellFromSub.FirstOrDefault(i => i.ItemPrefab == subItem.Prefab) is PurchasedItem item)
                {
                    item.Quantity += 1;
                }
                else if (subItem.Prefab.GetPriceInfo(CurrentLocation) != null)
                {
                    itemsToSellFromSub.Add(new PurchasedItem(subItem.Prefab, 1));
                }
            }

            // Remove items from sell crate if they aren't on the sub anymore
            var itemsInCrate = new List<PurchasedItem>(CargoManager.ItemsInSellFromSubCrate);
            foreach (PurchasedItem crateItem in itemsInCrate)
            {
                var subItem = itemsToSellFromSub.Find(i => i.ItemPrefab == crateItem.ItemPrefab);
                var subItemQuantity = subItem != null ? subItem.Quantity : 0;
                if (crateItem.Quantity > subItemQuantity)
                {
                    CargoManager.ModifyItemQuantityInSellFromSubCrate(crateItem.ItemPrefab, subItemQuantity - crateItem.Quantity);
                }
            }
            sellableItemsFromSubUpdateTimer = 0.0f;
            needsItemsToSellFromSubRefresh = false;
        }

        private void RefreshShoppingCrateList(List<PurchasedItem> items, GUIListBox listBox, StoreTab tab)
        {
            bool hasPermissions = HasTabPermissions(tab);
            HashSet<GUIComponent> existingItemFrames = new HashSet<GUIComponent>();
            int totalPrice = 0;
            foreach (PurchasedItem item in items)
            {
                if (!(item.ItemPrefab.GetPriceInfo(CurrentLocation) is { } priceInfo)) { continue; }
                GUINumberInput numInput = null;
                if (!(listBox.Content.FindChild(c => c.UserData is PurchasedItem pi && pi.ItemPrefab.Identifier == item.ItemPrefab.Identifier) is { } itemFrame))
                {
                    itemFrame = CreateItemFrame(item, listBox, tab, forceDisable: !hasPermissions);
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
                        numInput.MaxValueInt = GetMaxAvailable(item.ItemPrefab, tab);
                    }
                    SetOwnedText(itemFrame);
                    SetItemFrameStatus(itemFrame, hasPermissions);
                }
                existingItemFrames.Add(itemFrame);

                suppressBuySell = true;
                if (numInput != null)
                {
                    if (numInput.IntValue != item.Quantity) { itemFrame.Flash(GUIStyle.Green); }
                    numInput.IntValue = item.Quantity;
                }
                suppressBuySell = false;

                try
                {
                    int price = tab switch
                    {
                        StoreTab.Buy => CurrentLocation.GetAdjustedItemBuyPrice(item.ItemPrefab, priceInfo: priceInfo),
                        StoreTab.Sell => CurrentLocation.GetAdjustedItemSellPrice(item.ItemPrefab, priceInfo: priceInfo),
                        StoreTab.SellSub => CurrentLocation.GetAdjustedItemSellPrice(item.ItemPrefab, priceInfo: priceInfo),
                        _ => throw new NotImplementedException()
                    };
                    totalPrice += item.Quantity * price;
                }
                catch (NotImplementedException e)
                {
                    DebugConsole.ShowError($"Error getting item price: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
                }
            }

            var removedItemFrames = listBox.Content.Children.Except(existingItemFrames).ToList();
            removedItemFrames.ForEach(f => listBox.Content.RemoveChild(f));

            SortItems(listBox, SortingMethod.CategoryAsc);
            listBox.UpdateScrollBarSize();
            switch (tab)
            {
                case StoreTab.Buy:
                    buyTotal = totalPrice;
                    break;
                case StoreTab.Sell:
                    sellTotal = totalPrice;
                    break;
                case StoreTab.SellSub:
                    sellFromSubTotal = totalPrice;
                    break;
            }
            if (activeTab == tab)
            {
                SetShoppingCrateTotalText();
            }
            SetClearAllButtonStatus();
            SetConfirmButtonStatus();
        }

        private void RefreshShoppingCrateBuyList() => RefreshShoppingCrateList(CargoManager.ItemsInBuyCrate, shoppingCrateBuyList, StoreTab.Buy);

        private void RefreshShoppingCrateSellList() => RefreshShoppingCrateList(CargoManager.ItemsInSellCrate, shoppingCrateSellList, StoreTab.Sell);

        private void RefreshShoppingCrateSellFromSubList() => RefreshShoppingCrateList(CargoManager.ItemsInSellFromSubCrate, shoppingCrateSellFromSubList, StoreTab.SellSub);

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
                        int sortResult = itemX.ItemPrefab.Name != itemY.ItemPrefab.Name ?
                            itemX.ItemPrefab.Name.CompareTo(itemY.ItemPrefab.Name) :
                            itemX.ItemPrefab.Identifier.CompareTo(itemY.ItemPrefab.Identifier);
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
                if (list != storeBuyList && list != shoppingCrateBuyList)
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
                else if (list == storeSellFromSubList)
                {
                    return storeRequestedSubGoodGroup;
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

        private void SortItems(StoreTab tab)
        {
            SortItems(tab, tabSortingMethods[tab]);
        }

        private void SortActiveTabItems(SortingMethod sortingMethod) => SortItems(activeTab, sortingMethod);

        private GUIComponent CreateItemFrame(PurchasedItem pi, GUIComponent parentComponent, StoreTab containingTab, bool forceDisable = false)
        {
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

            if ((pi.ItemPrefab.InventoryIcon ?? pi.ItemPrefab.Sprite) is { } itemIcon)
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
            bool isSellingRelatedList = containingTab != StoreTab.Buy;
            bool locationHasDealOnItem = isSellingRelatedList ?
                CurrentLocation.RequestedGoods.Contains(pi.ItemPrefab) : CurrentLocation.DailySpecials.Contains(pi.ItemPrefab);
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), nameAndQuantityGroup.RectTransform),
                pi.ItemPrefab.Name, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomLeft)
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
            bool isParentOnLeftSideOfInterface = parentComponent == storeBuyList || parentComponent == storeDailySpecialsGroup ||
                parentComponent == storeSellList || parentComponent == storeRequestedGoodGroup ||
                parentComponent == storeSellFromSubList || parentComponent == storeRequestedSubGoodGroup;
            GUILayoutGroup shoppingCrateAmountGroup = null;
            GUINumberInput amountInput = null;
            if (isParentOnLeftSideOfInterface)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), nameAndQuantityGroup.RectTransform),
                    CreateQuantityLabelText(containingTab, pi.Quantity), font: GUIStyle.Font, textAlignment: Alignment.BottomLeft)
                {
                    CanBeFocused = false,
                    Shadow = locationHasDealOnItem,
                    TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                    TextScale = 0.85f,
                    UserData = "quantitylabel"
                };
            }
            else
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
                    MaxValueInt = GetMaxAvailable(pi.ItemPrefab, containingTab),
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
                    if (!HasActiveTabPermissions())
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
            var ownedLabel = new GUITextBlock(rectTransform, string.Empty, font: GUIStyle.Font, textAlignment: shoppingCrateAmountGroup == null ? Alignment.TopLeft : Alignment.CenterLeft)
            {
                CanBeFocused = false,
                Shadow = locationHasDealOnItem,
                TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                TextScale = 0.85f,
                UserData = "owned"
            };
            SetOwnedText(frame, ownedLabel);
            shoppingCrateAmountGroup?.Recalculate();

            var buttonRelativeWidth = (0.9f * mainGroup.Rect.Height) / mainGroup.Rect.Width;

            var priceFrame = new GUIFrame(new RectTransform(new Vector2(priceAndButtonRelativeWidth - buttonRelativeWidth, 1.0f), mainGroup.RectTransform), style: null)
            {
                CanBeFocused = false
            };
            var priceBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), priceFrame.RectTransform, anchor: Anchor.Center),
                "0 MK", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Right)
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
                    }, "", font: GUIStyle.SmallFont, textAlignment: Alignment.Center)
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
                    ForceUpperCase = ForceUpperCase.Yes,
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
                    ForceUpperCase = ForceUpperCase.Yes,
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
            if (Submarine.MainSub?.GetItems(true) is List<Item> subItems)
            {
                foreach (var subItem in subItems)
                {
                    if (!subItem.Components.All(c => !(c is Holdable h) || !h.Attachable || !h.Attached)) { continue; }
                    if (!subItem.Components.All(c => !(c is Wire w) || w.Connections.All(c => c == null))) { continue; }
                    if (!ItemAndAllContainersInteractable(subItem)) { continue; }
                    AddOwnedItem(subItem);
                }
            }

            // Add items in character inventories
            foreach (var item in Item.ItemList)
            {
                if (item == null || item.Removed) { continue; }
                var rootInventoryOwner = item.GetRootInventoryOwner();
                var ownedByCrewMember = GameMain.GameSession.CrewManager.GetCharacters().Any(c => c == rootInventoryOwner);
                if (!ownedByCrewMember) { continue; }
                AddOwnedItem(item);
            }

            // Add items already purchased
            CargoManager?.PurchasedItems?.ForEach(pi => AddNonEmptyOwnedItems(pi));

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

            void AddOwnedItem(Item item)
            {
                if (!(item?.Prefab.GetPriceInfo(CurrentLocation) is PriceInfo priceInfo)) { return; }
                bool isNonEmpty = !priceInfo.DisplayNonEmpty || item.ConditionPercentage > 5.0f;
                if (OwnedItems.TryGetValue(item.Prefab, out ItemQuantity itemQuantity))
                {
                    OwnedItems[item.Prefab].Add(1, isNonEmpty);
                }
                else
                {
                    OwnedItems.Add(item.Prefab, new ItemQuantity(1, areNonEmpty: isNonEmpty));
                }
            }

            void AddNonEmptyOwnedItems(PurchasedItem purchasedItem)
            {
                if (purchasedItem == null) { return; }
                if (OwnedItems.TryGetValue(purchasedItem.ItemPrefab, out ItemQuantity itemQuantity))
                {
                    OwnedItems[purchasedItem.ItemPrefab].Add(purchasedItem.Quantity, true);
                }
                else
                {
                    OwnedItems.Add(purchasedItem.ItemPrefab, new ItemQuantity(purchasedItem.Quantity));
                }
            }
        }

        private void SetItemFrameStatus(GUIComponent itemFrame, bool enabled)
        {
            if (!(itemFrame?.UserData is PurchasedItem pi)) { return; }
            bool refreshFrameStatus = !pi.IsStoreComponentEnabled.HasValue || pi.IsStoreComponentEnabled.Value != enabled;
            if (!refreshFrameStatus) { return; }
            if (itemFrame.FindChild("icon", recursive: true) is GUIImage icon)
            {
                if (pi.ItemPrefab?.InventoryIcon != null)
                {
                    icon.Color = pi.ItemPrefab.InventoryIconColor * (enabled ? 1.0f: 0.5f);
                }
                else if (pi.ItemPrefab?.Sprite != null)
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
            bool isDiscounted = false;
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
            pi.IsStoreComponentEnabled = enabled;
            itemFrame.UserData = pi;
        }

        private static void SetQuantityLabelText(StoreTab mode, GUIComponent itemFrame)
        {
            if (itemFrame?.FindChild("quantitylabel", recursive: true) is GUITextBlock label)
            {
                label.Text = CreateQuantityLabelText(mode, (itemFrame.UserData as PurchasedItem).Quantity);
            }
        }

        private static LocalizedString CreateQuantityLabelText(StoreTab mode, int quantity)
        {
            try
            {
                string textTag = mode switch
                {
                    StoreTab.Buy => "campaignstore.instock",
                    StoreTab.Sell => "campaignstore.ownedinventory",
                    StoreTab.SellSub => "campaignstore.ownedsub",
                    _ => throw new NotImplementedException()
                };
                return TextManager.GetWithVariable(textTag, "[amount]", quantity.ToString());
            }
            catch (NotImplementedException e)
            {
                string errorMsg = $"Error creating a store quantity label text: unknown store tab.\n{e.StackTrace.CleanupStackTrace()}";
#if DEBUG
                DebugConsole.ShowError(errorMsg);
#else
                DebugConsole.AddWarning(errorMsg);
#endif
            }
            return string.Empty;
        }

        private void SetOwnedText(GUIComponent itemComponent, GUITextBlock ownedLabel = null)
        {
            ownedLabel ??= itemComponent?.FindChild("owned", recursive: true) as GUITextBlock;
            if (itemComponent == null && ownedLabel == null) { return; }
            PurchasedItem purchasedItem = itemComponent?.UserData as PurchasedItem;
            ItemQuantity itemQuantity = null;
            LocalizedString ownedLabelText = string.Empty;
            if (purchasedItem != null && OwnedItems.TryGetValue(purchasedItem.ItemPrefab, out itemQuantity) && itemQuantity.Total > 0)
            {
                if (itemQuantity.AllNonEmpty)
                {
                    ownedLabelText = TextManager.GetWithVariable("campaignstore.owned", "[amount]", itemQuantity.Total.ToString());
                }
                else
                {
                    ownedLabelText = TextManager.GetWithVariables("campaignstore.ownedspecific",
                        ("[nonempty]", itemQuantity.NonEmpty.ToString()),
                        ("[total]", itemQuantity.Total.ToString()));
                }
            }
            if (itemComponent != null)
            {
                LocalizedString toolTip = string.Empty;
                if (purchasedItem.ItemPrefab != null)
                {
                    toolTip = purchasedItem.ItemPrefab.Name;
                    if (!purchasedItem.ItemPrefab.Description.IsNullOrEmpty())
                    {
                        toolTip += $"\n{purchasedItem.ItemPrefab.Description}";
                    }
                    if (itemQuantity != null)
                    {
                        if (itemQuantity.AllNonEmpty)
                        {
                            toolTip += $"\n\n{ownedLabelText}";
                        }
                        else
                        {
                            toolTip += $"\n\n{TextManager.GetWithVariable("campaignstore.ownednonempty", "[amount]", itemQuantity.NonEmpty.ToString())}";
                            toolTip += $"\n{TextManager.GetWithVariable("campaignstore.ownedtotal", "[amount]", itemQuantity.Total.ToString())}";
                        }
                    }
                }
                itemComponent.ToolTip = toolTip;
            }
            if (ownedLabel != null)
            {
                ownedLabel.Text = ownedLabelText;
            }
        }

        private int GetMaxAvailable(ItemPrefab itemPrefab, StoreTab mode)
        {
            List<PurchasedItem> list = null;
            try
            {
                list = mode switch
                {
                    StoreTab.Buy => CurrentLocation?.StoreStock,
                    StoreTab.Sell => itemsToSell,
                    StoreTab.SellSub => itemsToSellFromSub,
                    _ => throw new NotImplementedException()
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error getting item availability: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
            }
            if (list != null && list.Find(i => i.ItemPrefab == itemPrefab) is PurchasedItem item)
            {
                if (mode == StoreTab.Buy)
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

        private LocalizedString GetCurrencyFormatted(int amount) =>
            TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", amount));

        private bool ModifyBuyQuantity(PurchasedItem item, int quantity)
        {
            if (item?.ItemPrefab == null) { return false; }
            if (!HasBuyPermissions) { return false; }
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
            return true;
        }

        private bool ModifySellQuantity(PurchasedItem item, int quantity)
        {
            if (item?.ItemPrefab == null) { return false; }
            if (!HasSellInventoryPermissions) { return false; }
            if (quantity > 0)
            {
                // Make sure there's enough available to sell
                var itemToSell = CargoManager.ItemsInSellCrate.Find(i => i.ItemPrefab == item.ItemPrefab);
                var totalQuantityToSell = itemToSell != null ? itemToSell.Quantity + quantity : quantity;
                if (totalQuantityToSell > GetMaxAvailable(item.ItemPrefab, StoreTab.Sell)) { return false; }
            }
            CargoManager.ModifyItemQuantityInSellCrate(item.ItemPrefab, quantity);
            return true;
        }

        private bool ModifySellFromSubQuantity(PurchasedItem item, int quantity)
        {
            if (item?.ItemPrefab == null) { return false; }
            if (!HasSellSubPermissions) { return false; }
            if (quantity > 0)
            {
                // Make sure there's enough available to sell
                var itemToSell = CargoManager.ItemsInSellFromSubCrate.Find(i => i.ItemPrefab == item.ItemPrefab);
                var totalQuantityToSell = itemToSell != null ? itemToSell.Quantity + quantity : quantity;
                if (totalQuantityToSell > GetMaxAvailable(item.ItemPrefab, StoreTab.SellSub)) { return false; }
            }
            CargoManager.ModifyItemQuantityInSellFromSubCrate(item.ItemPrefab, quantity);
            GameMain.Client?.SendCampaignState();
            return true;
        }

        private bool AddToShoppingCrate(PurchasedItem item, int quantity = 1)
        {
            if (item == null) { return false; }
            try
            {
                return activeTab switch
                {
                    StoreTab.Buy => ModifyBuyQuantity(item, quantity),
                    StoreTab.Sell => ModifySellQuantity(item, quantity),
                    StoreTab.SellSub => ModifySellFromSubQuantity(item, quantity),
                    _ => throw new NotImplementedException()
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error adding an item to the shopping crate: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
                return false;
            }
        }

        private bool ClearFromShoppingCrate(PurchasedItem item)
        {
            if (item == null) { return false; }
            try
            {
                return activeTab switch
                {
                    StoreTab.Buy => ModifyBuyQuantity(item, -item.Quantity),
                    StoreTab.Sell => ModifySellQuantity(item, -item.Quantity),
                    StoreTab.SellSub => ModifySellFromSubQuantity(item, -item.Quantity),
                    _ => throw new NotImplementedException(),
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error clearing the shopping crate: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
                return false;
            }
        }

        private bool BuyItems()
        {
            if (!HasBuyPermissions) { return false; }

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

            if (itemsToPurchase.None() || !PlayerWallet.CanAfford(totalPrice)) { return false; }

            CargoManager.PurchaseItems(itemsToPurchase, true);
            GameMain.Client?.SendCampaignState();

            var dialog = new GUIMessageBox(
                TextManager.Get("newsupplies"),
                TextManager.GetWithVariable("suppliespurchasedmessage", "[location]", campaignUI?.Campaign?.Map?.CurrentLocation?.Name),
                new LocalizedString[] { TextManager.Get("Ok") });
            dialog.Buttons[0].OnClicked += dialog.Close;

            return false;
        }

        private bool SellItems()
        {
            if (!HasActiveTabPermissions()) { return false; }
            List<PurchasedItem> itemsToSell;
            try
            {
                itemsToSell = activeTab switch
                {
                    StoreTab.Sell => new List<PurchasedItem>(CargoManager.ItemsInSellCrate),
                    StoreTab.SellSub => new List<PurchasedItem>(CargoManager.ItemsInSellFromSubCrate),
                    _ => throw new NotImplementedException()
                };
            }
            catch (NotImplementedException e)
            {
                DebugConsole.ShowError($"Error confirming the store transaction: Uknown store tab type. {e.StackTrace.CleanupStackTrace()}");
                return false;
            }
            var itemsToRemove = new List<PurchasedItem>();
            int totalValue = 0;
            foreach (PurchasedItem item in itemsToSell)
            {
                if (item?.ItemPrefab?.GetPriceInfo(CurrentLocation) is PriceInfo priceInfo)
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
            CargoManager.SellItems(itemsToSell, activeTab);
            GameMain.Client?.SendCampaignState();
            return false;
        }

        private void SetShoppingCrateTotalText()
        {
            if (IsBuying)
            {
                shoppingCrateTotal.Text = GetCurrencyFormatted(buyTotal);
                shoppingCrateTotal.TextColor = !PlayerWallet.CanAfford(buyTotal) ? Color.Red : Color.White;
            }
            else
            {
                int total = activeTab switch
                {
                    StoreTab.Sell => sellTotal,
                    StoreTab.SellSub => sellFromSubTotal,
                    _ => throw new NotImplementedException(),
                };
                shoppingCrateTotal.Text = GetCurrencyFormatted(total);
                shoppingCrateTotal.TextColor = CurrentLocation != null && total > CurrentLocation.StoreCurrentBalance ? Color.Red : Color.White;
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
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    confirmDialog.Buttons[0].OnClicked = (b, o) => SellItems();
                    confirmDialog.Buttons[0].OnClicked += confirmDialog.Close;
                    confirmDialog.Buttons[1].OnClicked = confirmDialog.Close;
                    return true;
                };
            }
        }

        private void SetConfirmButtonStatus()
        {
            confirmButton.Enabled =
                HasActiveTabPermissions() &&
                ActiveShoppingCrateList.Content.RectTransform.Children.Any() &&
                activeTab switch
                {
                    StoreTab.Buy => PlayerWallet.CanAfford(buyTotal),
                    StoreTab.Sell => CurrentLocation != null && sellTotal <= CurrentLocation.StoreCurrentBalance,
                    StoreTab.SellSub => CurrentLocation != null && sellFromSubTotal <= CurrentLocation.StoreCurrentBalance,
                    _ => false
                };
        }

        private void SetClearAllButtonStatus()
        {
            clearAllButton.Enabled =
                HasActiveTabPermissions() &&
                ActiveShoppingCrateList.Content.RectTransform.Children.Any();
        }

        private float ownedItemsUpdateTimer = 0.0f, sellableItemsFromSubUpdateTimer = 0.0f;
        private const float timerUpdateInterval = 1.5f;
        private readonly Stopwatch updateStopwatch = new Stopwatch();

        public void Update(float deltaTime)
        {
            updateStopwatch.Restart();

            if (GameMain.GraphicsWidth != resolutionWhenCreated.X || GameMain.GraphicsHeight != resolutionWhenCreated.Y)
            {
                CreateUI();
            }
            else
            {
                // Update the owned items at short intervals and check if the interface should be refreshed
                ownedItemsUpdateTimer += deltaTime;
                if (ownedItemsUpdateTimer >= timerUpdateInterval)
                {
                    var prevOwnedItems = new Dictionary<ItemPrefab, ItemQuantity>(OwnedItems);
                    UpdateOwnedItems();
                    bool refresh = OwnedItems.Count != prevOwnedItems.Count ||
                        OwnedItems.Values.Sum(v => v.Total) != prevOwnedItems.Values.Sum(v => v.Total) ||
                        OwnedItems.Any(kvp => !prevOwnedItems.TryGetValue(kvp.Key, out ItemQuantity v) || kvp.Value.Total != v.Total) ||
                        prevOwnedItems.Any(kvp => !OwnedItems.ContainsKey(kvp.Key));
                    if (refresh)
                    {
                        needsItemsToSellRefresh = true;
                        needsRefresh = true;
                    }
                }
                // Update the sellable sub items at short intervals and check if the interface should be refreshed
                sellableItemsFromSubUpdateTimer += deltaTime;
                if (sellableItemsFromSubUpdateTimer >= timerUpdateInterval)
                {
                    var prevSubItems = new List<PurchasedItem>(itemsToSellFromSub);
                    RefreshItemsToSellFromSub();
                    needsRefresh = needsRefresh ||
                        itemsToSellFromSub.Count != prevSubItems.Count ||
                        itemsToSellFromSub.Sum(i => i.Quantity) != prevSubItems.Sum(i => i.Quantity) ||
                        itemsToSellFromSub.Any(i => !(prevSubItems.FirstOrDefault(prev => prev.ItemPrefab == i.ItemPrefab) is PurchasedItem prev) || i.Quantity != prev.Quantity) ||
                        prevSubItems.Any(prev => itemsToSellFromSub.None(i => i.ItemPrefab == prev.ItemPrefab));
                }
            }

            if (needsItemsToSellRefresh) { RefreshItemsToSell(); }
            if (needsItemsToSellFromSubRefresh) { RefreshItemsToSellFromSub(); }
            if (needsRefresh || HavePermissionsChanged()) { Refresh(updateOwned: ownedItemsUpdateTimer > 0.0f); }
            if (needsBuyingRefresh || HavePermissionsChanged(StoreTab.Buy)) { RefreshBuying(updateOwned: ownedItemsUpdateTimer > 0.0f); }
            if (needsSellingRefresh || HavePermissionsChanged(StoreTab.Sell)) { RefreshSelling(updateOwned: ownedItemsUpdateTimer > 0.0f); }
            if (needsSellingFromSubRefresh || HavePermissionsChanged(StoreTab.SellSub)) { RefreshSellingFromSub(updateOwned: ownedItemsUpdateTimer > 0.0f, updateItemsToSellFromSub: sellableItemsFromSubUpdateTimer > 0.0f); }

            updateStopwatch.Stop();
            GameMain.PerformanceCounter.AddPartialElapsedTicks("GameSessionUpdate", "StoreUpdate", updateStopwatch.ElapsedTicks);
        }
    }
}
