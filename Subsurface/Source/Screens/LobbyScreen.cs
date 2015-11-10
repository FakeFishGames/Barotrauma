using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class LobbyScreen : Screen
    {
        enum PanelTab { Crew = 0, Map = 1, CurrentLocation = 2, Store = 3 }

        private GUIFrame leftPanel;
        private GUIFrame[] rightPanel;

        private GUIButton startButton;

        private int selectedRightPanel;

        private GUIListBox characterList;
        private GUIListBox hireList;

        private GUIListBox selectedItemList, itemList;

        private SinglePlayerMode gameMode;

        private GUIFrame previewFrame;

        private GUIButton buyButton;

        private Level selectedLevel;

        float mapZoom = 3.0f;

        private string CostTextGetter()
        {
            return "Cost: "+selectedItemCost.ToString();
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
                GameMain.GraphicsWidth < 1000 ? 140 : 180,
                GameMain.GraphicsHeight - 80);

            leftPanel = new GUIFrame(panelRect, GUI.Style);
            leftPanel.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //new GUITextBlock(new Rectangle(0, 0, 200, 25), 
            //    save, Color.Transparent, Color.White, Alignment.Left, GUI.Style, leftPanel);

            GUITextBlock moneyText = new GUITextBlock(new Rectangle(0, 30, 0, 25), "", GUI.Style, 
                Alignment.TopLeft, Alignment.TopLeft, leftPanel);
            moneyText.TextGetter = GetMoney;
            
            GUIButton button = new GUIButton(new Rectangle(0, 70, 100, 30), "Map", null, Alignment.TopCenter, GUI.Style, leftPanel);
            button.UserData = PanelTab.Map;
            button.OnClicked = SelectRightPanel;
            SelectRightPanel(button, button.UserData);

            button = new GUIButton(new Rectangle(0, 110, 100, 30), "Crew", null, Alignment.TopCenter, GUI.Style, leftPanel);
            button.UserData = PanelTab.Crew;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 150, 100, 30), "Hire", null, Alignment.TopCenter, GUI.Style, leftPanel);
            button.UserData = PanelTab.CurrentLocation;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 190, 100, 30), "Store", null, Alignment.TopCenter, GUI.Style, leftPanel);
            button.UserData = PanelTab.Store;
            button.OnClicked = SelectRightPanel;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            panelRect = new Rectangle(
                panelRect.X + panelRect.Width + 40,
                40,
                GameMain.GraphicsWidth - panelRect.Width - 120,
                GameMain.GraphicsHeight - 80);

            rightPanel = new GUIFrame[4];

            rightPanel[(int)PanelTab.Crew] = new GUIFrame(panelRect, GUI.Style);
            rightPanel[(int)PanelTab.Crew].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, GUI.Style, rightPanel[(int)PanelTab.Crew]);

            int crewColumnWidth = Math.Min(300, (panelRect.Width - 40) / 2);
            characterList = new GUIListBox(new Rectangle(0, 30, crewColumnWidth, 0), GUI.Style, rightPanel[(int)PanelTab.Crew]);
            characterList.OnSelected = SelectCharacter;

            //---------------------------------------

            rightPanel[(int)PanelTab.Map] = new GUIFrame(panelRect, GUI.Style);
            rightPanel[(int)PanelTab.Map].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            startButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",
                Alignment.BottomRight, GUI.Style, rightPanel[(int)PanelTab.Map]);
            startButton.OnClicked = StartShift;
            startButton.Enabled = false;

            //---------------------------------------

            rightPanel[(int)PanelTab.CurrentLocation] = new GUIFrame(panelRect, GUI.Style);

            //---------------------------------------

            rightPanel[(int)PanelTab.Store] = new GUIFrame(panelRect, GUI.Style);
            rightPanel[(int)PanelTab.Store].Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            int sellColumnWidth = (panelRect.Width - 40) / 2 - 20;

            selectedItemList = new GUIListBox(new Rectangle(0, 0, sellColumnWidth, 400), Color.White * 0.7f, GUI.Style, rightPanel[(int)PanelTab.Store]);
            selectedItemList.OnSelected = DeselectItem;

            var costText = new GUITextBlock(new Rectangle(0, 0, 100, 25), "Cost: ", GUI.Style, Alignment.BottomLeft, Alignment.TopLeft, rightPanel[(int)PanelTab.Store]);
            costText.TextGetter = CostTextGetter;

            buyButton = new GUIButton(new Rectangle(sellColumnWidth+20, 0, 100, 25), "Buy", Alignment.Bottom, GUI.Style, rightPanel[(int)PanelTab.Store]);
            buyButton.OnClicked = BuyItems;

            itemList = new GUIListBox(new Rectangle(0, 0, sellColumnWidth, 400), Color.White * 0.7f, Alignment.TopRight, GUI.Style, rightPanel[(int)PanelTab.Store]);
            itemList.OnSelected = SelectItem;

            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                if (ep.Price == 0) continue;

                CreateItemFrame(ep, itemList);
            }
        }

        public override void Select()
        {
            base.Select();

            gameMode = GameMain.GameSession.gameMode as SinglePlayerMode;

            //Map.Unload();

            UpdateCharacterLists();            
        }

        private void UpdateLocationTab(Location location)
        {
            rightPanel[(int)PanelTab.CurrentLocation] = new GUIFrame(rightPanel[(int)PanelTab.CurrentLocation].Rect, GUI.Style);
            rightPanel[(int)PanelTab.CurrentLocation].UserData = location;
            //rightPanel[(int)PanelTab.Hire].Padding = GUI.style.smallPadding;
            
            new GUITextBlock(new Rectangle(0, 0, 200, 25), 
                "Location: "+location.Name, GUI.Style, rightPanel[(int)PanelTab.CurrentLocation]);
            new GUITextBlock(new Rectangle(0, 20, 200, 25),
                "("+location.Type.Name+")", GUI.Style, rightPanel[(int)PanelTab.CurrentLocation]);
            
            if (location.HireManager != null)
            {
                hireList = new GUIListBox(new Rectangle(0, 60, 300, 0), GUI.Style, Alignment.Left, rightPanel[(int)PanelTab.CurrentLocation]);
                hireList.OnSelected = SelectCharacter;

                hireList.ClearChildren();
                foreach (CharacterInfo c in location.HireManager.availableCharacters)
                {
                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        c.Name + " (" + c.Job.Name + ")", GUI.Style, hireList);
                    textBlock.UserData = c;

                    textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        c.Salary.ToString(),
                        null, null,
                        Alignment.TopRight, GUI.Style, textBlock);
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
            GUIComponent locationPanel = rightPanel[(int)PanelTab.Map].GetChild("selectedlocation");

            if (locationPanel != null) rightPanel[(int)PanelTab.Map].RemoveChild(locationPanel);

            locationPanel = new GUIFrame(new Rectangle(0, 0, 200, 190), Color.Transparent, Alignment.TopRight, null, rightPanel[(int)PanelTab.Map]);
            locationPanel.UserData = "selectedlocation";

            if (location == null) return;

            new GUITextBlock(new Rectangle(0,0,0,0), location.Name, Color.Transparent, Color.White, Alignment.TopLeft, null, locationPanel);

            if (GameMain.GameSession.Map.SelectedConnection != null && GameMain.GameSession.Map.SelectedConnection.Quest != null)
            {
                var quest = GameMain.GameSession.Map.SelectedConnection.Quest;

                new GUITextBlock(new Rectangle(0, 40, 0, 20), "Quest: "+quest.Name, Color.Transparent, Color.White, Alignment.TopLeft, null, locationPanel);
                
                new GUITextBlock(new Rectangle(0, 60, 0, 20), "Reward: " + quest.Reward, Color.Transparent, Color.White, Alignment.TopLeft, null, locationPanel);
                
                new GUITextBlock(new Rectangle(0, 80, 0, 0), quest.Description, Color.Transparent, Color.White, Alignment.TopLeft, null, locationPanel, true);

            }

            startButton.Enabled = true;

            selectedLevel = connection.Level;
        }

        private void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in CrewManager.characterInfos)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.Name + " (" + c.Job.Name + ")", GUI.Style, 
                    Alignment.Left, 
                    Alignment.Left,
                    characterList, false, GameMain.GraphicsWidth<1000 ? GUI.SmallFont : GUI.Font);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = c;
            }
        }

        private void CreateItemFrame(MapEntityPrefab ep, GUIListBox listBox)
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
                new Rectangle(40, 0, 0, 25),
                ep.Name,
                Color.Transparent, Color.White,
                Alignment.Left, Alignment.Left,
                null, frame);
            textBlock.Font = font;
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            textBlock = new GUITextBlock(
                new Rectangle(0, 0, 0, 25),
                ep.Price.ToString(),
                null, null,
                Alignment.TopRight, GUI.Style, textBlock);
            textBlock.Font = font;

            if (ep.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                img.Color = ep.SpriteColor;
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }
        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;
            if (prefab == null) return false;

            CreateItemFrame(prefab, selectedItemList);

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

            leftPanel.Update((float)deltaTime);
            rightPanel[selectedRightPanel].Update((float)deltaTime);

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

            graphics.Clear(Color.CornflowerBlue);

            GameMain.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            leftPanel.Draw(spriteBatch);

            rightPanel[selectedRightPanel].Draw(spriteBatch);

            if (selectedRightPanel == (int)PanelTab.Map)
            {
                GameMain.GameSession.Map.Draw(spriteBatch, new Rectangle(
                    rightPanel[selectedRightPanel].Rect.X + 20, 
                    rightPanel[selectedRightPanel].Rect.Y + 20,
                    rightPanel[selectedRightPanel].Rect.Width - 280, 
                    rightPanel[selectedRightPanel].Rect.Height - 40), mapZoom);
            }
     
            if (rightPanel[(int)selectedRightPanel].UserData as Location != GameMain.GameSession.Map.CurrentLocation)
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
                foreach (GUIComponent child in leftPanel.children)
                {
                    GUIButton otherButton = child as GUIButton;
                    if (otherButton == null || otherButton == button) continue;

                    otherButton.Selected = false;
                }
            }

            return true;
        }
        
        private string GetMoney()
        {
            return "Money: " + ((GameMain.GameSession == null) ? "" : CrewManager.Money.ToString());
        }

        private bool SelectCharacter(GUIComponent component, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) return false;

            if (previewFrame == null || previewFrame.UserData != characterInfo)
            {
                previewFrame = new GUIFrame(new Rectangle(rightPanel[(int)PanelTab.Crew].Rect.Width/2, 60, Math.Min(300,rightPanel[(int)PanelTab.Crew].Rect.Width/2 - 40), 300),
                        new Color(0.0f, 0.0f, 0.0f, 0.8f),
                        Alignment.Top, GUI.Style, rightPanel[selectedRightPanel]);
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
