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
        public enum Tab { Crew = 0, Map = 1, Store = 2 }

        private GUIFrame[] tabs;

        private GUIButton startButton;

        private Tab selectedTab;

        private GUIListBox characterList, hireList;

        private GUIListBox selectedItemList;
        private GUIListBox storeItemList;

        private CampaignMode campaign;

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

            tabs = new GUIFrame[3];

            tabs[(int)Tab.Crew] = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), container.RectTransform, Anchor.Center, Pivot.Center), null);
            tabs[(int)Tab.Crew].Padding = Vector4.One * 10.0f;

            //new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, "", bottomPanel[(int)PanelTab.Crew]);

            int crewColumnWidth = Math.Min(300, (container.Rect.Width - 40) / 2);

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.05f), tabs[(int)Tab.Crew].RectTransform)
            {
                RelativeOffset = new Vector2(0.01f, 0.02f)
            }, TextManager.Get("Crew") + ":", style: "");
            characterList = new GUIListBox(new RectTransform(new Vector2(0.3f, 0.95f), tabs[(int)Tab.Crew].RectTransform, Anchor.CenterLeft, Pivot.CenterLeft)
            {
                RelativeOffset = new Vector2(0.01f, 0.05f)
            }, false, null, "");
            characterList.OnSelected = SelectCharacter;

            new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.05f), tabs[(int)Tab.Crew].RectTransform, Anchor.TopRight, Pivot.TopRight)
            {
                RelativeOffset = new Vector2(0.01f, 0.02f)
            }, TextManager.Get("Hire") + ":", style: "");
            hireList = new GUIListBox(new RectTransform(new Vector2(0.3f, 0.95f), tabs[(int)Tab.Crew].RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.01f, 0.05f)
            }, false, null, "");
            hireList.OnSelected = SelectCharacter;

            //---------------------------------------

            tabs[(int)Tab.Map] = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), container.RectTransform, Anchor.Center, Pivot.Center), null);
            tabs[(int)Tab.Map].Padding = Vector4.One * 10.0f;

            if (GameMain.Client == null)
            {
                startButton = new GUIButton(new RectTransform(new Vector2(0.07f, 0.04f), tabs[(int)Tab.Map].RectTransform, Anchor.BottomRight, Pivot.BottomRight)
                {
                    RelativeOffset = new Vector2(0.01f, 0.03f)
                }, TextManager.Get("StartCampaignButton"));
                startButton.OnClicked = (GUIButton btn, object obj) => { StartRound?.Invoke(); return true; };
                startButton.Enabled = false;
            }

            //---------------------------------------

            tabs[(int)Tab.Store] = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), container.RectTransform, Anchor.Center, Pivot.Center), null);
            tabs[(int)Tab.Store].Padding = Vector4.One * 10.0f;

            int sellColumnWidth = (tabs[(int)Tab.Store].Rect.Width - 40) / 2 - 20;

            selectedItemList = new GUIListBox(new RectTransform(new Vector2(0.45f, 0.95f), tabs[(int)Tab.Store].RectTransform, Anchor.CenterLeft, Pivot.CenterLeft)
            {
                RelativeOffset = new Vector2(0.01f, 0.0f)
            }, false, null, "");
            selectedItemList.OnSelected = SellItem;
            
            storeItemList = new GUIListBox(new RectTransform(new Vector2(0.45f, 0.95f), tabs[(int)Tab.Store].RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.01f, 0.0f)
            }, false, null, "");
            storeItemList.OnSelected = BuyItem;


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
                }, category.ToString());
                categoryButton.UserData = category;
                categoryButton.OnClicked = SelectItemCategory;

                if (category == MapEntityCategory.Equipment)
                {
                    SelectItemCategory(categoryButton, category);
                }
                x += buttonWidth;
            }

            SelectTab(Tab.Map);

            UpdateLocationTab(campaign.Map.CurrentLocation);

            campaign.Map.OnLocationSelected += SelectLocation;
            campaign.Map.OnLocationChanged += (prevLocation, newLocation) => UpdateLocationTab(newLocation);
            campaign.CargoManager.OnItemsChanged += RefreshItemTab;
        }

        private void UpdateLocationTab(Location location)
        {
            if (characterPreviewFrame != null)
            {
                characterPreviewFrame.Parent.RemoveChild(characterPreviewFrame);
                characterPreviewFrame = null;
            }

            if (location.HireManager == null)
            {
                hireList.ClearChildren();
                hireList.Enabled = false;

                new GUITextBlock(new Rectangle(0, 0, 0, 0), TextManager.Get("HireUnavailable"), Color.Transparent, Color.LightGray, Alignment.Center, Alignment.Center, "", hireList);
                return;
            }

            hireList.Enabled = true;
            hireList.ClearChildren();

            foreach (CharacterInfo c in location.HireManager.availableCharacters)
            {
                var frame = c.CreateCharacterFrame(hireList, c.Name + " (" + c.Job.Name + ")", c);

                new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.Salary.ToString(),
                    null, null,
                    Alignment.TopRight, "", frame);
            }
        }

        public void Update(float deltaTime)
        {            
            if (GameMain.GameSession?.Map != null)
            {
                GameMain.GameSession.Map.Update(deltaTime, new Rectangle(
                    tabs[(int)selectedTab].Rect.X + 20,
                    tabs[(int)selectedTab].Rect.Y + 20,
                    tabs[(int)selectedTab].Rect.Width - 310,
                    tabs[(int)selectedTab].Rect.Height - 40));
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (selectedTab == Tab.Map && GameMain.GameSession?.Map != null)
            {
                GameMain.GameSession.Map.Draw(spriteBatch, new Rectangle(
                    tabs[(int)selectedTab].Rect.X + 20, 
                    tabs[(int)selectedTab].Rect.Y + 20,
                    tabs[(int)selectedTab].Rect.Width - 310, 
                    tabs[(int)selectedTab].Rect.Height - 40));
            }
        }

        public void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in GameMain.GameSession.CrewManager.GetCharacterInfos())
            {
                c.CreateCharacterFrame(characterList, c.Name + " (" + c.Job.Name + ") ", c);
            }
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            GUIComponent locationPanel = tabs[(int)Tab.Map].GetChild("selectedlocation");

            if (locationPanel != null) tabs[(int)Tab.Map].RemoveChild(locationPanel);

            locationPanel = new GUIFrame(new Rectangle(0, 0, 250, 190), Color.Transparent, Alignment.TopRight, null, tabs[(int)Tab.Map]);
            locationPanel.UserData = "selectedlocation";

            if (location == null) return;

            var titleText = new GUITextBlock(new Rectangle(0, 0, 250, 0), location.Name, "", Alignment.TopLeft, Alignment.TopCenter, locationPanel, true, GUI.LargeFont);

            if (GameMain.GameSession.Map.SelectedConnection != null && GameMain.GameSession.Map.SelectedConnection.Mission != null)
            {
                var mission = GameMain.GameSession.Map.SelectedConnection.Mission;

                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 20, 0, 20), TextManager.Get("Mission") + ": " + mission.Name, "", locationPanel);
                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 40, 0, 20), TextManager.Get("Reward") + ": " + mission.Reward + " " + TextManager.Get("Credits"), "", locationPanel);
                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 70, 0, 0), mission.Description, "", Alignment.TopLeft, Alignment.TopLeft, locationPanel, true, GUI.SmallFont);
            }

            if (startButton != null) startButton.Enabled = true;

            selectedLevel = connection.Level;

            OnLocationSelected?.Invoke(location, connection);
        }

        private void CreateItemFrame(ItemPrefab ip, PriceInfo priceInfo, GUIListBox listBox, int width)
        {
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), "ListBoxElement", listBox);
            frame.UserData = ip;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            frame.ToolTip = ip.Description;

            ScalableFont font = listBox.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(50, 0, 0, 25),
                ip.Name,
                null, null,
                Alignment.Left, Alignment.CenterX | Alignment.Left,
                "", frame);
            textBlock.Font = font;
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);
            textBlock.ToolTip = ip.Description;

            if (ip.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ip.sprite, Alignment.CenterLeft, frame);
                img.Color = ip.SpriteColor;
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }

            textBlock = new GUITextBlock(
                new Rectangle(width - 80, 0, 80, 25),
                priceInfo.BuyPrice.ToString(),
                null, null, Alignment.TopLeft,
                Alignment.TopLeft, "", frame);
            textBlock.Font = font;
            textBlock.ToolTip = ip.Description;
        }

        private bool BuyItem(GUIComponent component, object obj)
        {
            ItemPrefab prefab = obj as ItemPrefab;
            if (prefab == null) return false;

            if (GameMain.Client != null && !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
            {
                return false;
            }

            PriceInfo priceInfo = prefab.GetPrice(campaign.Map.CurrentLocation);
            if (priceInfo.BuyPrice > campaign.Money) return false;
            
            campaign.CargoManager.PurchaseItem(prefab);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private bool SellItem(GUIComponent component, object obj)
        {
            ItemPrefab prefab = obj as ItemPrefab;
            if (prefab == null) return false;

            if (GameMain.Client != null && !GameMain.Client.HasPermission(Networking.ClientPermissions.ManageCampaign))
            {
                return false;
            }
            
            campaign.CargoManager.SellItem(prefab);
            GameMain.Client?.SendCampaignState();

            return false;
        }

        private void RefreshItemTab()
        {
            selectedItemList.ClearChildren();
            foreach (ItemPrefab ip in campaign.CargoManager.PurchasedItems)
            {
                CreateItemFrame(ip, ip.GetPrice(campaign.Map.CurrentLocation), selectedItemList, selectedItemList.Rect.Width);
            }
        }
        
        public void SelectTab(Tab tab)
        {
            selectedTab = tab;
            for (int i = 0; i< tabs.Length; i++)
            {
                tabs[i].Visible = (int)selectedTab == i;
            }
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

                CreateItemFrame(itemPrefab, priceInfo, storeItemList, width);
            }

            storeItemList.Children.Sort((x, y) => (x.UserData as MapEntityPrefab).Name.CompareTo((y.UserData as MapEntityPrefab).Name));

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
            return TextManager.Get("Credits") + ": " + ((GameMain.GameSession == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", campaign.Money));
        }

        private bool SelectCharacter(GUIComponent component, object selection)
        {
            GUIComponent prevInfoFrame = null;
            foreach (GUIComponent child in tabs[(int)selectedTab].Children)
            {
                if (!(child.UserData is CharacterInfo)) continue;

                prevInfoFrame = child;
            }

            if (prevInfoFrame != null) tabs[(int)selectedTab].RemoveChild(prevInfoFrame);

            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            characterList.Deselect();
            hireList.Deselect();

            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) return false;

            if (characterPreviewFrame == null || characterPreviewFrame.UserData != characterInfo)
            {
                int width = Math.Min(300, tabs[(int)Tab.Crew].Rect.Width - hireList.Rect.Width - characterList.Rect.Width - 50);

                characterPreviewFrame = new GUIFrame(new Rectangle(0, 60, width, 300),
                        new Color(0.0f, 0.0f, 0.0f, 0.8f),
                        Alignment.TopCenter, "", tabs[(int)selectedTab]);
                characterPreviewFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
                characterPreviewFrame.UserData = characterInfo;

                characterInfo.CreateInfoFrame(characterPreviewFrame);
            }

            if (component.Parent == hireList)
            {
                GUIButton hireButton = new GUIButton(new Rectangle(0, 0, 100, 20), TextManager.Get("HireButton"), Alignment.BottomCenter, "", characterPreviewFrame);
                hireButton.Enabled = campaign.Money >= characterInfo.Salary;
                hireButton.UserData = characterInfo;
                hireButton.OnClicked = HireCharacter;
            }

            return true;
        }

        private bool HireCharacter(GUIButton button, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            SinglePlayerCampaign spCampaign = campaign as SinglePlayerCampaign;
            if (spCampaign == null)
            {
                DebugConsole.ThrowError("Characters can only be hired in the single player campaign.\n" + Environment.StackTrace);
                return false;
            }

            if (spCampaign.TryHireCharacter(GameMain.GameSession.Map.CurrentLocation.HireManager, characterInfo))
            {
                UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);
                SelectCharacter(null, null);
                UpdateCharacterLists();
            }

            return false;
        }


    }
}
