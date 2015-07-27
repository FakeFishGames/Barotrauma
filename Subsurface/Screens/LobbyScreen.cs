using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Subsurface
{
    class LobbyScreen : Screen
    {
        enum PanelTab { Crew = 0, Map = 1, CurrentLocation = 2, Store = 3 }

        GUIFrame leftPanel;
        GUIFrame[] rightPanel;

        GUIButton startButton;

        int selectedRightPanel;

        GUIListBox characterList;
        GUIListBox hireList;

        GUIListBox selectedItemList;

        SinglePlayerMode gameMode;

        GUIFrame previewFrame;

        GUIButton buyButton;

        Level selectedLevel;

        private string SelectedItemCost()
        {
            return selectedItemCost.ToString();
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

        public LobbyScreen()
        {
            Rectangle panelRect = new Rectangle(
                40, 40,
                (int)(Game1.GraphicsWidth * 0.3f) - 60,
                Game1.GraphicsHeight - 80);

            leftPanel = new GUIFrame(panelRect, GUI.style);
            //leftPanel.Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 200, 25), 
                "asdfdasfasdf", Color.Transparent, Color.White, Alignment.Left, GUI.style, leftPanel);

            GUITextBlock moneyText = new GUITextBlock(new Rectangle(0, 30, 200, 25), 
                "", Color.Transparent, Color.White, Alignment.Left, GUI.style, leftPanel);
            moneyText.TextGetter = GetMoney;
            
            GUIButton button = new GUIButton(new Rectangle(0, 70, 100, 30), "Map", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Map;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 110, 100, 30), "Crew", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Crew;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 150, 100, 30), "Hire", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.CurrentLocation;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 190, 100, 30), "Store", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Store;
            button.OnClicked = SelectRightPanel;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            panelRect = new Rectangle(
                panelRect.X + panelRect.Width + 40,
                40,
                Game1.GraphicsWidth - panelRect.Width - 120,
                Game1.GraphicsHeight - 80);

            rightPanel = new GUIFrame[4];

            rightPanel[(int)PanelTab.Crew] = new GUIFrame(panelRect, GUI.style);
            //rightPanel[(int)PanelTab.Crew].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, GUI.style, rightPanel[(int)PanelTab.Crew]);

            characterList = new GUIListBox(new Rectangle(0, 30, 300, 0), GUI.style, rightPanel[(int)PanelTab.Crew]);
            characterList.OnSelected = SelectCharacter;

            //---------------------------------------

            rightPanel[(int)PanelTab.Map] = new GUIFrame(panelRect, GUI.style);
            //rightPanel[(int)PanelTab.Map].Padding = GUI.style.smallPadding;

            startButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",
                Alignment.BottomRight, GUI.style, rightPanel[(int)PanelTab.Map]);
            startButton.OnClicked = StartShift;
            startButton.Enabled = false;

            //---------------------------------------

            rightPanel[(int)PanelTab.CurrentLocation] = new GUIFrame(panelRect, GUI.style);

            //---------------------------------------

            rightPanel[(int)PanelTab.Store] = new GUIFrame(panelRect, GUI.style);

            selectedItemList = new GUIListBox(new Rectangle(0, 0, 300, 400), Color.White * 0.7f, GUI.style, rightPanel[(int)PanelTab.Store]);

            var costText = new GUITextBlock(new Rectangle(0, 0, 200, 25), "Cost: ", Color.Transparent, Color.White, Alignment.BottomLeft, GUI.style, rightPanel[(int)PanelTab.Store]);
            costText.TextGetter = SelectedItemCost;

            buyButton = new GUIButton(new Rectangle(15, 0, 100, 25), "Buy", Alignment.Bottom, GUI.style, rightPanel[(int)PanelTab.Store]);
            buyButton.OnClicked = BuyItems;

            GUIListBox itemList = new GUIListBox(new Rectangle(0, 0, 300, 400), Color.White * 0.7f, Alignment.TopRight, GUI.style, rightPanel[(int)PanelTab.Store]);
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

            gameMode = Game1.GameSession.gameMode as SinglePlayerMode;

            //Map.Unload();

            UpdateCharacterLists();            
        }

        private void UpdateLocationTab(Location location)
        {
            rightPanel[(int)PanelTab.CurrentLocation] = new GUIFrame(rightPanel[(int)PanelTab.CurrentLocation].Rect, GUI.style);
            rightPanel[(int)PanelTab.CurrentLocation].UserData = location;
            //rightPanel[(int)PanelTab.Hire].Padding = GUI.style.smallPadding;
            
            new GUITextBlock(new Rectangle(0, 0, 200, 25), 
                "Location: "+location.Name, GUI.style, rightPanel[(int)PanelTab.CurrentLocation]);
            new GUITextBlock(new Rectangle(0, 20, 200, 25),
                "("+location.Type.Name+")", GUI.style, rightPanel[(int)PanelTab.CurrentLocation]);
            
            if (location.HireManager != null)
            {
                hireList = new GUIListBox(new Rectangle(0, 60, 300, 0), GUI.style, Alignment.Left, rightPanel[(int)PanelTab.CurrentLocation]);
                hireList.OnSelected = SelectCharacter;

                hireList.ClearChildren();
                foreach (CharacterInfo c in location.HireManager.availableCharacters)
                {
                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        c.Name + " (" + c.Job.Name + ")", GUI.style, hireList);
                    textBlock.UserData = c;

                    textBlock = new GUITextBlock(
                        new Rectangle(0, 0, 0, 25),
                        c.Salary.ToString(),
                        null, null,
                        Alignment.TopRight, GUI.style, textBlock);
                }
            }
        }


        public override void Deselect()
        {
            base.Deselect();
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            GUIComponent locationPanel = rightPanel[(int)PanelTab.Map].GetChild("selectedlocation");

            if (locationPanel != null) rightPanel[(int)PanelTab.Map].RemoveChild(locationPanel);

            locationPanel = new GUIFrame(new Rectangle(0, 0, rightPanel[(int)PanelTab.Map].Rect.Width / 2 - 40, 190), Color.Transparent, null, rightPanel[(int)PanelTab.Map]);
            locationPanel.UserData = "selectedlocation";

            new GUITextBlock(new Rectangle(0,0,100,20), location.Name, Color.Transparent, Color.White, Alignment.TopLeft, null, locationPanel);

            startButton.Enabled = true;

            selectedLevel = connection.Level;
        }

        private void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in gameMode.crewManager.characterInfos)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.Name + " (" + c.Job.Name + ")", GUI.style, 
                    Alignment.Left, 
                    Alignment.Left,
                    characterList);
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

            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(40, 0, 0, 25),
                ep.Name,
                Color.Transparent, Color.White,
                Alignment.Left, Alignment.Left,
                null, frame);
            textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

            textBlock = new GUITextBlock(
                new Rectangle(0, 0, 0, 25),
                ep.Price.ToString(),
                null, null,
                Alignment.TopRight, GUI.style, textBlock);

            if (ep.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), ep.sprite, Alignment.Left, frame);
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
            }
        }

        private bool SelectItem(object obj)
        {
            MapEntityPrefab prefab = obj as MapEntityPrefab;

            if (prefab == null) return false;

            CreateItemFrame(prefab, selectedItemList);

            buyButton.Enabled = gameMode.crewManager.Money >= selectedItemCost;

            return false;
        }

        private bool BuyItems(GUIButton button, object obj)
        {
            int cost =  selectedItemCost;

            if (gameMode.crewManager.Money < cost) return false;

            gameMode.crewManager.Money -= cost;

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
            //shiftPanel.Update((float)deltaTime);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {

            if (characterList.CountChildren != gameMode.crewManager.characterInfos.Count)
            {
                UpdateCharacterLists();
            }

            graphics.Clear(Color.CornflowerBlue);

            Game1.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            leftPanel.Draw(spriteBatch);

            rightPanel[selectedRightPanel].Draw(spriteBatch);

            if (selectedRightPanel == (int)PanelTab.Map)
            {
                Game1.GameSession.Map.Draw(spriteBatch, new Rectangle(
                    rightPanel[selectedRightPanel].Rect.X + 20, 
                    rightPanel[selectedRightPanel].Rect.Y + 20,
                    rightPanel[selectedRightPanel].Rect.Width - 40, 
                    rightPanel[selectedRightPanel].Rect.Height - 150), 3.0f);
            }
     
            if (rightPanel[(int)selectedRightPanel].UserData as Location != Game1.GameSession.Map.CurrentLocation)
            {
                UpdateLocationTab(Game1.GameSession.Map.CurrentLocation);
            }

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();

        }

        public bool SelectRightPanel(GUIButton button, object selection)
        {
            try { selectedRightPanel = (int)selection; }
            catch { return false; }
            return true;
        }
        
        private string GetMoney()
        {
            return "Money: " + ((Game1.GameSession == null) ? "" : gameMode.crewManager.Money.ToString());
        }

        private bool SelectCharacter(object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) return false;

            if (previewFrame == null || previewFrame.UserData != characterInfo)
            {
                previewFrame = new GUIFrame(new Rectangle(350, 60, 300, 300),
                        new Color(0.0f, 0.0f, 0.0f, 0.8f),
                        Alignment.Top, GUI.style, rightPanel[selectedRightPanel]);
                previewFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
                previewFrame.UserData = characterInfo;
                
                characterInfo.CreateInfoFrame(previewFrame);                
            }

            if (selectedRightPanel == (int)PanelTab.CurrentLocation)
            {
                GUIButton hireButton = new GUIButton(new Rectangle(0,0, 100, 20), "Hire", Alignment.BottomCenter, GUI.style, previewFrame);
                hireButton.UserData = characterInfo;
                hireButton.OnClicked = HireCharacter;
            }

            return false;
        }

        private bool HireCharacter(GUIButton button, object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (gameMode.TryHireCharacter(Game1.GameSession.Map.CurrentLocation.HireManager, characterInfo))
            {
                UpdateLocationTab(Game1.GameSession.Map.CurrentLocation);
            }



            return false;
        }

        private bool StartShift(GUIButton button, object selection)
        {           
            Game1.GameSession.StartShift(TimeSpan.Zero, selectedLevel);
            Game1.GameScreen.Select();
            
            return true;
        }

        public bool QuitToMainMenu(GUIButton button, object selection)
        {
            Game1.MainMenuScreen.Select();
            return true;
        }
    }
}
