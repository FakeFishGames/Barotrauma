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

            GUIFrame frame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Black * 0.8f);

            int width = 760, height = 400;
            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), frame.RectTransform, Anchor.Center, minSize: new Point(width, height)));
            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center));

            if (!singleplayer)
            {
                SoundPlayer.OverrideMusicType = gameOver ? "crewdead" : "endround";
                SoundPlayer.OverrideMusicDuration = 18.0f;
            }

            string summaryText = TextManager.Get(gameOver ? "RoundSummaryGameOver" :
                (progress ? "RoundSummaryProgress" : "RoundSummaryReturn"));

            summaryText = summaryText
                .Replace("[sub]", Submarine.MainSub.Name)
                .Replace("[location]", progress ? GameMain.GameSession.EndLocation.Name : GameMain.GameSession.StartLocation.Name);

            var infoText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                summaryText, wrap: true);

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform), 
                    endMessage, wrap: true);
            }

            if (GameMain.GameSession.Mission != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                    TextManager.Get("Mission") + ": " + GameMain.GameSession.Mission.Name, font: GUI.LargeFont);

                var missionInfo = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                    GameMain.GameSession.Mission.Completed ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    wrap: true);

                if (GameMain.GameSession.Mission.Completed)
                {
                    GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
                }

                if (GameMain.GameSession.Mission.Completed && singleplayer)
                {
                    var missionReward = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                        TextManager.Get("Reward") + ": " + GameMain.GameSession.Mission.Reward);
                }  
            }
            else
            {
                GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform), 
                TextManager.Get("RoundSummaryCrewStatus"), font: GUI.LargeFont);

            GUIListBox characterListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), listBox.Content.RectTransform, minSize: new Point(0, 75)), isHorizontal: true);
            foreach (GUIComponent child in listBox.Content.Children)
            {
                child.CanBeFocused = child == characterListBox;
            }
            
            foreach (CharacterInfo characterInfo in gameSession.CrewManager.GetCharacterInfos())
            {
                if (GameMain.GameSession.Mission is CombatMission &&
                    characterInfo.TeamID != GameMain.GameSession.CrewManager.WinningTeam)
                {
                    continue;
                }

                var characterFrame = new GUIFrame(new RectTransform(new Vector2(0.2f, 1.0f), characterListBox.Content.RectTransform, minSize: new Point(170, 0)), style: null)
                {
                    CanBeFocused = false
                };

                characterInfo.CreateCharacterFrame(characterFrame,
                    characterInfo.Job != null ? (characterInfo.Name + '\n' + "(" + characterInfo.Job.Name + ")") : characterInfo.Name, null);

                string statusText = TextManager.Get("StatusOK");
                Color statusColor = Color.DarkGreen;

                Character character = characterInfo.Character;
                if (character == null || character.IsDead)
                {
                    statusText = characterInfo.CauseOfDeath.First == CauseOfDeathType.Affliction ?
                        characterInfo.CauseOfDeath.Second.CauseOfDeathDescription :
                        TextManager.Get("CauseOfDeathDescription." + characterInfo.CauseOfDeath.First.ToString());
                    
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

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), characterFrame.RectTransform, Anchor.BottomCenter),
                    statusText, Color.White,
                    textAlignment: Alignment.Center,
                    wrap: true, font: GUI.SmallFont, color: statusColor * 0.8f);
            }


            return frame;
        }
    }
}
