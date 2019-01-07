using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class GameSession
    {
        private InfoFrameTab selectedTab;
        private GUIButton infoButton;
        private GUIButton infoFrame;

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

            infoFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) ToggleInfoFrame(btn, userdata); return true; }
            };


            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.35f), infoFrame.RectTransform, Anchor.Center) { MinSize = new Point(width,height) });

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center), style:null);
            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };
            infoFrameContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.08f) }, style: "InnerFrame");

            var crewButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Crew"))
            {
                UserData = InfoFrameTab.Crew,
                OnClicked = SelectInfoFrameTab
            };

            var missionButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Mission"))
            {
                UserData = InfoFrameTab.Mission,
                OnClicked = SelectInfoFrameTab
            };

            /*TODO: fix
            if (GameMain.Server != null)
            {
                var manageButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("ManagePlayers"))
                {
                    UserData = InfoFrameTab.ManagePlayers,
                    OnClicked = SelectInfoFrameTab
                };
            }*/

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
                    //TODO: fix
                    //GameMain.Server.ManagePlayersFrame(infoFrameContent);
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
            if (GUI.DisableHUD) return;
            infoButton.AddToGUIUpdateList();
            GameMode?.AddToGUIUpdateList();
            infoFrame?.AddToGUIUpdateList();
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GUI.DisableHUD) return;
            
            infoButton?.UpdateManually(deltaTime);
            infoFrame?.UpdateManually(deltaTime);
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            infoButton.DrawManually(spriteBatch);

            GameMode?.Draw(spriteBatch);
            infoFrame?.DrawManually(spriteBatch);
        }
    }
}
