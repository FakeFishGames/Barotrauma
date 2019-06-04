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

            int width = 760, height = 500;
            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.5f), frame.RectTransform, Anchor.Center, minSize: new Point(width, height)));
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            GUIListBox infoTextBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), paddedFrame.RectTransform));
            
            string summaryText = TextManager.GetWithVariables(gameOver ? "RoundSummaryGameOver" :
                (progress ? "RoundSummaryProgress" : "RoundSummaryReturn"), new string[2] { "[sub]", "[location]" },
                new string[2] { Submarine.MainSub.Name, progress ? GameMain.GameSession.EndLocation.Name : GameMain.GameSession.StartLocation.Name });

            var infoText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                summaryText, wrap: true);

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform), 
                    endMessage, wrap: true);
            }

            //don't show the mission info if the mission was not completed and there's no localized "mission failed" text available
            if (GameMain.GameSession.Mission != null)
            {
                string message = GameMain.GameSession.Mission.Completed ? GameMain.GameSession.Mission.SuccessMessage : GameMain.GameSession.Mission.FailureMessage;
                if (!string.IsNullOrEmpty(message))
                {
                    //spacing
                    new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), infoTextBox.Content.RectTransform), style: null);

                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                       TextManager.AddPunctuation(':', TextManager.Get("Mission"), GameMain.GameSession.Mission.Name),
                       font: GUI.LargeFont);

                    var missionInfo = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                        message, wrap: true);
                
                    if (GameMain.GameSession.Mission.Completed && singleplayer)
                    {
                        var missionReward = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoTextBox.Content.RectTransform),
                            TextManager.GetWithVariable("MissionReward", "[reward]", GameMain.GameSession.Mission.Reward.ToString()));
                    }  
                }
            }

            foreach (GUIComponent child in infoTextBox.Content.Children)
            {
                child.CanBeFocused = false;
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), 
                TextManager.Get("RoundSummaryCrewStatus"), font: GUI.LargeFont);

            GUIListBox characterListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), paddedFrame.RectTransform, minSize: new Point(0, 75)), isHorizontal: true);
                        
            foreach (CharacterInfo characterInfo in gameSession.CrewManager.GetCharacterInfos())
            {
                if (GameMain.GameSession.Mission is CombatMission &&
                    characterInfo.TeamID != GameMain.GameSession.WinningTeam)
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
                    if (characterInfo.CauseOfDeath == null)
                    {
                        statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    }
                    else if (characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction && characterInfo.CauseOfDeath.Affliction == null)
                    {
                        string errorMsg = "Character \"" + character.Name + "\" had an invalid cause of death (the type of the cause of death was Affliction, but affliction was not specified).";
                        DebugConsole.ThrowError(errorMsg);
                        GameAnalyticsManager.AddErrorEventOnce("RoundSummary:InvalidCauseOfDeath", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                        statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    }
                    else
                    {
                        statusText = characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction ?
                            characterInfo.CauseOfDeath.Affliction.CauseOfDeathDescription :
                            TextManager.Get("CauseOfDeathDescription." + characterInfo.CauseOfDeath.Type.ToString());
                    }
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

                var textHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), characterFrame.RectTransform, Anchor.BottomCenter), style: "InnerGlow", color: statusColor);
                new GUITextBlock(new RectTransform(Vector2.One, textHolder.RectTransform, Anchor.Center),
                    statusText, Color.White,
                    textAlignment: Alignment.Center,
                    wrap: true, font: GUI.SmallFont, style: null);
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
