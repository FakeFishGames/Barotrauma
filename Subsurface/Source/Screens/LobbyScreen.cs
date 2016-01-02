using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Barotrauma
{
    class LobbyScreen : Screen
    {
        enum PanelTab { Crew = 0, Map = 1, CurrentLocation = 2, Store = 3 }

        private GUIFrame topPanel;
        private GUIFrame[] bottomPanel;

        private GUIButton startButton;

        private int selectedRightPanel;

        private GUIListBox characterList, hireList;

        private GUIListBox selectedItemList;
        private GUIListBox storeItemList;

        private SinglePlayerMode gameMode;

        private GUIFrame previewFrame;

        private GUIButton buyButton;

        private Level selectedLevel;

        float mapZoom = 3.0f;

        private string CostTextGetter()
        {
            return "Cost: "+selectedItemCost.ToString()+" credits";
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

        private CrewManager CrewManager
        {
            get { return GameMain.GameSession.CrewManager; }
        }

        public LobbyScreen()
        {
            Rectangle panelRect = new Rectangle(
                40, 40,
                GameMain.GraphicsWidth - 80,
                100);

            topPanel = new GUIFrame(panelRect, GUI.Style);
            topPanel.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            
            GUITextBlock moneyText = new GUITextBlock(new Rectangle(0, 0, 0, 25), "", GUI.Style, 
                Alignment.BottomLeft, Alignment.BottomLeft, topPanel);
            moneyText.TextGetter = GetMoney;
            
            GUIButton button = new GUIButton(new Rectangle(-360, 0, 100, 30), "Map", null, Alignment.BottomRight, GUI.Style, topPanel);
            button.UserData = PanelTab.Map;
            button.OnClicked = SelectRightPanel;
            SelectRightPanel(button, button.UserData);

            button = new GUIButton(new Rectangle(-240, 0, 100, 30), "Crew", null, Alignment.BottomRight, GUI.Style, topPanel);
            button.UserData = PanelTab.Crew;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(-120, 0, 100, 30), "Hire", null, Alignment.BottomRight, GUI.Style, topPanel);
            button.UserData = PanelTab.CurrentLocation;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Store", null, Alignment.BottomRight, GUI.Style, topPanel);
            button.UserData = PanelTab.Store;
            button.OnClicked = SelectRightPanel;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            panelRect = new Rectangle(
                40,
                panelRect.Bottom + 40,
                panelRect.Width,
                GameMain.GraphicsHeight - 120 - panelRect.Height);

            bottomPanel = new GUIFrame[4];

            bottomPanel[(int)PanelTab.Crew] = new GUIFrame(panelRect, GUI.Style);
            bottomPanel[(int)PanelTab.Crew].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, GUI.Style, bottomPanel[(int)PanelTab.Crew]);

            int crewColumnWidth = Math.Min(300, (panelRect.Width - 40) / 2);
            characterList = new GUIListBox(new Rectangle(0, 0, crewColumnWidth, 0), GUI.Style, bottomPanel[(int)PanelTab.Crew]);
            characterList.OnSelected = SelectCharacter;

            //---------------------------------------

            bottomPanel[(int)PanelTab.Map] = new GUIFrame(panelRect, GUI.Style);
            bottomPanel[(int)PanelTab.Map].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            startButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",
                Alignment.BottomRight, GUI.Style, bottomPanel[(int)PanelTab.Map]);
            startButton.OnClicked = StartShift;
            startButton.Enabled = false;

            //---------------------------------------

            bottomPanel[(int)PanelTab.CurrentLocation] = new GUIFrame(panelRect, GUI.Style);
            bottomPanel[(int)PanelTab.CurrentLocation].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //---------------------------------------

            bottomPanel[(int)PanelTab.Store] = new GUIFrame(panelRect, GUI.Style);
            bottomPanel[(int)PanelTab.Store].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);            

            int sellColumnWidth = (panelRect.Width - 40) / 2 - 20;

            selectedItemList = new GUIListBox(new Rectangle(0, 0, sellColumnWidth, 400), Color.White * 0.7f, GUI.Style, bottomPanel[(int)PanelTab.Store]);
            selectedItemList.OnSelected = DeselectItem;

            var costText = new GUITextBlock(new Rectangle(0, 0, 100, 25), "Cost: ", GUI.Style, Alignment.BottomLeft, Alignment.TopLeft, bottomPanel[(int)PanelTab.Store]);
            costText.TextGetter = CostTextGetter;

            buyButton = new GUIButton(new Rectangle(sellColumnWidth + 20, 0, 100, 25), "Buy", Alignment.Bottom, GUI.Style, bottomPanel[(int)PanelTab.Store]);
            buyButton.OnClicked = BuyItems;

            storeItemList = new GUIListBox(new Rectangle(0, 30, sellColumnWidth, 400), Color.White * 0.7f, Alignment.TopRight, GUI.Style, bottomPanel[(int)PanelTab.Store]);
            storeItemList.OnSelected = SelectItem;

            int x = selectedItemList.Rect.Width + 40;
            foreach (MapEntityCategory category in Enum.GetValues(typeof(MapEntityCategory)))
            {
                var items = MapEntityPrefab.list.FindAll(ep => ep.Price>0.0f && ep.Category == category);
                if (!items.Any()) continue;

                var categoryButton = new GUIButton(new Rectangle(x, 0, 100, 20), category.ToString(), GUI.Style, bottomPanel[(int)PanelTab.Store]);
                categoryButton.UserData = category;
                categoryButton.OnClicked = SelectItemCategory;

                if (category==MapEntityCategory.Equipment)
                {
                    SelectItemCategory(categoryButton, category);
                }
                x += 110;

            }

            
        }

        public override void Select()
        {
            base.Select();

            GUI.ScreenOverlayColor = Color.Transparent;

            gameMode = GameMain.GameSession.gameMode as SinglePlayerMode;

            foreach (GUIComponent component in topPanel.children)
            {
                var button = component as GUIButton;
                if (button == null || button.Text != "Hire") continue;

                button.Enabled = GameMain.GameSession.Map.CurrentLocation.Type.HasHireableCharacters;
                break;
            }

            UpdateCharacterLists();            
        }

        private void UpdateLocationTab(Location location)
        {
            topPanel.RemoveChild(topPanel.FindChild("locationtitle"));

            topPanel.UserData = location;

            var locationTitle = new GUITextBlock(new Rectangle(0, 0, 200, 25),
                "Location: "+location.Name, Color.Transparent, Color.White, Alignment.TopLeft, GUI.Style, topPanel);
            locationTitle.UserData = "locationtitle";
            locationTitle.Font = GUI.LargeFont;

            bottomPanel[(int)PanelTab.CurrentLocation].ClearChildren();
            bottomPanel[(int)PanelTab.CurrentLocation].UserData = location;

            if (location.HireManager != null)
            {
                hireList = new GUIListBox(new Rectangle(0, 0, 300, 0), GUI.Style, Alignment.Left, bottomPanel[(int)PanelTab.CurrentLocation]);
                hireList.OnSelected = SelectCharacter;

                hireList.ClearChildren();
                foreach (CharacterInfo c in location.HireManager.availableCharacters)
                {
                    var frame = c.CreateCharacterFrame(hireList, c.Name + " (" + c.Job.Name + ")", c);

                    //GUITextBlock textBlock = new GUITextBlock(
                    //    new Rectangle(0, 0, 0, 25),
                    //    c.Name + " (" + c.Job.Name + ")", GUI.Style, hireList);
                    //textBlock.UserData = c;

                    var textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        c.Salary.ToString(),
                        null, null,
                        Alignment.TopRight, GUI.Style, frame);
                }            
            }
        }


        public override void Deselect()
        {
            SelectLocation(null,null);

            base.Deselect();
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            GUIComponent locationPanel = bottomPanel[(int)PanelTab.Map].GetChild("selectedlocation");

            if (locationPanel != null) bottomPanel[(int)PanelTab.Map].RemoveChild(locationPanel);

            locationPanel = new GUIFrame(new Rectangle(0, 0, 250, 190), Color.Transparent, Alignment.TopRight, null, bottomPanel[(int)PanelTab.Map]);
            locationPanel.UserData = "selectedlocation";

            if (location == null) return;

            new GUITextBlock(new Rectangle(0, 0, 250, 0), location.Name, GUI.Style,  Alignment.TopLeft, Alignment.TopCenter, locationPanel, true, GUI.LargeFont);

            if (GameMain.GameSession.Map.SelectedConnection != null && GameMain.GameSession.Map.SelectedConnection.Mission != null)
            {
                var mission = GameMain.GameSession.Map.SelectedConnection.Mission;

                new GUITextBlock(new Rectangle(0, 80, 0, 20), "Mission: "+mission.Name, Color.Black*0.8f, Color.White, Alignment.TopLeft, null, locationPanel);

                new GUITextBlock(new Rectangle(0, 100, 0, 20), "Reward: " + mission.Reward+" credits", Color.Black * 0.8f, Color.White, Alignment.TopLeft, null, locationPanel);

                new GUITextBlock(new Rectangle(0, 120, 0, 0), mission.Description, Color.Black * 0.8f, Color.White, Alignment.TopLeft, null, locationPanel, true);

            }

            startButton.Enabled = true;

            selectedLevel = connection.Level;
        }

        private void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in CrewManager.characterInfos)
            {
                c.CreateCharacterFrame(characterList, c.Name + " ("+c.Job.Name+") ", c);
            }
        }

        private void CreateItemFrame(MapEntityPrefab ep, GUIListBox listBox, int width)
        {
            Color color = ((listBox.CountChildren % 2) == 0) ? Color.Transparent : Color.White * 0.1f;

            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), Color.Transparent, null, listBox);
            frame.UserData = ep;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            frame.Color = color;
            frame.HoverColor = Color.Gold * 0.2f;
            frame.SelectedColor = Color.Gold * 0.5f;

            SpriteFont font = listBox.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(50, 0, 0, 25),
                ep.Name,
                Color.Transparent, Color.White,
                Alignment.Left, Alignment.CenterX | Alignment.Left,
                null, frame);
            textBlock.Font = font;
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            if (ep.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                img.Color = ep.SpriteColor;
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }

            textBlock = new GUITextBlock(
                new Rectangle(width - 80, 0, 80, 25),
                ep.Price.ToString(),
                null, null, Alignment.TopLeft,
                Alignment.TopLeft, GUI.Style, frame);
            textBlock.Font = font;

        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;
            if (prefab == null) return false;

            CreateItemFrame(prefab, selectedItemList, selectedItemList.Rect.Width);

            buyButton.Enabled = CrewManager.Money >= selectedItemCost;

            return false;
        }

        private bool DeselectItem(GUIComponent component, object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;
            if (prefab == null) return false;

            selectedItemList.RemoveChild(selectedItemList.children.Find(c => c.UserData == obj));

            return false;
        }

        private bool BuyItems(GUIButton button, object obj)
        {
            int cost =  selectedItemCost;

            if (CrewManager.Money < cost) return false;

            CrewManager.Money -= cost;

            for (int i = selectedItemList.children.Count-1; i>=0; i--)
            {
                GUIComponent child = selectedItemList.children[i];

                MapEntityPrefab ep = child.UserData as MapEntityPrefab;
                if (ep == null) continue;

                gameMode.CargoManager.AddItem(ep);
                
                selectedItemList.RemoveChild(child);
            }


            return false;
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            topPanel.Update((float)deltaTime);
            bottomPanel[selectedRightPanel].Update((float)deltaTime);

            mapZoom += PlayerInput.ScrollWheelSpeed / 1000.0f;
            mapZoom = MathHelper.Clamp(mapZoom, 1.0f, 4.0f);
            //shiftPanel.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {

            if (characterList.CountChildren != CrewManager.characterInfos.Count)
            {
                UpdateCharacterLists();
            }

            graphics.Clear(Color.Black);

            //GameMain.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            Sprite backGround = GameMain.GameSession.Map.CurrentLocation.Type.Background;
            spriteBatch.Draw(backGround.Texture, Vector2.Zero, null, Color.White, 0.0f, Vector2.Zero,
                Math.Max((float)GameMain.GraphicsWidth / backGround.SourceRect.Width, (float)GameMain.GraphicsHeight / backGround.SourceRect.Height), SpriteEffects.None, 0.0f);
            
            topPanel.Draw(spriteBatch);

            bottomPanel[selectedRightPanel].Draw(spriteBatch);

            if (selectedRightPanel == (int)PanelTab.Map)
            {
                GameMain.GameSession.Map.Draw(spriteBatch, new Rectangle(
                    bottomPanel[selectedRightPanel].Rect.X + 20, 
                    bottomPanel[selectedRightPanel].Rect.Y + 20,
                    bottomPanel[selectedRightPanel].Rect.Width - 310, 
                    bottomPanel[selectedRightPanel].Rect.Height - 40), mapZoom);
            }

            if (topPanel.UserData as Location != GameMain.GameSession.Map.CurrentLocation)
            {
                UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);
            }

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();

        }

        public bool SelectRightPanel(GUIButton button, object selection)
        {
            try 
            { 
                selectedRightPanel =  (int)selection;                
            }
            catch { return false; }


            if (button != null)
            {
                button.Selected = true;
                foreach (GUIComponent child in topPanel.children)
                {
                    GUIButton otherButton = child as GUIButton;
                    if (otherButton == null || otherButton == button) continue;

                    otherButton.Selected = false;
                }
            }

            return true;
        }
        
        private bool SelectItemCategory(GUIButton button, object selection)
        {

            if (!(selection is MapEntityCategory)) return false;
            //var existingList = bottomPanel[(int)PanelTab.Store].children.Find(c => c is GUIListBox && c.UserData is MapEntityCategory);
            //if (existingList != null) bottomPanel[(int)PanelTab.Store].RemoveChild(existingList);

            //bottomPanel[(int)PanelTab.Store].AddChild(storeItemLists[(int)selection]);

            storeItemList.ClearChildren();

            MapEntityCategory category = (MapEntityCategory)selection;
            var items = MapEntityPrefab.list.FindAll(ep => ep.Price > 0.0f && ep.Category == category);

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
            return "Money: " + ((GameMain.GameSession == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", CrewManager.Money)) + " credits";
        }

        private bool SelectCharacter(GUIComponent component, object selection)
        {
            GUIComponent prevInfoFrame = null;
            foreach (GUIComponent child in bottomPanel[selectedRightPanel].children)
            {
                if (child.UserData as CharacterInfo == null) continue;

                prevInfoFrame = child;
            }

            if (prevInfoFrame != null) bottomPanel[selectedRightPanel].RemoveChild(prevInfoFrame);

            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) return false;

            if (previewFrame == null || previewFrame.UserData != characterInfo)
            {
                previewFrame = new GUIFrame(new Rectangle(bottomPanel[(int)PanelTab.Crew].Rect.Width/2, 60, Math.Min(300,bottomPanel[(int)PanelTab.Crew].Rect.Width/2 - 40), 300),
                        new Color(0.0f, 0.0f, 0.0f, 0.8f),
                        Alignment.Top, GUI.Style, bottomPanel[selectedRightPanel]);
                previewFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
                previewFrame.UserData = characterInfo;
                
                characterInfo.CreateInfoFrame(previewFrame);                
            }

            if (selectedRightPanel == (int)PanelTab.CurrentLocation)
            {
                GUIButton hireButton = new GUIButton(new Rectangle(0,0, 100, 20), "Hire", Alignment.BottomCenter, GUI.Style, previewFrame);
                hireButton.UserData = characterInfo;
                hireButton.OnClicked = HireCharacter;
            }

            return false;
        }

        private bool HireCharacter(GUIButton button, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (gameMode.TryHireCharacter(GameMain.GameSession.Map.CurrentLocation.HireManager, characterInfo))
            {
                UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);
            }



            return false;
        }

        private bool StartShift(GUIButton button, object selection)
        {
            GameMain.ShowLoading(ShiftLoading());

            //GameMain.GameSession.StartShift(selectedLevel, false);
            //GameMain.GameScreen.Select();
            
            return true;
        }

        private IEnumerable<object> ShiftLoading()
        {
            GameMain.GameSession.StartShift(selectedLevel, false);
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public bool QuitToMainMenu(GUIButton button, object selection)
        {
            GameMain.MainMenuScreen.Select();
            return true;
        }
    }
}
