using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    class LobbyScreen : Screen
    {
        GUIFrame leftPanel;
        GUIFrame[] rightPanel;

        GUIFrame shiftPanel;

        int selectedRightPanel;

        GUIListBox characterList;
        GUIListBox hireList;

        SinglePlayerMode gameMode;

        Body previewPlatform;
        Hull previewHull;

        Character previewCharacter;

        public LobbyScreen()
        {
            Rectangle panelRect = new Rectangle(
                (int)GUI.style.largePadding.X,
                (int)GUI.style.largePadding.Y,
                (int)(Game1.GraphicsWidth * 0.3f) - (int)(GUI.style.largePadding.X * 1.5f),
                Game1.GraphicsHeight - (int)(GUI.style.largePadding.Y * 2));

            leftPanel = new GUIFrame(panelRect, GUI.style.backGroundColor);
            leftPanel.Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 200, 25), 
                "asdfdasfasdf", Color.Transparent, Color.White, Alignment.Left, leftPanel);

            GUITextBlock moneyText = new GUITextBlock(new Rectangle(0, 30, 200, 25), 
                "", Color.Transparent, Color.White, Alignment.Left, leftPanel);
            moneyText.TextGetter = GetMoney;
            
            GUIButton button = new GUIButton(new Rectangle(0, 60, 100, 30), "Crew", GUI.style, Alignment.CenterX, leftPanel);
            button.UserData = 0;
            button.OnClicked = SelectRightPanel;

            button = new GUIButton(new Rectangle(0, 100, 100, 30), "Hire", GUI.style, Alignment.CenterX, leftPanel);
            button.UserData = 1;
            button.OnClicked = SelectRightPanel;

            //--------------------------------------

            panelRect = new Rectangle(
                panelRect.X + panelRect.Width + (int)(GUI.style.largePadding.X),
                panelRect.Y,
                Game1.GraphicsWidth - panelRect.Width - (int)(GUI.style.largePadding.X * 3.0f),
                (int)(Game1.GraphicsHeight * 0.3f) - (int)(GUI.style.largePadding.Y * 1.5f));

            shiftPanel = new GUIFrame(panelRect, GUI.style.backGroundColor);
            shiftPanel.Padding = GUI.style.smallPadding;

            GUITextBlock dayText = new GUITextBlock(new Rectangle(0, 0, 200, 25),
                "", Color.Transparent, Color.White, Alignment.Left, shiftPanel);
            dayText.TextGetter = GetDay;

            GUIProgressBar progressBar = new GUIProgressBar(new Rectangle(0, 30, 200, 20), Color.Green, 0.0f, shiftPanel);
            progressBar.ProgressGetter = GetWeekProgress;


            button = new GUIButton(new Rectangle(0,0,100,30), "Start", GUI.style, 
                (Alignment.Right | Alignment.Bottom), shiftPanel);
            button.OnClicked = StartShift;
                       
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            rightPanel = new GUIFrame[2];

            panelRect = new Rectangle(
                panelRect.X,
                panelRect.Y + panelRect.Height + (int)(GUI.style.largePadding.Y),
                panelRect.Width,
                (int)(Game1.GraphicsHeight * 0.7f) - (int)(GUI.style.largePadding.Y * 1.5f));

            rightPanel[0] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            rightPanel[0].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 200, 25), "Crew:", Color.Transparent, Color.White, Alignment.Left, rightPanel[0]);

            characterList = new GUIListBox(new Rectangle(0, 30, 300, 0), Color.White, rightPanel[0]);
            characterList.OnSelected = SelectCharacter;

            //---------------------------------------

            rightPanel[1] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            rightPanel[1].Padding = GUI.style.smallPadding;

            hireList = new GUIListBox(new Rectangle(0, 30, 300, 0), Color.White, Alignment.Left, rightPanel[1]);            
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
                Game1.world.RemoveBody(previewPlatform);
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

            previewCharacter.animController.isStanding = true;

            if (previewPlatform == null)
            {
                Body platform = BodyFactory.CreateRectangle(Game1.world, 3.0f, 1.0f, 5.0f);
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
                previewCharacter.animController.Update((float)Physics.step);
                previewCharacter.animController.UpdateAnim((float)Physics.step);
                Game1.world.Step((float)Physics.step);
            }

        }

        private void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in gameMode.crewManager.characterInfos)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.name, GUI.style, 
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
                    new Rectangle(0, 0, 0, 25), 
                    Color.White, 
                    Alignment.Left, 
                    GUI.style,
                    hireList);
                frame.UserData = c;
                frame.Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f);

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.name,
                    Color.Transparent, Color.Black,
                    Alignment.Left,
                    frame);

                textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.salary.ToString(),
                    Color.Transparent, Color.Black,
                    Alignment.Right,
                    frame);
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            leftPanel.Update((float)deltaTime);
            rightPanel[selectedRightPanel].Update((float)deltaTime);
            shiftPanel.Update((float)deltaTime);
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
            shiftPanel.Draw(spriteBatch);

            rightPanel[selectedRightPanel].Draw(spriteBatch);

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

        private string GetDay()
        {

            return "Day #" + ((Game1.GameSession == null) ? "" : gameMode.Day.ToString());
        }

        private float GetWeekProgress()
        {
            if (Game1.GameSession == null) return 0.0f;

            return (float)((gameMode.Day - 1) % 7) / 7.0f;
        }

        private bool SelectCharacter(object selection)
        {
            CharacterInfo characterInfo = selection as CharacterInfo;
            if (characterInfo == null) return false;

            if (Character.Controlled != null && characterInfo == Character.Controlled.info) return false;

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
            Game1.GameSession.StartShift();
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
