using Barotrauma.Extensions;
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
        private GUIDropDown sortingDropDown;
        private GUITextBox searchBox;
        private GUIListBox storeDealsList, storeBuyList, storeSellList;

        private GUIListBox shoppingCrateBuyList, shoppingCrateSellList;
        private GUITextBlock shoppingCrateTotal;
        private GUIButton clearAllButton, confirmButton;

        private Point resolutionWhenCreated;
        private bool hadPermissions;

        private CargoManager CargoManager => campaignUI.Campaign.CargoManager;
        private Location CurrentLocation => campaignUI.Campaign.Map?.CurrentLocation;
        private int PlayerMoney => campaignUI.Campaign.Money;
        private bool HasPermissions => campaignUI.Campaign.AllowedToManageCampaign();
        private bool IsBuying => activeTab != StoreTab.Sell;
        private bool IsSelling => activeTab == StoreTab.Sell;
        private GUIListBox ActiveShoppingCrateList => IsBuying ? shoppingCrateBuyList : shoppingCrateSellList;

        private enum StoreTab
        {
            Deals,
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
            campaignUI.Campaign.CargoManager.OnItemsInBuyCrateChanged += RefreshBuying;
            campaignUI.Campaign.CargoManager.OnPurchasedItemsChanged += RefreshBuying;
            campaignUI.Campaign.CargoManager.OnItemsInSellCrateChanged += RefreshSelling;
            campaignUI.Campaign.CargoManager.OnSoldItemsChanged += () =>
            {
                RefreshItemsToSell();
                RefreshSelling();
            };
        }

        public void Refresh()
        {
            hadPermissions = HasPermissions;
            RefreshBuying();
            RefreshSelling();
        }

        private void RefreshBuying()
        {
            RefreshShoppingCrateBuyList();
            //RefreshStoreDealsList();
            RefreshStoreBuyList();
            var hasPermissions = HasPermissions;
            //storeDealsList.Enabled = hasPermissions;
            storeBuyList.Enabled = hasPermissions;
            shoppingCrateBuyList.Enabled = hasPermissions;
        }

        private void RefreshSelling()
        {
            RefreshShoppingCrateSellList();
            RefreshStoreSellList();
            var hasPermissions = HasPermissions;
            storeSellList.Enabled = hasPermissions;
            shoppingCrateSellList.Enabled = hasPermissions;
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
            var merchantBalanceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f / 14.0f), storeContent.RectTransform))
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
                "", font: GUI.SubHeadingFont, textAlignment: Alignment.TopLeft)
            {
                AutoScaleVertical = true,
                TextScale = 1.1f,
                TextGetter = () =>
                {
                    var balance = CurrentLocation != null ? CurrentLocation.StoreCurrentBalance : 0;
                    if (balance < (int)(0.25f * Location.StoreInitialBalance))
                    {
                        merchantBalanceBlock.TextColor = Color.Red;
                    }
                    else if (balance < (int)(0.5f * Location.StoreInitialBalance))
                    {
                        merchantBalanceBlock.TextColor = Color.Orange;
                    }
                    else
                    {
                        merchantBalanceBlock.TextColor = Color.White;
                    }
                    return GetCurrencyFormatted(balance);
                } 
            };

            // Store mode buttons ------------------------------------------------
            var modeButtonFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f / 14.0f), storeContent.RectTransform), style: null);
            var modeButtonContainer = new GUILayoutGroup(new RectTransform(Vector2.One, modeButtonFrame.RectTransform), isHorizontal: true);

            var tabs = Enum.GetValues(typeof(StoreTab));
            storeTabButtons.Clear();
            tabSortingMethods.Clear();
            foreach (StoreTab tab in tabs)
            {
                // TODO: Remove the row below once the deal page is implemented
                if (tab == StoreTab.Deals) { continue; }
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
            storeDealsList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
            tabLists.Clear();
            tabLists.Add(StoreTab.Deals, storeDealsList);
            storeBuyList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
            tabLists.Add(StoreTab.Buy, storeBuyList);
            storeSellList = new GUIListBox(new RectTransform(Vector2.One, storeItemListContainer.RectTransform))
            {
                AutoHideScrollBar = false,
                Visible = false
            };
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
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), TextManager.Get("campaignstore.total"), font: GUI.Font);
            shoppingCrateTotal = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), totalContainer.RectTransform), "", font: GUI.SubHeadingFont, textAlignment: Alignment.Right)
            {
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

        private void UpdateLocation(Location prevLocation, Location newLocation)
        {
            if (prevLocation == newLocation) { return; }

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (itemPrefab.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo _))
                {
                    ChangeStoreTab(StoreTab.Buy);
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
            if (tab == StoreTab.Deals)
            {
                storeBuyList.Visible = false;
                storeSellList.Visible = false;
                storeDealsList.Visible = true;
                shoppingCrateSellList.Visible = false;
                shoppingCrateBuyList.Visible = true;
            }
            else if (tab == StoreTab.Buy)
            {
                storeDealsList.Visible = false;
                storeSellList.Visible = false;
                storeBuyList.Visible = true;
                shoppingCrateSellList.Visible = false;
                shoppingCrateBuyList.Visible = true;
            }
            else if (tab == StoreTab.Sell)
            {
                storeDealsList.Visible = false;
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
            foreach (PurchasedItem item in CurrentLocation.StoreStock)
            {
                if (item.ItemPrefab.CanBeBoughtAtLocation(CurrentLocation, out PriceInfo priceInfo))
                {
                    var itemFrame = storeBuyList.Content.Children.FirstOrDefault(c => c.UserData is PurchasedItem pi && pi.ItemPrefab == item.ItemPrefab);
                    var quantity = item.Quantity;
                    if (CargoManager.PurchasedItems.Find(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem purchasedItem)
                    {
                        quantity = Math.Max(quantity - purchasedItem.Quantity, 0);
                    }
                    if (CargoManager.ItemsInBuyCrate.Find(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem itemInBuyCrate)
                    {
                        quantity = Math.Max(quantity - itemInBuyCrate.Quantity, 0);
                    }
                    if (itemFrame == null)
                    {
                        itemFrame = CreateItemFrame(new PurchasedItem(item.ItemPrefab, quantity), priceInfo, storeBuyList, forceDisable: !hasPermissions);
                    }
                    else
                    {
                        (itemFrame.UserData as PurchasedItem).Quantity = quantity;
                        SetQuantityLabelText(StoreTab.Buy, itemFrame);
                        SetItemFrameStatus(itemFrame, hasPermissions && quantity > 0);
                    }
                    existingItemFrames.Add(itemFrame);
                }
            }

            var removedItemFrames = storeBuyList.Content.Children.Except(existingItemFrames).ToList();
            removedItemFrames.ForEach(f => storeBuyList.Content.RemoveChild(f));
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
            foreach (PurchasedItem item in itemsToSell)
            {
                PriceInfo priceInfo = item.ItemPrefab.GetPriceInfo(CurrentLocation);
                if (priceInfo == null) { continue; }
                var itemFrame = storeSellList.Content.FindChild(c => c.UserData is PurchasedItem i && i.ItemPrefab == item.ItemPrefab);
                var quantity = item.Quantity;
                if (CargoManager.ItemsInSellCrate.Find(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem itemInSellCrate)
                {
                    quantity = Math.Max(quantity - itemInSellCrate.Quantity, 0);
                }
                if (itemFrame == null)
                {
                    itemFrame = CreateItemFrame(new PurchasedItem(item.ItemPrefab, quantity), priceInfo, storeSellList, forceDisable: !hasPermissions);
                }
                else
                {
                    (itemFrame.UserData as PurchasedItem).Quantity = quantity;
                    SetQuantityLabelText(StoreTab.Sell, itemFrame);
                    SetItemFrameStatus(itemFrame, hasPermissions);
                }
                if (quantity < 1) { itemFrame.Visible = false; }
                existingItemFrames.Add(itemFrame);
            }

            var removedItemFrames = storeSellList.Content.Children.Except(existingItemFrames).ToList();
            removedItemFrames.ForEach(f => storeSellList.Content.RemoveChild(f));
            if (IsSelling) { FilterStoreItems(); }
            SortItems(StoreTab.Sell);

            storeSellList.BarScroll = prevSellListScroll;
            shoppingCrateSellList.BarScroll = prevShoppingCrateScroll;
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

                if (priceInfo != null)
                {
                    var price = listBox == shoppingCrateBuyList ?
                        CurrentLocation.GetAdjustedItemBuyPrice(priceInfo) :
                        CurrentLocation.GetAdjustedItemSellPrice(priceInfo);
                    totalPrice += item.Quantity * price;
                }
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
            if (sortingMethod == SortingMethod.AlphabeticalAsc || sortingMethod == SortingMethod.AlphabeticalDesc)
            {
                list.Content.RectTransform.SortChildren(
                        (x, y) => (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name));
                if (sortingMethod == SortingMethod.AlphabeticalDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            else if (sortingMethod == SortingMethod.PriceAsc || sortingMethod == SortingMethod.PriceDesc)
            {
                SortItems(list, SortingMethod.AlphabeticalAsc);
                if (list == storeSellList || list == shoppingCrateSellList)
                {
                    list.Content.RectTransform.SortChildren(
                        (x, y) => CurrentLocation.GetAdjustedItemSellPrice((x.GUIComponent.UserData as PurchasedItem).ItemPrefab).CompareTo(
                            CurrentLocation.GetAdjustedItemSellPrice((y.GUIComponent.UserData as PurchasedItem).ItemPrefab)));
                }
                else
                {
                    list.Content.RectTransform.SortChildren(
                        (x, y) => CurrentLocation.GetAdjustedItemBuyPrice((x.GUIComponent.UserData as PurchasedItem).ItemPrefab).CompareTo(
                            CurrentLocation.GetAdjustedItemBuyPrice((y.GUIComponent.UserData as PurchasedItem).ItemPrefab)));
                }
                if (sortingMethod == SortingMethod.PriceDesc) { list.Content.RectTransform.ReverseChildren(); }
            }
            else if (sortingMethod == SortingMethod.CategoryAsc)
            {
                SortItems(list, SortingMethod.AlphabeticalAsc);
                list.Content.RectTransform.SortChildren((x, y) =>
                    (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category));
            }
        }

        private void SortItems(StoreTab tab, SortingMethod sortingMethod)
        {
            tabSortingMethods[tab] = sortingMethod;
            SortItems(tabLists[tab], sortingMethod);
        }

        private void SortItems(StoreTab tab) => SortItems(tab, tabSortingMethods[tab]);

        private void SortActiveTabItems(SortingMethod sortingMethod) => SortItems(activeTab, sortingMethod);

        private GUIComponent CreateItemFrame(PurchasedItem pi, PriceInfo priceInfo, GUIListBox listBox, bool forceDisable = false)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, (int)(GUI.yScale * 60)), parent: listBox.Content.RectTransform), style: "ListBoxElement")
            {
                ToolTip = pi.ItemPrefab.Description,
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
                    Color = (itemIcon == pi.ItemPrefab.InventoryIcon ? pi.ItemPrefab.InventoryIconColor : pi.ItemPrefab.SpriteColor) * (forceDisable ? 0.5f : 1.0f),
                    UserData = "icon"
                };
                img.RectTransform.MaxSize = img.Rect.Size;
            }

            GUILayoutGroup nameAndQuantityGroup = new GUILayoutGroup(new RectTransform(new Vector2(nameAndIconRelativeWidth - iconRelativeWidth, 1.0f), mainGroup.RectTransform))
            {
                Stretch = true,
                ToolTip = pi.ItemPrefab.Description
            };
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndQuantityGroup.RectTransform),
                pi.ItemPrefab.Name, font: GUI.SubHeadingFont, textAlignment: Alignment.BottomLeft)
            {
                CanBeFocused = false,
                TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                TextScale = 0.85f,
                UserData = "name"
            };
            GUINumberInput amountInput = null;
            if (listBox == storeBuyList || listBox == storeSellList)
            {
                var block = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), nameAndQuantityGroup.RectTransform),
                    CreateQuantityLabelText(listBox == storeSellList ? StoreTab.Sell : StoreTab.Buy, pi.Quantity), font: GUI.Font, textAlignment: Alignment.TopLeft)
                {
                    CanBeFocused = false,
                    TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                    TextScale = 0.85f,
                    UserData = "quantitylabel"
                };
            }
            else if (listBox == shoppingCrateBuyList || listBox == shoppingCrateSellList)
            {
                amountInput = new GUINumberInput(new RectTransform(new Vector2(0.5f), nameAndQuantityGroup.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = GetMaxAvailable(pi.ItemPrefab, listBox == shoppingCrateBuyList ? StoreTab.Buy : StoreTab.Sell),
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
                frame.HoverColor = frame.SelectedColor = Color.Transparent;
            }

            var buttonRelativeWidth = (0.9f * mainGroup.Rect.Height) / mainGroup.Rect.Width;

            var priceBlock = new GUITextBlock(new RectTransform(new Vector2(priceAndButtonRelativeWidth - buttonRelativeWidth, 1.0f), mainGroup.RectTransform), "", font: GUI.SubHeadingFont, textAlignment: Alignment.Right)
            {
                TextColor = Color.White * (forceDisable ? 0.5f : 1.0f),
                ToolTip = pi.ItemPrefab.Description,
                UserData = "price"
            };
            if(listBox == storeSellList || listBox == shoppingCrateSellList)
            {
                priceBlock.TextGetter = () => GetCurrencyFormatted(CurrentLocation.GetAdjustedItemSellPrice(priceInfo));
            }
            else
            {
                priceBlock.TextGetter = () => GetCurrencyFormatted(CurrentLocation.GetAdjustedItemBuyPrice(priceInfo));
            }

            if (listBox == storeDealsList || listBox == storeBuyList || listBox == storeSellList)
            {
                new GUIButton(new RectTransform(new Vector2(buttonRelativeWidth, 0.9f), mainGroup.RectTransform), style: "StoreAddToCrateButton")
                {
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
                    Enabled = !forceDisable,
                    ForceUpperCase = true,
                    UserData = "removebutton",
                    OnClicked = (button, userData) => ClearFromShoppingCrate(pi)
                };
            }

            listBox.RecalculateChildren();
            mainGroup.Recalculate();
            mainGroup.RectTransform.RecalculateChildren(true, true);
            amountInput?.LayoutGroup.Recalculate();
            nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);
            mainGroup.RectTransform.Children.ForEach(c => c.IsFixedSize = true);

            return frame;
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

            if (itemFrame.FindChild("price", recursive: true) is GUITextBlock price)
            {
                price.TextColor = color;
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
            if (itemFrame?.FindChild("quantitylabel", recursive: true) is GUITextBlock label)
            {
                label.Text = CreateQuantityLabelText(mode, (itemFrame.UserData as PurchasedItem).Quantity);
            }
        }

        private string CreateQuantityLabelText(StoreTab mode, int quantity) => mode == StoreTab.Sell ?
            TextManager.GetWithVariable("campaignstore.quantity", "[amount]", quantity.ToString()) :
            TextManager.GetWithVariable("campaignstore.instock", "[amount]", quantity.ToString());

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
                totalPrice += item.Quantity * CurrentLocation.GetAdjustedItemBuyPrice(priceInfo);
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
                    totalValue += item.Quantity * CurrentLocation.GetAdjustedItemSellPrice(priceInfo);
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

        public void Update()
        {
            if (GameMain.GraphicsWidth != resolutionWhenCreated.X || GameMain.GraphicsHeight != resolutionWhenCreated.Y)
            {
                CreateUI();
            }
            else if (hadPermissions != HasPermissions)
            {
                Refresh();
            }
        }
    }
}
