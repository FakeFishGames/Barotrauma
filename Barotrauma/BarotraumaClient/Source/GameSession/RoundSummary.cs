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
            if (!singleplayer)
            {
                SoundPlayer.OverrideMusicType = gameOver ? "crewdead" : "endround";
                SoundPlayer.OverrideMusicDuration = 18.0f;
            }

            GUIFrame frame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");

            int width = 760, height = 400;
            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.4f), frame.RectTransform, Anchor.Center, minSize: new Point(width, height)));
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            GUIListBox infoTextBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), paddedFrame.RectTransform));
            
            string summaryText = TextManager.Get(gameOver ? "RoundSummaryGameOver" :
                (progress ? "RoundSummaryProgress" : "RoundSummaryReturn"));

            summaryText = summaryText
                .Replace("[sub]", Submarine.MainSub.Name)
                .Replace("[location]", progress ? GameMain.GameSession.EndLocation.Name : GameMain.GameSession.StartLocation.Name);

            var infoText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                summaryText, wrap: true);

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform), 
                    endMessage, wrap: true);
            }

            if (GameMain.GameSession.Mission != null)
            {
                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), infoTextBox.Content.RectTransform), style: null);

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                    TextManager.Get("Mission") + ": " + GameMain.GameSession.Mission.Name, font: GUI.LargeFont);

                var missionInfo = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                    GameMain.GameSession.Mission.Completed ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage,
                    wrap: true);

                /*TODO: whoops this definitely does not belong here
                if (GameMain.GameSession.Mission.Completed)
                {
                    GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
                }*/

                if (GameMain.GameSession.Mission.Completed && singleplayer)
                {
                    var missionReward = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                        TextManager.Get("MissionReward").Replace("[reward]", GameMain.GameSession.Mission.Reward.ToString()));
                }  
            }
#if SERVER
            //TODO: fix?
            else
            {
                GameMain.Server?.ConnectedClients.ForEach(c => c.Karma += 0.1f);
            }
#endif

            foreach (GUIComponent child in infoTextBox.Content.Children)
            {
                child.CanBeFocused = false;
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), 
                TextManager.Get("RoundSummaryCrewStatus"), font: GUI.LargeFont);

            GUIListBox characterListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), paddedFrame.RectTransform, minSize: new Point(0, 75)), isHorizontal: true);
                        
            foreach (CharacterInfo characterInfo in gameSession.CrewManager.GetCharacterInfos())
            {
                if (GameMain.GameSession.Mission is CombatMission &&
                    characterInfo.TeamID != GameMain.GameSession.CrewManager.WinningTeam)
                {
                    continue;
                }

                var characterFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1.0f), characterListBox.Content.RectTransform, minSize: new Point(170, 0)))
                {
                    CanBeFocused = false,
                    Stretch = true
                };

                characterInfo.CreateCharacterFrame(characterFrame,
                    characterInfo.Job != null ? (characterInfo.Name + '\n' + "(" + characterInfo.Job.Name + ")") : characterInfo.Name, null);

                string statusText = TextManager.Get("StatusOK");
                Color statusColor = Color.DarkGreen;

                Character character = characterInfo.Character;
                if (character == null || character.IsDead)
                {
                    statusText = characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction ?
                        characterInfo.CauseOfDeath.Affliction.CauseOfDeathDescription :
                        TextManager.Get("CauseOfDeathDescription." + characterInfo.CauseOfDeath.Type.ToString());
                    
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

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), characterFrame.RectTransform, Anchor.BottomCenter),
                    statusText, Color.White,
                    textAlignment: Alignment.Center,
                    wrap: true, font: GUI.SmallFont, style: null, color: statusColor * 0.8f);
            }

            new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.BottomRight)
            {
                RelativeSpacing = 0.05f,
                UserData = "buttonarea"
            };

            return frame;
        }
    }
}
