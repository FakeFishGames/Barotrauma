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

        private GUIFrame previewFrame;

        private GUIButton buyButton;

        private Level selectedLevel;

        private float mapZoom = 3.0f;

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

        private string CostTextGetter()
        {
            return "Cost: " + selectedItemCost.ToString() + " credits";
        }

        private int selectedItemCost
        {
            get
            {
                int cost = 0;
                foreach (GUIComponent child in selectedItemList.children)
                {
                    MapEntityPrefab ep = child.UserData as MapEntityPrefab;
                    if (ep == null) continue;
                    cost += ep.Price;
                }
                return cost;
            }
        }

        public CampaignUI(CampaignMode campaign, GUIFrame container)
        {
            this.campaign = campaign;

            tabs = new GUIFrame[3];

            tabs[(int)Tab.Crew] = new GUIFrame(Rectangle.Empty, null, container);
            tabs[(int)Tab.Crew].Padding = Vector4.One * 10.0f;

            //new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, "", bottomPanel[(int)PanelTab.Crew]);

            int crewColumnWidth = Math.Min(300, (container.Rect.Width - 40) / 2);

            new GUITextBlock(new Rectangle(0, 0, 100, 20), "Crew:", "", tabs[(int)Tab.Crew], GUI.LargeFont);
            characterList = new GUIListBox(new Rectangle(0, 40, crewColumnWidth, 0), "", tabs[(int)Tab.Crew]);
            characterList.OnSelected = SelectCharacter;

            hireList = new GUIListBox(new Rectangle(0, 40, 300, 0), "", Alignment.Right, tabs[(int)Tab.Crew]);
            new GUITextBlock(new Rectangle(0, 0, 300, 20), "Hire:", "", Alignment.Right, Alignment.Left, tabs[(int)Tab.Crew], false, GUI.LargeFont);
            hireList.OnSelected = SelectCharacter;

            //---------------------------------------

            tabs[(int)Tab.Map] = new GUIFrame(Rectangle.Empty, null, container);
            tabs[(int)Tab.Map].Padding = Vector4.One * 10.0f;

            if (GameMain.Client == null)
            {
                startButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",
                    Alignment.BottomRight, "", tabs[(int)Tab.Map]);
                startButton.OnClicked = (GUIButton btn, object obj) => { StartRound?.Invoke(); return true; };
                startButton.Enabled = false;
            }

            //---------------------------------------

            tabs[(int)Tab.Store] = new GUIFrame(Rectangle.Empty, null, container);
            tabs[(int)Tab.Store].Padding = Vector4.One * 10.0f;

            int sellColumnWidth = (tabs[(int)Tab.Store].Rect.Width - 40) / 2 - 20;

            selectedItemList = new GUIListBox(new Rectangle(0, 30, sellColumnWidth, tabs[(int)Tab.Store].Rect.Height - 80), Color.White * 0.7f, "", tabs[(int)Tab.Store]);
            selectedItemList.OnSelected = DeselectItem;

            var costText = new GUITextBlock(new Rectangle(0, 0, 100, 25), "Cost: ", "", Alignment.BottomLeft, Alignment.TopLeft, tabs[(int)Tab.Store]);
            costText.TextGetter = CostTextGetter;

            buyButton = new GUIButton(new Rectangle(selectedItemList.Rect.Width - 100, 0, 100, 25), "Buy", Alignment.Bottom, "", tabs[(int)Tab.Store]);
            buyButton.OnClicked = BuyItems;

            storeItemList = new GUIListBox(new Rectangle(0, 30, sellColumnWidth, tabs[(int)Tab.Store].Rect.Height - 80), Color.White * 0.7f, Alignment.TopRight, "", tabs[(int)Tab.Store]);
            storeItemList.OnSelected = SelectItem;

            int x = storeItemList.Rect.X - storeItemList.Parent.Rect.X;

            List<MapEntityCategory> itemCategories = Enum.GetValues(typeof(MapEntityCategory)).Cast<MapEntityCategory>().ToList();
            //don't show categories with no buyable items
            itemCategories.RemoveAll(c => !MapEntityPrefab.list.Any(ep => ep.Price > 0.0f && ep.Category.HasFlag(c)));

            int buttonWidth = Math.Min(sellColumnWidth / itemCategories.Count, 100);
            foreach (MapEntityCategory category in itemCategories)
            {
                var categoryButton = new GUIButton(new Rectangle(x, 0, buttonWidth, 20), category.ToString(), "", tabs[(int)Tab.Store]);
                categoryButton.UserData = category;
                categoryButton.OnClicked = SelectItemCategory;

                if (category == MapEntityCategory.Equipment)
                {
                    SelectItemCategory(categoryButton, category);
                }
                x += buttonWidth;
            }

            SelectTab(Tab.Map);

            GameMain.GameSession.Map.OnLocationSelected += SelectLocation;
        }

        private void UpdateLocationTab(Location location)
        {
            if (location.HireManager == null)
            {
                hireList.ClearChildren();
                hireList.Enabled = false;

                new GUITextBlock(new Rectangle(0, 0, 0, 0), "No-one available for hire", Color.Transparent, Color.LightGray, Alignment.Center, Alignment.Center, "", hireList);
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
            mapZoom += PlayerInput.ScrollWheelSpeed / 1000.0f;
            mapZoom = MathHelper.Clamp(mapZoom, 1.0f, 4.0f);

            if (GameMain.GameSession?.Map != null)
            {
                GameMain.GameSession.Map.Update(deltaTime, new Rectangle(
                    tabs[(int)selectedTab].Rect.X + 20,
                    tabs[(int)selectedTab].Rect.Y + 20,
                    tabs[(int)selectedTab].Rect.Width - 310,
                    tabs[(int)selectedTab].Rect.Height - 40), mapZoom);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            /*if (characterList.CountChildren != CrewManager.CharacterInfos.Count)
            {
                UpdateCharacterLists();
            }
            
            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);
            */
             if (selectedTab == Tab.Map && GameMain.GameSession?.Map != null)
             {
                 GameMain.GameSession.Map.Draw(spriteBatch, new Rectangle(
                     tabs[(int)selectedTab].Rect.X + 20, 
                     tabs[(int)selectedTab].Rect.Y + 20,
                     tabs[(int)selectedTab].Rect.Width - 310, 
                     tabs[(int)selectedTab].Rect.Height - 40), mapZoom);
             }

             /*if (topPanel.UserData as Location != GameMain.GameSession.Map.CurrentLocation)
             {
                 UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);
             }
             
            spriteBatch.End();*/

        }

        public void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in GameMain.GameSession.CrewManager.CharacterInfos)
            {
                c.CreateCharacterFrame(characterList, c.Name + " (" + c.Job.Name + ") ", c);
            }
        }


        private void CreateItemFrame(MapEntityPrefab ep, GUIListBox listBox, int width)
        {
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), "ListBoxElement", listBox);
            frame.UserData = ep;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            frame.ToolTip = ep.Description;

            ScalableFont font = listBox.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(50, 0, 0, 25),
                ep.Name,
                null, null,
                Alignment.Left, Alignment.CenterX | Alignment.Left,
                "", frame);
            textBlock.Font = font;
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);
            textBlock.ToolTip = ep.Description;

            if (ep.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.CenterLeft, frame);
                img.Color = ep.SpriteColor;
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }

            textBlock = new GUITextBlock(
                new Rectangle(width - 80, 0, 80, 25),
                ep.Price.ToString(),
                null, null, Alignment.TopLeft,
                Alignment.TopLeft, "", frame);
            textBlock.Font = font;
            textBlock.ToolTip = ep.Description;

        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;
            if (prefab == null) return false;

            CreateItemFrame(prefab, selectedItemList, selectedItemList.Rect.Width);

            buyButton.Enabled = campaign.Money >= selectedItemCost;

            return false;
        }

        private bool DeselectItem(GUIComponent component, object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;
            if (prefab == null) return false;

            selectedItemList.RemoveChild(selectedItemList.children.Find(c => c.UserData == obj));

            return false;
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

                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 20, 0, 20), "Mission: " + mission.Name, "", locationPanel);
                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 40, 0, 20), "Reward: " + mission.Reward + " credits", "", locationPanel);
                new GUITextBlock(new Rectangle(0, titleText.Rect.Height + 70, 0, 0), mission.Description, "", Alignment.TopLeft, Alignment.TopLeft, locationPanel, true, GUI.SmallFont);
            }

            if (startButton != null) startButton.Enabled = true;

            selectedLevel = connection.Level;

            OnLocationSelected?.Invoke(location, connection);
        }

        private bool BuyItems(GUIButton button, object obj)
        {
            int cost = selectedItemCost;

            if (campaign.Money < cost) return false;

            campaign.Money -= cost;

            for (int i = selectedItemList.children.Count - 1; i >= 0; i--)
            {
                GUIComponent child = selectedItemList.children[i];

                ItemPrefab ip = child.UserData as ItemPrefab;
                if (ip == null) continue;

                campaign.CargoManager.AddItem(ip);

                selectedItemList.RemoveChild(child);
            }

            return false;
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

            MapEntityCategory category = (MapEntityCategory)selection;
            var items = MapEntityPrefab.list.FindAll(ep => ep.Price > 0.0f && ep.Category.HasFlag(category));

            int width = storeItemList.Rect.Width;

            foreach (MapEntityPrefab ep in items)
            {
                CreateItemFrame(ep, storeItemList, width);
            }

            storeItemList.children.Sort((x, y) => (x.UserData as MapEntityPrefab).Name.CompareTo((y.UserData as MapEntityPrefab).Name));

            foreach (GUIComponent child in button.Parent.children)
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

        private string GetMoney()
        {
            return "Money: " + ((GameMain.GameSession == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", campaign.Money)) + " credits";
        }

        private bool SelectCharacter(GUIComponent component, object selection)
        {
            GUIComponent prevInfoFrame = null;
            foreach (GUIComponent child in tabs[(int)selectedTab].children)
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

            if (previewFrame == null || previewFrame.UserData != characterInfo)
            {
                int width = Math.Min(300, tabs[(int)Tab.Crew].Rect.Width - hireList.Rect.Width - characterList.Rect.Width - 50);

                previewFrame = new GUIFrame(new Rectangle(0, 60, width, 300),
                        new Color(0.0f, 0.0f, 0.0f, 0.8f),
                        Alignment.TopCenter, "", tabs[(int)selectedTab]);
                previewFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
                previewFrame.UserData = characterInfo;

                characterInfo.CreateInfoFrame(previewFrame);
            }

            if (component.Parent == hireList)
            {
                GUIButton hireButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Hire", Alignment.BottomCenter, "", previewFrame);
                hireButton.UserData = characterInfo;
                hireButton.OnClicked = HireCharacter;
            }

            return true;
        }

        private bool HireCharacter(GUIButton button, object selection)
        {
            /*CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (campaign.TryHireCharacter(GameMain.GameSession.Map.CurrentLocation.HireManager, characterInfo))
            {
                UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);

                SelectCharacter(null, null);
            }*/

            return false;
        }


    }
}
