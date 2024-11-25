using Microsoft.Xna.Framework;
namespace Barotrauma
{
    partial class PvPMode : MissionMode
    {
        private GUIComponent scoreContainer;
        private readonly GUITextBlock[] scoreTexts = new GUITextBlock[2];
        private readonly GUITextBlock[] scoreTextShadows = new GUITextBlock[2];
        private readonly int[] prevScores = new int[2];

        private void InitUI()
        {
            scoreContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.TutorialObjectiveListArea, GUI.Canvas), childAnchor: Anchor.TopRight)
            {
                CanBeFocused = false
            };
            for (int i = 0; i < 2; i++)
            {
                var frame = new GUIFrame(new RectTransform(new Point(scoreContainer.Rect.Width, GUI.IntScale(80)), scoreContainer.RectTransform), style: null)
                {
                    CanBeFocused = false
                };
                new GUIImage(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight), style: i == 0 ? "CoalitionIcon" : "SeparatistIcon")
                {
                    CanBeFocused = false
                };
                scoreTextShadows[i] = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point(GUI.IntScale(38), GUI.IntScale(2)) },
                    string.Empty, textColor: GUIStyle.TextColorDark, textAlignment: Alignment.CenterRight, font: GUIStyle.SubHeadingFont)
                {
                    CanBeFocused = false
                };
                scoreTexts[i] = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point(GUI.IntScale(40), 0) }, 
                    string.Empty, textAlignment: Alignment.CenterRight, font: GUIStyle.SubHeadingFont)
                {
                    CanBeFocused = false
                };
            }
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();

            if (scoreContainer == null) { InitUI(); }

            scoreContainer.Visible = false;
            foreach (var mission in Missions)
            {
                if (mission is CombatMission combatMission && combatMission.HasWinScore)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var scoreText = scoreTexts[i];
                        //one team very close to the win score, start flashing the score
                        if (combatMission.Scores[i] > combatMission.WinScore * 0.9f ||
                            combatMission.Scores[i] == combatMission.WinScore - 1)
                        {
                            if (scoreText.Parent.FlashTimer <= 0.0f)
                            {
                                scoreText.Parent.Flash(GUIStyle.Orange);
                                scoreText.Pulsate(Vector2.One, Vector2.One * 1.2f, scoreText.Parent.FlashTimer);
                            }
                        }
                        if (prevScores[i] != combatMission.Scores[i] || scoreText.Text.IsNullOrEmpty())
                        {
                            scoreText.Text =  scoreTextShadows[i].Text = $"{combatMission.Scores[i]}/{combatMission.WinScore}";                          
                            scoreText.Parent.Flash(GUIStyle.Green);
                            scoreText.Parent.GetAnyChild<GUIImage>().Pulsate(Vector2.One, Vector2.One * 1.2f, scoreText.Parent.FlashTimer);
                            SoundPlayer.PlayUISound(GUISoundType.UIMessage);
                        }
                        scoreText.Parent.RectTransform.NonScaledSize =
                            new Point(
                                (int)(scoreText.TextSize.X + scoreText.Padding.X + scoreText.Padding.X) + scoreText.Parent.GetChild<GUIImage>().Rect.Width + GUI.IntScale(10),
                                scoreText.Parent.Rect.Height);
                        scoreText.Parent.ForceLayoutRecalculation();
                        prevScores[i] = combatMission.Scores[i];
                    }
                    scoreContainer.Visible = true;
                }
            }
            scoreContainer.AddToGUIUpdateList();
        }
    }
}
