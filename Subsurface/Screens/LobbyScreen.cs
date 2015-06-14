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

            //---------------------------------------

            rightPanel[1] = new GUIFrame(panelRect, GUI.style.backGroundColor);
            rightPanel[1].Padding = GUI.style.smallPadding;

            hireList = new GUIListBox(new Rectangle(0, 30, 300, 0), Color.White, Alignment.Left, rightPanel[1]);
            
            hireList.OnSelected = HireCharacter;
        }

        public override void Select()
        {
            base.Select();

            //Map.Unload();

            UpdateCharacterLists();
            
        }

        private void UpdateCharacterLists()
        {
            characterList.ClearChildren();
            foreach (CharacterInfo c in Game1.GameSession.crewManager.characterInfos)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    c.name, GUI.style, 
                    Alignment.Left, 
                    Alignment.Left,
                    characterList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
            }

            hireList.ClearChildren();
            foreach (CharacterInfo c in Game1.GameSession.hireManager.availableCharacters)
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

            if (characterList.CountChildren != Game1.GameSession.crewManager.characterInfos.Count
                || hireList.CountChildren != Game1.GameSession.hireManager.availableCharacters.Count)
            {
                UpdateCharacterLists();
            }

            graphics.Clear(Color.CornflowerBlue);

            Game1.GameScreen.DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();


            //ConstructionPrefab.list[5].sprite.Draw(spriteBatch, Vector2.Zero, new Vector2(10.0f, 1.0f), Color.White);


            leftPanel.Draw(spriteBatch);
            shiftPanel.Draw(spriteBatch);

            rightPanel[selectedRightPanel].Draw(spriteBatch);

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
            return "Money: " + ((Game1.GameSession == null) ? "" : Game1.GameSession.crewManager.Money.ToString());
        }

        private string GetDay()
        {

            return "Day #" + ((Game1.GameSession == null) ? "" : Game1.GameSession.Day.ToString());
        }

        private float GetWeekProgress()
        {
            if (Game1.GameSession == null) return 0.0f;

            return (float)((Game1.GameSession.Day - 1) % 7) / 7.0f;
        }

        private bool HireCharacter(object selection)
        {
            CharacterInfo characterInfo;
            try { characterInfo = (CharacterInfo)selection; }
            catch { return false; }

            if (characterInfo == null) return false;

            Game1.GameSession.TryHireCharacter(characterInfo);

            return false;
        }

        private bool StartShift(GUIButton button, object selection)
        {
            
            //Map.Load(Game1.GameSession.SaveFile);

            Game1.GameSession.StartShift();

            //EventManager.StartShift();

            Game1.GameScreen.Select();
            
            return true;
        }
    }
}
