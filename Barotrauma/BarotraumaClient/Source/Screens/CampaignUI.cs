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

        private CampaignMode campaign;

        private GUIFrame[] tabs;

        private GUIButton startButton;
        
        private GUIFrame topPanel, tabContainer;
        private GUITextBlock locationTitle;

        private GUIListBox characterList;

        private GUIListBox selectedItemList;
        private GUIListBox storeItemList;

        private GUIComponent missionPanel;
        private GUIListBox selectedMissionInfo;
        private GUIListBox selectedLocationInfo;

        private GUIFrame characterPreviewFrame;
        
        private Level selectedLevel;
        
        public Action StartRound;
        public Action<Location, LocationConnection> OnLocationSelected;

        public Level SelectedLevel
        {
            get { return selectedLevel; }
        }

        public CampaignMode Campaign
        {
            get { return campaign; }
        }
        
        public CampaignUI(CampaignMode campaign, GUIFrame container)
        {
            this.campaign = campaign;

            new GUICustomComponent(new RectTransform(Vector2.One, container.RectTransform), DrawMap, UpdateMap);

            // top panel -------------------------------------------------------------------------

            topPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), container.RectTransform, Anchor.TopCenter));
            var topPanelContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.8f), topPanel.RectTransform, Anchor.Center), 
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                new GUIButton(new RectTransform(new Vector2(0.15f, 0.5f), topPanelContent.RectTransform),
                    TextManager.Get(tab.ToString()))
                {
                    UserData = tab,
                    OnClicked = SelectTab
                };
            }

            // bottom panel -------------------------------------------------------------------------

            tabContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.8f), container.RectTransform, Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0.0f, topPanel.RectTransform.RelativeSize.Y + 0.02f)
            }, color: Color.Black * 0.7f);
            
            // crew tab -------------------------------------------------------------------------
            
            tabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length];
            tabs[(int)Tab.Crew] = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), tabContainer.RectTransform, Anchor.Center), null);
            
            int crewColumnWidth = Math.Min(300, (tabContainer.Rect.Width - 40) / 2);

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
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), characterList.Content.RectTransform),
                TextManager.Get("CampaignMenuHireable"), font: GUI.LargeFont)
            {
                UserData = "hire",
                CanBeFocused = false,
                AutoScale = true
            };
            
            // store tab -------------------------------------------------------------------------
            
            tabs[(int)Tab.Store] = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), tabContainer.RectTransform, Anchor.Center, Pivot.Center), null);

            int sellColumnWidth = (tabs[(int)Tab.Store].Rect.Width - 40) / 2 - 20;

            selectedItemList = new GUIListBox(new RectTransform(new Vector2(0.45f, 0.95f), tabs[(int)Tab.Store].RectTransform, Anchor.CenterLeft, Pivot.CenterLeft)
            {
                RelativeOffset = new Vector2(0.01f, 0.0f)
            }, false, null, "");

            storeItemList = new GUIListBox(new RectTransform(new Vector2(0.45f, 0.95f), tabs[(int)Tab.Store].RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.01f, 0.0f)
            }, false, null, "")
            {
                OnSelected = BuyItem
            };

            List<MapEntityCategory> itemCategories = Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>().ToList();
            
            //don't show categories with no buyable items
            itemCategories.RemoveAll(c => 
                !MapEntityPrefab.List.Any(ep => ep.Category.HasFlag(c) && (ep is ItemPrefab) && ((ItemPrefab)ep).CanBeBought));

            int x = 0;
            int buttonWidth = storeItemList.Rect.Width / itemCategories.Count;
            foreach (MapEntityCategory category in itemCategories)
            {
                var categoryButton = new GUIButton(new RectTransform(new Point(buttonWidth, 30), tabs[(int)Tab.Store].RectTransform, Anchor.CenterRight, Pivot.BottomRight)
                {
                    AbsoluteOffset = new Point(x, -storeItemList.Rect.Height / 2)
                }, category.ToString())
                {
                    UserData = category,
                    OnClicked = SelectItemCategory
                };

                if (category == MapEntityCategory.Equipment)
                {
                    SelectItemCategory(categoryButton, category);
                }
                x += buttonWidth;
            }

            // mission info -------------------------------------------------------------------------

            missionPanel = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.5f), container.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, topPanel.RectTransform.RelativeSize.Y + 0.02f)
            }, color: Color.Black * 0.7f);
            var missionPanelContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), missionPanel.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            selectedLocationInfo = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.75f), missionPanelContent.RectTransform));
            selectedMissionInfo = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.25f), missionPanelContent.RectTransform));
            
            var mapButtonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), missionPanelContent.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterRight);

            if (GameMain.Client == null)
            {
                startButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), mapButtonArea.RectTransform),
                    TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
                {
                    OnClicked = (GUIButton btn, object obj) => { StartRound?.Invoke(); return true; },
                    Enabled = false
                };
            }

            // -------------------------------------------------------------------------
            
            SelectTab(Tab.None);

            UpdateLocationView(campaign.Map.CurrentLocation);

            campaign.Map.OnLocationSelected += SelectLocation;
            campaign.Map.OnLocationChanged += (prevLocation, newLocation) => UpdateLocationView(newLocation);
            campaign.CargoManager.OnItemsChanged += RefreshItemTab;
        }

        private void UpdateLocationView(Location location)
        {
            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }

            var hireableCharacters = location.GetHireableCharacters();
            foreach (GUIComponent child in characterList.Children.ToList())
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
                new GUITextBlock(new RectTransform(Vector2.One, characterList.Content.RectTransform), TextManager.Get("HireUnavailable"), textAlignment: Alignment.Center);
            }
            else
            {
                foreach (CharacterInfo c in hireableCharacters)
                {
                    var frame = c.CreateCharacterFrame(characterList.Content, c.Name + " (" + c.Job.Name + ")", c);
                    new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.TopRight), c.Salary.ToString(), textAlignment: Alignment.CenterRight);
                }
            }
            characterList.UpdateScrollBarSize();

            RefreshItemTab();
        }
        
        private void DrawMap(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            GameMain.GameSession?.Map?.Draw(spriteBatch, mapContainer);
        }

        private void UpdateMap(float deltaTime, GUICustomComponent mapContainer)
        {
            GameMain.GameSession?.Map?.Update(deltaTime, mapContainer);
        }
        
        public void RefreshLocationTexts()
        {
            if (locationTitle != null)
            {
                locationTitle.Text = TextManager.Get("Location") + ": " + campaign.Map.CurrentLocation.Name;
            }
        }

        public void UpdateCharacterLists()
        {
            foreach (GUIComponent child in characterList.Children.ToList())
            {
                if (child.UserData as string == "mycrew")
                {
                    continue;
                }
                else if (child.UserData as string == "hire")
                {
                    break;
                }
                characterList.RemoveChild(child);
            }
            foreach (CharacterInfo c in GameMain.GameSession.CrewManager.GetCharacterInfos())
            {
                c.CreateCharacterFrame(characterList.Content, c.Name + " (" + c.Job.Name + ") ", c);
            }
            characterList.UpdateScrollBarSize();
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            selectedLocationInfo.ClearChildren();
            
            if (location == null) return;

            var container = selectedLocationInfo.Content;

            var titleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), container.RectTransform), location.Name, font: GUI.LargeFont)
            {
                AutoScale = true
            };

            Sprite portrait = location.Type.Background;
            new GUIImage(new RectTransform(
                new Point(container.Rect.Width, (int)(portrait.size.Y * (container.Rect.Width / portrait.size.X))),
                container.RectTransform), portrait, scaleToFit: true);

            if (GameMain.GameSession.Map.SelectedConnection != null && GameMain.GameSession.Map.SelectedConnection.Mission != null)
            {
                var mission = GameMain.GameSession.Map.SelectedConnection.Mission;

                new GUITextBlock(
                    new RectTransform(new Vector2(1.0f, 0.1f), container.RectTransform),
                    TextManager.Get("Mission") + ": " + mission.Name);
                new GUITextBlock(
                    new RectTransform(new Vector2(1.0f, 0.1f), container.RectTransform),
                    TextManager.Get("Reward").Replace("[reward]", mission.Reward.ToString()));
                new GUITextBlock(
                    new RectTransform(new Vector2(1.0f, 0.0f), container.RectTransform),
                    mission.Description, wrap: true);
            }

            if (startButton != null) startButton.Enabled = true;

            selectedLevel = connection?.Level;

            OnLocationSelected?.Invoke(location, connection);
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

            if (pi.ItemPrefab.sprite != null)
            {
                GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), frame.RectTransform, Anchor.CenterLeft), pi.ItemPrefab.sprite)
                {
                    Color = pi.ItemPrefab.SpriteColor
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
                        quantity = campaign.Money <= 0 ? 
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
            
            PriceInfo priceInfo = pi.ItemPrefab.GetPrice(campaign.Map.CurrentLocation);
            if (priceInfo == null || priceInfo.BuyPrice > campaign.Money) return false;
            
            campaign.CargoManager.PurchaseItem(pi.ItemPrefab, 1);
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
            
            campaign.CargoManager.SellItem(pi.ItemPrefab,1);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private void RefreshItemTab()
        {
            selectedItemList.Content.ClearChildren();
            foreach (PurchasedItem ip in campaign.CargoManager.PurchasedItems)
            {
                CreateItemFrame(ip, ip.ItemPrefab.GetPrice(campaign.Map.CurrentLocation), selectedItemList, selectedItemList.Rect.Width);
            }
            selectedItemList.Content.RectTransform.SortChildren((x, y) => 
                (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name));
            selectedItemList.Content.RectTransform.SortChildren((x, y) => 
                (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Category));
            selectedItemList.UpdateScrollBarSize();
        }


        public bool SelectTab(GUIButton button, object selection)
        {
            if (button != null)
            {
                button.Selected = true;
                foreach (GUIComponent child in topPanel.Children)
                {
                    GUIButton otherButton = child as GUIButton;
                    if (otherButton == null || otherButton == button) continue;
                    otherButton.Selected = false;
                }
            }
            SelectTab((Tab)selection);
            return true;
        }

        public void SelectTab(Tab tab)
        {
            selectedTab = tab;
            for (int i = 0; i< tabs.Length; i++)
            {
                if (tabs[i] != null)
                {
                    tabs[i].Visible = (int)selectedTab == i;            
                }
            }
            tabContainer.Visible = tab != Tab.None;
        }

        private bool SelectItemCategory(GUIButton button, object selection)
        {
            if (!(selection is MapEntityCategory)) return false;

            storeItemList.ClearChildren();
            storeItemList.BarScroll = 0.0f;
                        
            MapEntityCategory category = (MapEntityCategory)selection;
            int width = storeItemList.Rect.Width;
            foreach (MapEntityPrefab mapEntityPrefab in MapEntityPrefab.List)
            {
                var itemPrefab = mapEntityPrefab as ItemPrefab;
                if (itemPrefab == null || !itemPrefab.Category.HasFlag(category)) continue;

                PriceInfo priceInfo = itemPrefab.GetPrice(campaign.Map.CurrentLocation);
                if (priceInfo == null) continue;

                CreateItemFrame(new PurchasedItem(itemPrefab, 0), priceInfo, storeItemList, width);
            }

            storeItemList.Content.RectTransform.SortChildren(
                (x, y) => (x.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name.CompareTo((y.GUIComponent.UserData as PurchasedItem).ItemPrefab.Name));

            foreach (GUIComponent child in button.Parent.Children)
            {
                var otherButton = child as GUIButton;
                if (child.UserData is MapEntityCategory && otherButton != button)
                {
                    otherButton.Selected = false;
                }
            }

            button.Selected = true;
            return true;
        }

        public string GetMoney()
        {
            return TextManager.Get("PlayerCredits").Replace("[credits]",
                ((GameMain.GameSession == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", campaign.Money)));
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
                    Enabled = campaign.Money >= characterInfo.Salary,
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

            SinglePlayerCampaign spCampaign = campaign as SinglePlayerCampaign;
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

            SinglePlayerCampaign spCampaign = campaign as SinglePlayerCampaign;
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
