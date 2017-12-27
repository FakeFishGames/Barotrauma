using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    class RoundSummary
    {
        private Location startLocation, endLocation;

        private GameSession gameSession;

        private Mission selectedMission;
               
        public RoundSummary(GameSession gameSession)
        {
            this.gameSession = gameSession;

            startLocation   = gameSession.StartLocation;
            endLocation     = gameSession.EndLocation;
            
            selectedMission = gameSession.Mission;
        }
        

        public GUIFrame CreateSummaryFrame(string endMessage)
        {
            bool singleplayer = GameMain.NetworkMember == null;

            bool gameOver = gameSession.CrewManager.GetCharacters().All(c => c.IsDead);
            bool progress = Submarine.MainSub.AtEndPosition;
            
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.8f, null);
            
            int width = 760, height = 400;
            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, "", frame);
            
            int y = 0;
            
            if (singleplayer)
            {
                string summaryText = TextManager.Get(gameOver ? "RoundSummaryGameOver" :
                    (progress ? "RoundSummaryProgress" : "RoundSummaryReturn"));

                summaryText.Replace("[sub]", Submarine.MainSub.Name);
                summaryText.Replace("[location]", GameMain.GameSession.StartLocation.Name);

                var infoText = new GUITextBlock(new Rectangle(0, y, 0, 50), summaryText, "", innerFrame, true);
                y += infoText.Rect.Height;
            }


            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new Rectangle(0, y, 0, 30), endMessage, "", innerFrame, true);

                y += 30 + endText.Text.Split('\n').Length * 20;
            }

            new GUITextBlock(new Rectangle(0, y, 0, 20), TextManager.Get("RoundSummaryCrewStatus"), "", innerFrame, GUI.LargeFont);
            y += 30;

            GUIListBox listBox = new GUIListBox(new Rectangle(0,y,0,90), null, Alignment.TopLeft, "", innerFrame, true);

            int x = 0;
            foreach (CharacterInfo characterInfo in gameSession.CrewManager.GetCharacterInfos())
            {
                if (GameMain.GameSession.Mission is CombatMission &&
                    characterInfo.TeamID != GameMain.GameSession.CrewManager.WinningTeam)
                {
                    continue;
                }

                var characterFrame = new GUIFrame(new Rectangle(x, y, 170, 70), Color.Transparent, "", listBox);
                characterFrame.OutlineColor = Color.Transparent;
                characterFrame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                characterFrame.CanBeFocused = false;

                characterInfo.CreateCharacterFrame(characterFrame,
                    characterInfo.Job != null ? (characterInfo.Name + '\n' + "(" + characterInfo.Job.Name + ")") : characterInfo.Name, null);
                
                string statusText = TextManager.Get("StatusOK");
                Color statusColor = Color.DarkGreen;

                Character character = characterInfo.Character;
                if (character == null || character.IsDead)
                {
                    statusText = TextManager.Get("CauseOfDeath." + characterInfo.CauseOfDeath.ToString());
                    statusColor = Color.DarkRed;
                }
                else
                {
                    if (character.IsUnconscious)
                    {
                        statusText = TextManager.Get("Unconscious");
                        statusColor = Color.DarkOrange;
                    }
                    else if (character.Health / character.MaxHealth < 0.8f)
                    {
                        statusText = TextManager.Get("Injured");
                        statusColor = Color.DarkOrange;
                    }
                }

                new GUITextBlock(
                    new Rectangle(0, 0, 0, 20), statusText, statusColor * 0.8f, Color.White,
                    Alignment.BottomLeft, Alignment.Center,
                    null, characterFrame, true, GUI.SmallFont);

                x += characterFrame.Rect.Width + 10;                
            }

            y += 120;

            if (GameMain.GameSession.Mission != null)
            {
                new GUITextBlock(new Rectangle(0, y, 0, 20), TextManager.Get("Mission") + ": " + GameMain.GameSession.Mission.Name, "", innerFrame, GUI.LargeFont);
                y += 30;

                new GUITextBlock(new Rectangle(0, y, innerFrame.Rect.Width - 170, 0),
                    (GameMain.GameSession.Mission.Completed) ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    "", innerFrame, true);

                if (GameMain.GameSession.Mission.Completed)
                {
                    GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
                }

                if (GameMain.GameSession.Mission.Completed && singleplayer)
                {
                    new GUITextBlock(new Rectangle(0, 0, 0, 30), TextManager.Get("Reward") + ": " + GameMain.GameSession.Mission.Reward, "", Alignment.BottomLeft, Alignment.BottomLeft, innerFrame);
                }  
            }
            else
            {
                GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
            }

            return frame;
        }
    }
}
