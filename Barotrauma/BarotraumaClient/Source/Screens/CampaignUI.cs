using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class CampaignUI
    {
        public enum Tab { None, Crew, Store }
        private Tab selectedTab;
        private GUIFrame[] tabs;

        private GUIButton startButton;
        
        private GUIFrame topPanel;

        private GUIListBox characterList;

        private GUIListBox myItemList;
        private GUIListBox storeItemList;

        private GUIComponent missionPanel;
        private GUIComponent selectedLocationInfo;
        private GUIListBox selectedMissionInfo;

        private GUIFrame characterPreviewFrame;

        private List<GUIButton> tabButtons = new List<GUIButton>();
        private List<GUIButton> itemCategoryButtons = new List<GUIButton>();
        private List<GUITickBox> missionTickBoxes = new List<GUITickBox>();

        public Action StartRound;
        public Action<Location, LocationConnection> OnLocationSelected;

        public Level SelectedLevel { get; private set; }

        public GUIComponent MapContainer { get; private set; }

        public CampaignMode Campaign { get; }

        public CampaignUI(CampaignMode campaign, GUIFrame container)
        {
            this.Campaign = campaign;

            MapContainer = new GUICustomComponent(new RectTransform(Vector2.One, container.RectTransform), DrawMap, UpdateMap);
            new GUIFrame(new RectTransform(Vector2.One, MapContainer.RectTransform), style: "InnerGlow", color: Color.Black * 0.9f)
            {
                CanBeFocused = false
            };

            // top panel -------------------------------------------------------------------------

            topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), container.RectTransform, Anchor.TopCenter), style: null)
            {
                CanBeFocused = false
            };
            var topPanelContent = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), topPanel.RectTransform, Anchor.BottomCenter), style: null)
            {
                CanBeFocused = false
            };

            var outpostBtn = new GUIButton(new RectTransform(new Vector2(0.15f, 0.55f), topPanelContent.RectTransform), 
                TextManager.Get("Outpost"), textAlignment: Alignment.Center, style: "GUISlopedHeader")
            {
             OnClicked = (btn, userdata) => { SelectTab(Tab.None); return true; }   
            };
            outpostBtn.TextBlock.Font = GUI.LargeFont;
            outpostBtn.TextBlock.AutoScale = true;

            var tabButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 0.3f), topPanelContent.RectTransform, Anchor.BottomLeft), isHorizontal: true);

            int i = 0;
            var tabValues = Enum.GetValues(typeof(Tab));
            foreach (Tab tab in tabValues)
            {
                var tabButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), tabButtonContainer.RectTransform),
                    TextManager.Get(tab.ToString()),
                    textAlignment: Alignment.Center,
                    style: i == 0 ? "GUISlopedTabButtonLeft" : (i == tabValues.Length - 1 ? "GUISlopedTabButtonRight" : "GUISlopedTabButtonMid"))
                {
                    UserData = tab,
                    OnClicked = (btn, userdata) => { SelectTab((Tab)userdata); return true; },
                    Selected = tab == Tab.None
                };
                var buttonSprite = tabButton.Style.Sprites[GUIComponent.ComponentState.None][0];
                tabButton.RectTransform.MaxSize = new Point(
                    (int)(tabButton.Rect.Height * (buttonSprite.Sprite.size.X / buttonSprite.Sprite.size.Y)), int.MaxValue);
                tabButtons.Add(tabButton);
                tabButton.Font = GUI.LargeFont;
                i++;
            }

            // crew tab -------------------------------------------------------------------------

            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabs[(int)Tab.Crew] = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.7f), container.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0.0f, topPanel.RectTransform.RelativeSize.Y)
            }, color: Color.Black * 0.7f);
            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), tabs[(int)Tab.Crew].RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                CanBeFocused = false
            };

            characterList = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.95f), tabs[(int)Tab.Crew].RectTransform, Anchor.Center))
            {
                OnSelected = SelectCharacter
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), characterList.Content.RectTransform),
                TextManager.Get("CampaignMenuCrew"), font: GUI.LargeFont)
            {
                UserData = "mycrew",
                CanBeFocused = false,
                AutoScale = true
            };
            if (campaign is SinglePlayerCampaign)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), characterList.Content.RectTransform),
                    TextManager.Get("CampaignMenuHireable"), font: GUI.LargeFont)
                {
                    UserData = "hire",
                    CanBeFocused = false,
                    AutoScale = true
                };
            }
            
            // store tab -------------------------------------------------------------------------
            
            tabs[(int)Tab.Store] = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.7f), container.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0.1f, topPanel.RectTransform.RelativeSize.Y)
            }, color: Color.Black * 0.7f);
            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), tabs[(int)Tab.Store].RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                CanBeFocused = false
            };

            List<MapEntityCategory> itemCategories = Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>().ToList();
            //don't show categories with no buyable items
            itemCategories.RemoveAll(c =>
                !MapEntityPrefab.List.Any(ep => ep.Category.HasFlag(c) && (ep is ItemPrefab) && ((ItemPrefab)ep).CanBeBought));

            var storeContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), tabs[(int)Tab.Store].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), storeContent.RectTransform), "", font: GUI.LargeFont)
            {
                TextGetter = GetMoney
            };

            var storeItemLists = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), storeContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            myItemList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), storeItemLists.RectTransform));
            storeItemList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), storeItemLists.RectTransform))
            {
                OnSelected = BuyItem
            };

            var categoryButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.1f, 0.9f), tabs[(int)Tab.Store].RectTransform, Anchor.CenterLeft, Pivot.CenterRight))
            {
                RelativeSpacing = 0.02f
            };
            foreach (MapEntityCategory category in itemCategories)
            {
                var categoryButton = new GUIButton(new RectTransform(new Point(categoryButtonContainer.Rect.Width), categoryButtonContainer.RectTransform),
                    "", style: "ItemCategory" + category.ToString())
                {
                    UserData = category,
                    OnClicked = (btn, userdata) => { SelectItemCategory((MapEntityCategory)userdata); return true; }
                };
                itemCategoryButtons.Add(categoryButton);

                new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.25f), categoryButton.RectTransform, Anchor.BottomCenter),
                   TextManager.Get("MapEntityCategory." + category), textAlignment: Alignment.Center, textColor: categoryButton.TextColor)
                {
                    AutoScale = true,
                    Color = Color.Transparent,
                    HoverColor = Color.Transparent,
                    PressedColor = Color.Transparent,
                    SelectedColor = Color.Transparent,
                    CanBeFocused = false
                };
            }
            SelectItemCategory(MapEntityCategory.Equipment);

            // mission info -------------------------------------------------------------------------

            missionPanel = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.5f), container.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, topPanel.RectTransform.RelativeSize.Y)
            }, color: Color.Black * 0.7f)
            {
                Visible = false
            };
            
            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), missionPanel.RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                CanBeFocused = false
            };

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.15f), missionPanel.RectTransform, Anchor.TopRight, Pivot.BottomRight)
            { RelativeOffset = new Vector2(0.1f, -0.05f) }, TextManager.Get("Mission"),
                textAlignment: Alignment.Center, font: GUI.LargeFont, style: "GUISlopedHeader")
            {
                AutoScale = true
            };
            var missionPanelContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), missionPanel.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            selectedLocationInfo = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.75f), missionPanelContent.RectTransform))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };
            selectedMissionInfo = new GUIListBox(new RectTransform(new Vector2(0.9f, 0.25f), missionPanel.RectTransform, Anchor.BottomRight, Pivot.TopRight))
            {
                Visible = false
            };

            // -------------------------------------------------------------------------

            topPanel.RectTransform.SetAsLastChild();

            SelectTab(Tab.None);

            UpdateLocationView(campaign.Map.CurrentLocation);

            campaign.Map.OnLocationSelected += SelectLocation;
            campaign.Map.OnLocationChanged += (prevLocation, newLocation) => UpdateLocationView(newLocation);
            campaign.Map.OnMissionSelected += (connection, mission) => 
            {
                var selectedTickBox = missionTickBoxes.Find(tb => tb.UserData == mission);
                if (selectedTickBox != null)
                {
                    selectedTickBox.Selected = true;
                }
            };
            campaign.CargoManager.OnItemsChanged += RefreshMyItems;
        }

        private void UpdateLocationView(Location location)
        {
            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }
            
            if (Campaign is SinglePlayerCampaign)
            {
                var hireableCharacters = location.GetHireableCharacters();
                foreach (GUIComponent child in characterList.Content.Children.ToList())
                {
                    if (child.UserData is CharacterInfo character)
                    {
                        if (GameMain.GameSession.CrewManager.GetCharacterInfos().Contains(character)) { continue; }
                    }
                    else if (child.UserData as string == "mycrew" || child.UserData as string == "hire")
                    {
                        continue;
                    }
                    characterList.RemoveChild(child);
                }
                if (!hireableCharacters.Any())
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), characterList.Content.RectTransform), TextManager.Get("HireUnavailable"), textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
                else
                {
                    foreach (CharacterInfo c in hireableCharacters)
                    {
                        var frame = c.CreateCharacterFrame(characterList.Content, c.Name + " (" + c.Job.Name + ")", c);
                        new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.TopRight), c.Salary.ToString(), textAlignment: Alignment.CenterRight);
                    }
                }
            }
            characterList.UpdateScrollBarSize();

            RefreshMyItems();

            bool purchaseableItemsFound = false;
            foreach (MapEntityPrefab mapEntityPrefab in MapEntityPrefab.List)
            {
                var itemPrefab = mapEntityPrefab as ItemPrefab;
                if (itemPrefab == null) { continue; }

                PriceInfo priceInfo = itemPrefab.GetPrice(Campaign.Map.CurrentLocation);
                if (priceInfo != null) { purchaseableItemsFound = true; break; }
            }

            //disable store tab if there's nothing to buy
            tabButtons.Find(btn => (Tab)btn.UserData == Tab.Store).Enabled = purchaseableItemsFound;

            if (selectedTab == Tab.Store && !purchaseableItemsFound)
            {
                //switch out from store tab if there's nothing to buy
                SelectTab(Tab.None);
            }
            else
            {
                //refresh store view
                SelectItemCategory(MapEntityCategory.Equipment);
            }
            
        }
        
        private void DrawMap(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            GameMain.GameSession?.Map?.Draw(spriteBatch, mapContainer);
        }

        private void UpdateMap(float deltaTime, GUICustomComponent mapContainer)
        {
            GameMain.GameSession?.Map?.Update(deltaTime, mapContainer);
        }
        
        public void UpdateCharacterLists()
        {
            int placeIndex = 1;
            foreach (GUIComponent child in characterList.Content.Children.ToList())
            {
                if (child.UserData as string == "mycrew")
                {
                    continue;
                }
                else if (child.UserData as string == "hire")
                {
                    break;
                }
                placeIndex++;
                characterList.RemoveChild(child);
            }
            foreach (CharacterInfo c in GameMain.GameSession.CrewManager.GetCharacterInfos())
            {
                var frame = c.CreateCharacterFrame(characterList.Content, c.Name + " (" + c.Job.Name + ") ", c);
                frame.RectTransform.RepositionChildInHierarchy(placeIndex);
            }
            characterList.UpdateScrollBarSize();
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            selectedLocationInfo.ClearChildren();
            missionPanel.Visible = location != null;
            
            if (location == null) { return; }
            
            var container = selectedLocationInfo;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform), location.Name, font: GUI.LargeFont)
            {
                AutoScale = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform), location.Type.DisplayName);

            Sprite portrait = location.Type.Background;
            new GUIImage(new RectTransform(new Vector2(1.0f, 0.6f),
                container.RectTransform), portrait, scaleToFit: true);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform), "Select a mission", font: GUI.LargeFont)
            {
                AutoScale = true
            };

            var missionFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.3f), container.RectTransform), style: "InnerFrame");
            var missionContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), missionFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };
            
            SelectedLevel = connection?.Level;
            if (connection != null)
            {
                Point maxTickBoxSize = new Point(int.MaxValue, missionContent.Rect.Height / 4) ;
                List<Mission> availableMissions = connection.AvailableMissions.ToList();
                if (!availableMissions.Contains(null)) { availableMissions.Add(null); }

                Mission selectedMission = connection.SelectedMission != null && connection.AvailableMissions.Contains(connection.SelectedMission) ?
                    connection.SelectedMission : null;
                missionTickBoxes.Clear();
                foreach (Mission mission in availableMissions)
                {
                    var tickBox = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.1f), missionContent.RectTransform) { MaxSize = maxTickBoxSize },
                       mission?.Name ?? "No mission")
                    {
                        UserData = mission,
                        Enabled = GameMain.Client == null || GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign),
                        Selected = mission == selectedMission,
                        OnSelected = (tb) => 
                        {
                            if (!tb.Selected) { return false; }
                            RefreshMissionTab(tb.UserData as Mission); 
                            Campaign.Map.OnMissionSelected?.Invoke(connection, mission);
                            if (GameMain.Client != null && GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
                            {
                                GameMain.Client?.SendCampaignState();
                            }
                            return true;
                        }
                    };
                    missionTickBoxes.Add(tickBox);
                }
                
                RefreshMissionTab(selectedMission);

                startButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.7f), missionContent.RectTransform, Anchor.CenterRight),
                    TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
                {
                    IgnoreLayoutGroups = true,
                    OnClicked = (GUIButton btn, object obj) => { StartRound?.Invoke(); return true; },
                    Enabled = true
                };
            }

            OnLocationSelected?.Invoke(location, connection);
        }


        public void RefreshMissionTab(Mission mission)
        {
            System.Diagnostics.Debug.Assert(
                mission == null ||
                (GameMain.GameSession.Map?.SelectedConnection != null &&
                GameMain.GameSession.Map.SelectedConnection.AvailableMissions.Contains(mission)));
            
            GameMain.GameSession.Map.SelectedConnection.SelectedMission = mission;

            foreach (GUITickBox missionTickBox in missionTickBoxes)
            {
                missionTickBox.Selected = missionTickBox.UserData == mission;
            }

            selectedMissionInfo.ClearChildren();
            var container = selectedMissionInfo.Content;
            selectedMissionInfo.Visible = mission != null;
            selectedMissionInfo.Spacing = 10;
            if (mission == null) { return; }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform),
                mission.Name, font: GUI.LargeFont)
            {
                AutoScale = true,
                CanBeFocused = false
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform),
                TextManager.Get("Reward").Replace("[reward]", mission.Reward.ToString()))
            {
                CanBeFocused = false
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform),
                mission.Description, wrap: true)
            {
                CanBeFocused = false
            };

            if (startButton != null) { startButton.Enabled = true; }
        }

        private void CreateItemFrame(PurchasedItem pi, PriceInfo priceInfo, GUIListBox listBox, int width)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(listBox.Rect.Width, 50), listBox.Content.RectTransform), style: "ListBoxElement")
            {
                UserData = pi,
                ToolTip = pi.ItemPrefab.Description
            };

            ScalableFont font = listBox.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Point(listBox.Rect.Width - 50, 25), frame.RectTransform, Anchor.CenterLeft)
            {
                AbsoluteOffset = new Point(40, 0)
            }, pi.ItemPrefab.Name, font: font)
            {
                ToolTip = pi.ItemPrefab.Description
            };

            Sprite itemIcon = pi.ItemPrefab.InventoryIcon ?? pi.ItemPrefab.sprite;

            if (itemIcon != null)
            {
                GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), frame.RectTransform, Anchor.CenterLeft), itemIcon)
                {
                    Color = itemIcon == pi.ItemPrefab.InventoryIcon ? pi.ItemPrefab.InventoryIconColor : pi.ItemPrefab.SpriteColor
                };
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }

            textBlock = new GUITextBlock(new RectTransform(new Point(120, 25), frame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(20, 0) }, 
                priceInfo.BuyPrice.ToString(), font: font)
            {
                ToolTip = pi.ItemPrefab.Description
            };


            //If its the store menu, quantity will always be 0
            if (pi.Quantity > 0)
            {
                var amountInput = new GUINumberInput(new RectTransform(new Point(50, 40), frame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(20, 0) }, 
                    GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueInt = 1000,
                    UserData = pi,
                    IntValue = pi.Quantity
                };
                amountInput.OnValueChanged += (numberInput) =>
                {
                    PurchasedItem purchasedItem = numberInput.UserData as PurchasedItem;

                    //Attempting to buy
                    if (numberInput.IntValue > purchasedItem.Quantity)
                    {
                        int quantity = numberInput.IntValue - purchasedItem.Quantity;
                        //Cap the numberbox based on the amount we can afford.
                        quantity = Campaign.Money <= 0 ? 
                            0 : Math.Min((int)(Campaign.Money / (float)priceInfo.BuyPrice), quantity);
                        for (int i = 0; i < quantity; i++)
                        {
                            BuyItem(numberInput, purchasedItem);
                        }
                        numberInput.IntValue = purchasedItem.Quantity;
                    }
                    //Attempting to sell
                    else
                    {
                        int quantity = purchasedItem.Quantity - numberInput.IntValue;
                        for (int i = 0; i < quantity; i++)
                        {
                            SellItem(numberInput, purchasedItem);
                        }
                    }
                };
            }
        }

        private bool BuyItem(GUIComponent component, object obj)
        {
            PurchasedItem pi = obj as PurchasedItem;
            if (pi == null || pi.ItemPrefab == null) return false;

            if (GameMain.Client != null && !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
            {
                return false;
            }
            
            PriceInfo priceInfo = pi.ItemPrefab.GetPrice(Campaign.Map.CurrentLocation);
            if (priceInfo == null || priceInfo.BuyPrice > Campaign.Money) return false;
            
            Campaign.CargoManager.PurchaseItem(pi.ItemPrefab, 1);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private bool SellItem(GUIComponent component, object obj)
        {
            PurchasedItem pi = obj as PurchasedItem;
            if (pi == null || pi.ItemPrefab == null) return false;

            if (GameMain.Client != null && !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
            {
                return false;
            }
            
            Campaign.CargoManager.SellItem(pi.ItemPrefab,1);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private void RefreshMyItems()
        {
            myItemList.Content.ClearChildren();

            foreach (PurchasedItem ip in Campaign.CargoManager.PurchasedItems)
            {
                CreateItemFrame(ip, ip.ItemPrefab.GetPrice(Campaign.Map.CurrentLocation), myItemList, myItemList.Rect.Width);
            }
            myItemList.Content.RectTransform.SortChildren((x, y) =>
                (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name));
            myItemList.Content.RectTransform.SortChildren((x, y) =>
                (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category));
            myItemList.UpdateScrollBarSize();
        }
        
        public void SelectTab(Tab tab)
        {
            selectedTab = tab;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null)
                {
                    tabs[i].Visible = (int)selectedTab == i;
                }
            }
            foreach (GUIButton button in tabButtons)
            {
                button.Selected = (Tab)button.UserData == tab;
            }
        }

        private bool SelectItemCategory(MapEntityCategory category)
        {
            storeItemList.ClearChildren();

            int width = storeItemList.Rect.Width;
            foreach (MapEntityPrefab mapEntityPrefab in MapEntityPrefab.List)
            {
                var itemPrefab = mapEntityPrefab as ItemPrefab;
                if (itemPrefab == null || !itemPrefab.Category.HasFlag(category)) continue;

                PriceInfo priceInfo = itemPrefab.GetPrice(Campaign.Map.CurrentLocation);
                if (priceInfo == null) continue;

                CreateItemFrame(new PurchasedItem(itemPrefab, 0), priceInfo, storeItemList, width);
            }

            storeItemList.Content.RectTransform.SortChildren(
                (x, y) => (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name));

            foreach (GUIButton btn in itemCategoryButtons)
            {
                btn.Selected = (MapEntityCategory)btn.UserData == category;
            }

            storeItemList.BarScroll = 0.0f;

            return true;
        }

        public string GetMoney()
        {
            return TextManager.Get("PlayerCredits").Replace("[credits]",
                ((GameMain.GameSession == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", Campaign.Money)));
        }

        private bool SelectCharacter(GUIComponent component, object selection)
        {
            GUIComponent prevInfoFrame = null;
            foreach (GUIComponent child in tabs[(int)selectedTab].Children)
            {
                if (!(child.UserData is CharacterInfo)) { continue; }

                prevInfoFrame = child;
            }

            if (prevInfoFrame != null) { tabs[(int)selectedTab].RemoveChild(prevInfoFrame); }
            
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) { return false; }
            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) { return false; }

            if (characterPreviewFrame == null || characterPreviewFrame.UserData != characterInfo)
            {
                characterPreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.5f), tabs[(int)selectedTab].RectTransform, Anchor.TopRight, Pivot.TopLeft))
                {
                    UserData = characterInfo
                };

                characterInfo.CreateInfoFrame(characterPreviewFrame);
            }

            if (GameMain.GameSession.CrewManager.GetCharacterInfos().Contains(characterInfo))
            {
                new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), characterPreviewFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) }, 
                    TextManager.Get("FireButton"))
                {
                    Color = Color.Red,
                    UserData = characterInfo,
                    OnClicked = (btn, obj) =>
                    {
                        var confirmDialog = new GUIMessageBox(
                            TextManager.Get("FireWarningHeader"),
                            TextManager.Get("FireWarningText").Replace("[charactername]", ((CharacterInfo)obj).Name),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                        confirmDialog.Buttons[0].UserData = (CharacterInfo)obj;
                        confirmDialog.Buttons[0].OnClicked = FireCharacter;
                        confirmDialog.Buttons[0].OnClicked += confirmDialog.Close;
                        confirmDialog.Buttons[1].OnClicked = confirmDialog.Close;
                        return true;
                    }
                };
            }
            else
            {
                new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), characterPreviewFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) },
                    TextManager.Get("HireButton"))
                {
                    Enabled = Campaign.Money >= characterInfo.Salary,
                    UserData = characterInfo,
                    OnClicked = HireCharacter
                };
            }

            return true;
        }

        private bool HireCharacter(GUIButton button, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) { return false; }

            SinglePlayerCampaign spCampaign = Campaign as SinglePlayerCampaign;
            if (spCampaign == null)
            {
                DebugConsole.ThrowError("Characters can only be hired in the single player campaign.\n" + Environment.StackTrace);
                return false;
            }

            if (spCampaign.TryHireCharacter(GameMain.GameSession.Map.CurrentLocation, characterInfo))
            {
                UpdateLocationView(GameMain.GameSession.Map.CurrentLocation);
                SelectCharacter(null, null);
                UpdateCharacterLists();
            }

            return false;
        }

        private bool FireCharacter(GUIButton button, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            SinglePlayerCampaign spCampaign = Campaign as SinglePlayerCampaign;
            if (spCampaign == null)
            {
                DebugConsole.ThrowError("Characters can only be fired in the single player campaign.\n" + Environment.StackTrace);
                return false;
            }

            spCampaign.FireCharacter(characterInfo);
            SelectCharacter(null, null);
            UpdateCharacterLists();

            return false;
        }

    }
}
