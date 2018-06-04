using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class GameSession
    {
        private InfoFrameTab selectedTab;
        private GUIButton infoButton;
        private GUIFrame infoFrame;

        private GUIFrame infoFrameContent;

        private RoundSummary roundSummary;
        public RoundSummary RoundSummary
        {
            get { return roundSummary; }
        }

        private bool ToggleInfoFrame(GUIButton button, object obj)
        {
            if (infoFrame == null)
            {
                CreateInfoFrame();
                SelectInfoFrameTab(null, selectedTab);
            }
            else
            {
                infoFrame = null;
            }

            return true;
        }

        public void CreateInfoFrame()
        {
            int width = 600, height = 400;
            
            infoFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style:null, color: Color.Black * 0.8f);

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.35f), infoFrame.RectTransform, Anchor.Center) { MinSize = new Point(width,height) });

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center), style:null);
            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };
            infoFrameContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.08f) }, style: "InnerFrame");

            var crewButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Crew"))
            {
                ClampMouseRectToParent = false,
                UserData = InfoFrameTab.Crew,
                OnClicked = SelectInfoFrameTab
            };

            var missionButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Mission"))
            {
                ClampMouseRectToParent = false,
                UserData = InfoFrameTab.Mission,
                OnClicked = SelectInfoFrameTab
            };

            if (GameMain.Server != null)
            {
                var manageButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("ManagePlayers"))
                {
                    ClampMouseRectToParent = false,
                    UserData = InfoFrameTab.ManagePlayers,
                    OnClicked = SelectInfoFrameTab
                };
            }

            var closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.08f), paddedFrame.RectTransform, Anchor.BottomRight), TextManager.Get("Close"))
            {
                OnClicked = ToggleInfoFrame
            };

        }

        private bool SelectInfoFrameTab(GUIButton button, object userData)
        {
            selectedTab = (InfoFrameTab)userData;

            CreateInfoFrame();

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                    CrewManager.CreateCrewListFrame(CrewManager.GetCharacters(), infoFrameContent);
                    break;
                case InfoFrameTab.Mission:
                    CreateMissionInfo(infoFrameContent);
                    break;
                case InfoFrameTab.ManagePlayers:
                    GameMain.Server.ManagePlayersFrame(infoFrameContent);
                    break;
            }

            return true;
        }

        private void CreateMissionInfo(GUIFrame infoFrame)
        {
            infoFrameContent.ClearChildren();

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), infoFrameContent.RectTransform))
            {
                RelativeSpacing = 0.05f
            };

            if (Mission == null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform, Anchor.TopCenter), TextManager.Get("NoMission"));
                return;
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), Mission.Name, font: GUI.LargeFont);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform), TextManager.Get("MissionReward").Replace("[reward]", Mission.Reward.ToString()));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), Mission.Description, wrap: true);
        }

        public void AddToGUIUpdateList()
        {
            infoButton.AddToGUIUpdateList();

            if (GameMode != null) GameMode.AddToGUIUpdateList();

            if (infoFrame != null) infoFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            EventManager.Update(deltaTime);

            if (GUI.DisableHUD) return;

            //guiRoot.Update(deltaTime);
            infoButton.UpdateManually(deltaTime);

            if (GameMode != null) GameMode.Update(deltaTime);
            if (Mission != null) Mission.Update(deltaTime);
            if (infoFrame != null) infoFrame.UpdateManually(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            infoButton.DrawManually(spriteBatch);

            if (GameMode != null) GameMode.Draw(spriteBatch);
            if (infoFrame != null) infoFrame.DrawManually(spriteBatch);
        }
    }
}
