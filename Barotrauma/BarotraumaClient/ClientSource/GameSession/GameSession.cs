using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class GameSession
    {
        private InfoFrameTab selectedTab;
        private GUIFrame infoFrame;

        private readonly List<GUIButton> tabButtons = new List<GUIButton>();

        private GUIFrame infoFrameContent;
        public RoundSummary RoundSummary { get; private set; }
        public static bool IsInfoFrameOpen => GameMain.GameSession?.infoFrame != null;

        private bool ToggleInfoFrame()
        {
            if (GameMain.NetworkMember != null && GameMain.NetLobbyScreen != null)
            {
                if (GameMain.NetLobbyScreen.HeadSelectionList != null) { GameMain.NetLobbyScreen.HeadSelectionList.Visible = false; }
                if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
            }
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

            tabButtons.Clear();

            infoFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.35f), infoFrame.RectTransform, Anchor.Center) { MinSize = new Point(width, height), RelativeOffset = new Vector2(0.0f, 0.033f) });

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center), style: null);
            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), paddedFrame.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };
            infoFrameContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.85f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.08f) }, style: "InnerFrame");

            var crewButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Crew"), style: "GUITabButton")
            {
                UserData = InfoFrameTab.Crew,
                OnClicked = SelectInfoFrameTab
            };
            tabButtons.Add(crewButton);

            var missionButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("Mission"), style: "GUITabButton")
            {
                UserData = InfoFrameTab.Mission,
                OnClicked = SelectInfoFrameTab
            };
            tabButtons.Add(missionButton);

            if (GameMain.NetworkMember != null)
            {
                var myCharacterButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("MyCharacter"), style: "GUITabButton")
                {
                    UserData = InfoFrameTab.MyCharacter,
                    OnClicked = SelectInfoFrameTab
                };
                tabButtons.Add(myCharacterButton);
            }

            /*TODO: fix
            if (GameMain.Server != null)
            {
                var manageButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform), TextManager.Get("ManagePlayers"))
                {
                    UserData = InfoFrameTab.ManagePlayers,
                    OnClicked = SelectInfoFrameTab
                };
            }*/

        }

        private bool SelectInfoFrameTab(GUIButton button, object userData)
        {
            selectedTab = (InfoFrameTab)userData;

            CreateInfoFrame();
            tabButtons.ForEach(tb => tb.Selected = (InfoFrameTab)tb.UserData == selectedTab);

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                    CrewManager.CreateCrewListFrame(CrewManager.GetCharacters(), infoFrameContent);
                    break;
                case InfoFrameTab.Mission:
                    CreateMissionInfo(infoFrameContent);
                    break;
                case InfoFrameTab.MyCharacter:
                    if (GameMain.NetworkMember == null) { return false; }
                    GameMain.NetLobbyScreen.CreatePlayerFrame(infoFrameContent);
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

            var isTraitor = GameMain.Client?.Character?.IsTraitor ?? false;

            var missionFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, isTraitor ? 0.95f : 0.45f), infoFrameContent.RectTransform))
            {
                RelativeSpacing = 0.05f
            };

            if (Mission != null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionFrame.RectTransform), Mission.Name, font: GUI.LargeFont);

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionFrame.RectTransform), TextManager.GetWithVariable("MissionReward", "[reward]", Mission.Reward.ToString()));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionFrame.RectTransform), Mission.Description, wrap: true);
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionFrame.RectTransform, Anchor.TopCenter), TextManager.Get("NoMission"), font: GUI.LargeFont);
            }
            if (isTraitor)
            {
                var traitorFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.45f), infoFrameContent.RectTransform, Anchor.BottomLeft))
                {
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), traitorFrame.RectTransform), TextManager.Get("Traitors"), font: GUI.LargeFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), traitorFrame.RectTransform), GameMain.Client.Character.TraitorCurrentObjective, wrap: true);
            }
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            GameMode?.AddToGUIUpdateList();
            infoFrame?.AddToGUIUpdateList();

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetLobbyScreen?.HeadSelectionList?.AddToGUIUpdateList();
                GameMain.NetLobbyScreen?.JobSelectionFrame?.AddToGUIUpdateList();
            }
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GUI.DisableHUD) return;

            if (PlayerInput.KeyDown(InputType.InfoTab) && 
                (GUI.KeyboardDispatcher.Subscriber == null || GUI.KeyboardDispatcher.Subscriber is GUIListBox))
            {
                if (infoFrame == null)
                {
                    ToggleInfoFrame();
                }
            }
            else if (infoFrame != null)
            {
                ToggleInfoFrame();
            }

            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetLobbyScreen?.HeadSelectionList != null)
                {
                    if (PlayerInput.PrimaryMouseButtonDown() && !GUI.IsMouseOn(GameMain.NetLobbyScreen.HeadSelectionList))
                    {
                        if (GameMain.NetLobbyScreen.HeadSelectionList != null) { GameMain.NetLobbyScreen.HeadSelectionList.Visible = false; }
                    }
                }
                if (GameMain.NetLobbyScreen?.JobSelectionFrame != null)
                {
                    if (PlayerInput.PrimaryMouseButtonDown() && !GUI.IsMouseOn(GameMain.NetLobbyScreen.JobSelectionFrame))
                    {
                        GameMain.NetLobbyScreen.JobList.Deselect();
                        if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
                    }
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            GameMode?.Draw(spriteBatch);
            //infoFrame?.DrawManually(spriteBatch);
        }
    }
}
