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

            bool gameOver = gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsUnconscious);
            bool progress = Submarine.MainSub.AtEndPosition;
            
            GUIFrame frame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.8f, null);
            
            int width = 760, height = 400;
            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, "", frame);

            GUIListBox listBox = new GUIListBox(new Rectangle(0, 0, 0, height - (int)(30 + innerFrame.Padding.Y + innerFrame.Padding.W)), "", innerFrame);
                        
            if (!singleplayer)
            {
                SoundPlayer.OverrideMusicType = gameOver ? "crewdead" : "endround";
            }

            string summaryText = TextManager.Get(gameOver ? "RoundSummaryGameOver" :
                (progress ? "RoundSummaryProgress" : "RoundSummaryReturn"));

            summaryText = summaryText
                .Replace("[sub]", Submarine.MainSub.Name)
                .Replace("[location]", progress ? GameMain.GameSession.EndLocation.Name : GameMain.GameSession.StartLocation.Name);

            var infoText = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width - 20, 0), summaryText, "", null, true);
            infoText.Rect = new Rectangle(0, 0, infoText.Rect.Width, infoText.Rect.Height + 20);
            listBox.AddChild(infoText);

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width - 20, 0), endMessage, "", null, true);
                endText.Rect = new Rectangle(0, 0, endText.Rect.Width, endText.Rect.Height + 20);
                listBox.AddChild(endText);
            }

            if (GameMain.GameSession.Mission != null)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 40), TextManager.Get("Mission") + ": " + GameMain.GameSession.Mission.Name, "", listBox, GUI.LargeFont);

                var missionInfo = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width-20, 0),
                    (GameMain.GameSession.Mission.Completed) ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    "", null, true);
                missionInfo.Rect = new Rectangle(0, 0, missionInfo.Rect.Width, missionInfo.Rect.Height + 20);
                listBox.AddChild(missionInfo);

                if (GameMain.GameSession.Mission.Completed)
                {
                    GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
                }

                if (GameMain.GameSession.Mission.Completed && singleplayer)
                {
                    var missionReward = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width-20, 0), TextManager.Get("Reward") + ": " + GameMain.GameSession.Mission.Reward, "", Alignment.BottomLeft, Alignment.BottomLeft, null);
                    missionReward.Rect = new Rectangle(0, 0, missionReward.Rect.Width, missionReward.Rect.Height + 20);
                    listBox.AddChild(missionReward);
                }  
            }
            else
            {
                GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
            }


            new GUITextBlock(new Rectangle(0, 0, 0, 40), TextManager.Get("RoundSummaryCrewStatus"), "", listBox, GUI.LargeFont);

            GUIListBox characterListBox = new GUIListBox(new Rectangle(0, 0, listBox.Rect.Width - 20, 90), null, Alignment.TopLeft, "", null, true);
            listBox.AddChild(characterListBox);

            int x = 0;
            foreach (CharacterInfo characterInfo in gameSession.CrewManager.GetCharacterInfos())
            {
                if (GameMain.GameSession.Mission is CombatMission &&
                    characterInfo.TeamID != GameMain.GameSession.CrewManager.WinningTeam)
                {
                    continue;
                }

                var characterFrame = new GUIFrame(new Rectangle(x, 0, 170, 70), Color.Transparent, "", characterListBox);
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
                    statusText = characterInfo.CauseOfDeath.First == CauseOfDeathType.Affliction ?
                        characterInfo.CauseOfDeath.Second.CauseOfDeathDescription :
                        TextManager.Get("Self_CauseOfDeathDescription." + characterInfo.CauseOfDeath.First.ToString());

                    string chatMessage = characterInfo.CauseOfDeath.First == CauseOfDeathType.Affliction ?
                        characterInfo.CauseOfDeath.Second.CauseOfDeathDescription :
                        TextManager.Get("Self_CauseOfDeathDescription." + characterInfo.CauseOfDeath.First.ToString());

                    statusColor = Color.DarkRed;
                }
                else
                {
                    if (character.IsUnconscious)
                    {
                        statusText = TextManager.Get("Unconscious");
                        statusColor = Color.DarkOrange;
                    }
                    else if (character.Vitality / character.MaxVitality < 0.8f)
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


            return frame;
        }
    }
}
