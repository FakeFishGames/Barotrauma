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
        enum PanelTab { Crew = 0, Map = 1, Hire = 2 }

        GUIFrame leftPanel;
        GUIFrame[] rightPanel;

        GUIButton startButton;

        int selectedRightPanel;

        GUIListBox characterList;
        GUIListBox hireList;

        SinglePlayerMode gameMode;

        Body previewPlatform;
        Hull previewHull;

        Character previewCharacter;

        Level selectedLevel;

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
            
            GUIButton button = new GUIButton(new Rectangle(0, 60, 100, 30), "Map", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Map;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 100, 100, 30), "Crew", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Crew;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 140, 100, 30), "Hire", null, Alignment.Left, GUI.style, leftPanel);
            button.UserData = PanelTab.Hire;
            button.OnClicked = SelectRightPanel;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            panelRect = new Rectangle(
                panelRect.X + panelRect.Width + 40,
                40,
                Game1.GraphicsWidth - panelRect.Width - 120,
                Game1.GraphicsHeight - 80);

            rightPanel = new GUIFrame[3];

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

            rightPanel[(int)PanelTab.Hire] = new GUIFrame(panelRect, GUI.style);
            //rightPanel[(int)PanelTab.Hire].Padding = GUI.style.smallPadding;

            hireList = new GUIListBox(new Rectangle(0, 30, 300, 0), GUI.style, Alignment.Left, rightPanel[(int)PanelTab.Hire]);            
            hireList.OnSelected = HireCharacter;
        }

        public override void Select()
        {
            base.Select();

            gameMode = Game1.GameSession.gameMode as SinglePlayerMode;

            //Map.Unload();

            UpdateCharacterLists();            
        }

        public override void Deselect()
        {
            base.Deselect();

            if (previewPlatform != null)
            {
                Game1.World.RemoveBody(previewPlatform);
                previewPlatform = null;
            }

            if (previewHull != null)
            {
                previewHull.Remove();
                previewHull = null;
            }

            if (previewCharacter != null)
            {
                previewCharacter.Remove();
                previewCharacter = null;
            }
        }

        private void CreatePreviewCharacter()
        {
            if (previewCharacter != null) previewCharacter.Remove();

            Vector2 pos = new Vector2(1000.0f, 1000.0f);

            previewCharacter = new Character(characterList.SelectedData as CharacterInfo, pos);

            previewCharacter.AnimController.IsStanding = true;

            if (previewPlatform == null)
            {
                Body platform = BodyFactory.CreateRectangle(Game1.World, 3.0f, 1.0f, 5.0f);
                platform.SetTransform(new Vector2(pos.X, pos.Y - 3.5f), 0.0f);
                platform.IsStatic = true;
            }

            if (previewHull == null)
            {
                pos = ConvertUnits.ToDisplayUnits(pos);
                previewHull = new Hull(new Rectangle((int)pos.X - 100, (int)pos.Y + 100, 200, 500));
            }

            Physics.Alpha = 1.0f;

            for (int i = 0; i < 500; i++)
            {
                previewCharacter.AnimController.Update((float)Physics.step);
                previewCharacter.AnimController.UpdateAnim((float)Physics.step);
                Game1.World.Step((float)Physics.step);
            }
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
                    c.Name, GUI.style, 
                    Alignment.Left, 
                    Alignment.Left,
                    characterList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = c;
            }

            hireList.ClearChildren();
            foreach (CharacterInfo c in gameMode.hireManager.availableCharacters)
            {
                GUIFrame frame = new GUIFrame(
                    new Rectangle(0, 0, 0, 25), Color.White, Alignment.Left, null, hireList);
                frame.UserData = c;
                frame.Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f);

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.Name,
                    Color.Transparent, Color.Black,
                    Alignment.Left, null, frame);

                textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.Salary.ToString(),
                    Color.Transparent, Color.Black,
                    Alignment.Right, null, frame);
            }
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

            if (characterList.CountChildren != gameMode.crewManager.characterInfos.Count
                || hireList.CountChildren != gameMode.hireManager.availableCharacters.Count)
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
                Game1.GameSession.map.Draw(spriteBatch, new Rectangle(
                    rightPanel[selectedRightPanel].Rect.Right - 20 - 400, 
                    rightPanel[selectedRightPanel].Rect.Y + 20, 
                    400, 400));
            }            

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();

            if (characterList.SelectedData != null)
            {
                if (previewCharacter != null)
                {
                    Vector2 position = new Vector2(characterList.Rect.Right + 100, characterList.Rect.Y + 25.0f);

                    Vector2 pos = previewCharacter.Position;
                    pos.Y = -pos.Y;
                    Matrix transform = Matrix.CreateTranslation(new Vector3(-pos + position, 0.0f));

                    spriteBatch.Begin(SpriteSortMode.BackToFront, null, null, null, null, null, transform);
                    previewCharacter.Draw(spriteBatch);
                    spriteBatch.End();
                }
                else
                {
                    CreatePreviewCharacter();
                }
            }
        }

        public bool SelectRightPanel(GUIButton button, object selection)
        {
            try { selectedRightPanel = (int)selection; }
            catch { return false; }
            return true;
        }

        //private void CreatePreviewCharacter()
        //{
        //    if (Game1.Client.Character != null) Game1.Client.Character.Remove();

        //    Vector2 pos = new Vector2(1000.0f, 1000.0f);

        //    Character character = new Character(Game1.Client.CharacterInfo, pos);

        //    Game1.Client.Character = character;

        //    character.animController.isStanding = true;

        //    if (previewPlatform == null)
        //    {
        //        Body platform = BodyFactory.CreateRectangle(Game1.world, 3.0f, 1.0f, 5.0f);
        //        platform.SetTransform(new Vector2(pos.X, pos.Y - 2.5f), 0.0f);
        //        platform.IsStatic = true;
        //    }

        //    if (previewPlatform == null)
        //    {
        //        pos = ConvertUnits.ToDisplayUnits(pos);
        //        new Hull(new Rectangle((int)pos.X - 100, (int)pos.Y + 100, 200, 200));
        //    }

        //    Physics.Alpha = 1.0f;

        //    for (int i = 0; i < 500; i++)
        //    {
        //        character.animController.Update((float)Physics.step);
        //        character.animController.UpdateAnim((float)Physics.step);
        //        Game1.world.Step((float)Physics.step);
        //    }
        //}

        private string GetMoney()
        {
            return "Money: " + ((Game1.GameSession == null) ? "" : gameMode.crewManager.Money.ToString());
        }

        private bool SelectCharacter(object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (Character.Controlled != null && characterInfo == Character.Controlled.Info) return false;

            CreatePreviewCharacter();

            return false;
        }

        private bool HireCharacter(object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            gameMode.TryHireCharacter(characterInfo);

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
