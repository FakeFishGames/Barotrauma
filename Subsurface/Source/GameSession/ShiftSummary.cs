using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    class ShiftSummary
    {
        private Location startLocation, endLocation;

        private GameSession gameSession;

        private Mission selectedMission;
               
        public ShiftSummary(GameSession gameSession)
        {
            this.gameSession = gameSession;

            startLocation   = gameSession.Map==null ? null : gameSession.Map.CurrentLocation;
            endLocation     = gameSession.Map==null ? null : gameSession.Map.SelectedLocation;
            
            selectedMission = gameSession.Mission;
        }


        //public void AddCasualty(Character character, CauseOfDeath causeOfDeath)
        //{
        //    casualties.Add(new Casualty(character.Info, causeOfDeath, ""));
        //}

        public GUIFrame CreateSummaryFrame(string endMessage)
        {
            bool singleplayer = GameMain.NetworkMember == null;

            bool gameOver = gameSession.CrewManager.characters.All(c => c.IsDead);
            bool progress = Submarine.MainSub.AtEndPosition;
            
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.8f);
            
            int width = 760, height = 400;
            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, GUI.Style, frame);
            
            int y = 0;
            
            if (singleplayer)
            {
                string summaryText = InfoTextManager.GetInfoText(gameOver ? "gameover" :
                    (progress ? "progress" : "return"));

                var infoText = new GUITextBlock(new Rectangle(0, y, 0, 50), summaryText, GUI.Style, innerFrame, true);
                y += infoText.Rect.Height;
            }


            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new Rectangle(0, y, 0, 30), endMessage, GUI.Style, innerFrame, true);

                y += 30 + endText.Text.Split('\n').Length * 20;
            }

            new GUITextBlock(new Rectangle(0, y, 0, 20), "Crew status:", GUI.Style, innerFrame, GUI.LargeFont);
            y += 30;

            GUIListBox listBox = new GUIListBox(new Rectangle(0,y,0,90), null, Alignment.TopLeft, GUI.Style, innerFrame, true);

            int x = 0;
            foreach (Character character in gameSession.CrewManager.characters)
            {
                if (GameMain.GameSession.Mission is CombatMission && 
                    character.TeamID != GameMain.GameSession.CrewManager.WinningTeam)
                {
                    continue;
                }

                var characterFrame = new GUIFrame(new Rectangle(x, y, 170, 70), Color.Transparent, GUI.Style, listBox);
                characterFrame.OutlineColor = Color.Transparent;
                characterFrame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                characterFrame.CanBeFocused = false;

                character.Info.CreateCharacterFrame(characterFrame,
                    character.Info.Job != null ? (character.Info.Name + '\n' + "(" + character.Info.Job.Name + ")") : character.Info.Name, null);
                
                string statusText = "OK";
                Color statusColor = Color.DarkGreen;

                if (character.IsDead)
                {
                    statusText = InfoTextManager.GetInfoText("CauseOfDeath." + character.CauseOfDeath.ToString());
                    statusColor = Color.DarkRed;
                }
                else
                {

                    if (character.IsUnconscious)
                    {
                        statusText = "Unconscious";
                        statusColor = Color.DarkOrange;
                    }
                    else if (character.Health / character.MaxHealth < 0.8f)
                    {
                        statusText = "Injured";
                        statusColor = Color.DarkOrange;
                    }

                }

                new GUITextBlock(new Rectangle(0, 0, 0, 20), statusText,
                    GUI.Style, Alignment.BottomLeft, Alignment.TopCenter, characterFrame, true, GUI.SmallFont).Color = statusColor * 0.7f;

                x += characterFrame.Rect.Width + 10;                
            }

            y += 120;

            if (GameMain.GameSession.Mission != null)
            {
                new GUITextBlock(new Rectangle(0, y, 0, 20), "Mission: " + GameMain.GameSession.Mission.Name, GUI.Style, innerFrame, GUI.LargeFont);
                y += 30;

                new GUITextBlock(new Rectangle(0, y, innerFrame.Rect.Width - 170, 0),
                    (GameMain.GameSession.Mission.Completed) ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    GUI.Style, innerFrame, true);
                //y += 50;

                if (GameMain.GameSession.Mission.Completed && singleplayer)
                {
                    new GUITextBlock(new Rectangle(0, 0, 0, 30), "Reward: " + GameMain.GameSession.Mission.Reward, GUI.Style, Alignment.BottomLeft, Alignment.BottomLeft, innerFrame);
                    //y += 30;
                }  
            }

            return frame;
        }
    }
}
