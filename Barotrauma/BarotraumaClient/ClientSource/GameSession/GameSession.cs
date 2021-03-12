using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class GameSession
    {
        public RoundSummary RoundSummary
        {
            get;
            private set;
        }

        public static bool IsTabMenuOpen => GameMain.GameSession?.tabMenu != null;
        public static TabMenu TabMenuInstance => GameMain.GameSession?.tabMenu;


        private TabMenu tabMenu;

        public bool ToggleTabMenu()
        {
            if (GameMain.NetworkMember != null && GameMain.NetLobbyScreen != null)
            {
                if (GameMain.NetLobbyScreen.HeadSelectionList != null) { GameMain.NetLobbyScreen.HeadSelectionList.Visible = false; }
                if (GameMain.NetLobbyScreen.JobSelectionFrame != null) { GameMain.NetLobbyScreen.JobSelectionFrame.Visible = false; }
            }
            if (tabMenu == null && GameMode is TutorialMode == false)
            {
                tabMenu = new TabMenu();
                HintManager.OnShowTabMenu();
            }
            else
            {
                tabMenu = null;
                NetLobbyScreen.JobInfoFrame = null;
            }

            return true;
        }

        private GUILayoutGroup topLeftButtonGroup;
        private GUIButton crewListButton, commandButton, tabMenuButton;
        private GUILayoutGroup TopLeftButtonGroup
        {
            get
            {
                if (topLeftButtonGroup == null)
                {
                    topLeftButtonGroup = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, GUI.Canvas), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                    {
                        AbsoluteSpacing = HUDLayoutSettings.Padding,
                        CanBeFocused = false
                    };

                    int buttonHeight = GUI.IntScale(40);
                    Vector2 buttonSpriteSize = GUI.Style.GetComponentStyle("CrewListToggleButton").GetDefaultSprite().size;
                    int buttonWidth = (int)((buttonHeight / buttonSpriteSize.Y) * buttonSpriteSize.X);
                    Point buttonSize = new Point(buttonWidth, buttonHeight);
                    // Crew list button
                    crewListButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "CrewListToggleButton")
                    {
                        ToolTip = TextManager.GetWithVariable("hudbutton.crewlist", "[key]", GameMain.Config.KeyBindText(InputType.CrewOrders)),
                        OnClicked = (GUIButton btn, object userdata) =>
                        {
                            if (CrewManager == null) { return false; }
                            CrewManager.IsCrewMenuOpen = !CrewManager.IsCrewMenuOpen;
                            return true;
                        }
                    };
                    // Command interface button
                    commandButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform),style: "CommandButton")
                    {
                        ToolTip = TextManager.GetWithVariable("hudbutton.commandinterface", "[key]", GameMain.Config.KeyBindText(InputType.Command)),
                        OnClicked = (button, userData) =>
                        {
                            if (CrewManager == null) { return false; }
                            CrewManager.ToggleCommandUI();
                            return true;
                        }
                    };
                    // Tab menu button
                    tabMenuButton = new GUIButton(new RectTransform(buttonSize, parent: topLeftButtonGroup.RectTransform), style: "TabMenuButton")
                    {
                        ToolTip = TextManager.GetWithVariable("hudbutton.tabmenu", "[key]", GameMain.Config.KeyBindText(InputType.InfoTab)),
                        OnClicked = (button, userData) =>
                        {
                            return ToggleTabMenu();
                        }
                    };
                }
                return topLeftButtonGroup;
            }
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }
            GameMode?.AddToGUIUpdateList();
            tabMenu?.AddToGUIUpdateList();

            if ((!(GameMode is CampaignMode campaign) || (!campaign.ForceMapUI && !campaign.ShowCampaignUI)) &&
                !CoroutineManager.IsCoroutineRunning("LevelTransition") && !CoroutineManager.IsCoroutineRunning("SubmarineTransition"))
            {
                if (crewListButton != null)
                {
                    crewListButton.Selected = CrewManager != null && CrewManager.IsCrewMenuOpen;
                }
                if (commandButton != null)
                {
                    commandButton.Selected = CrewManager.IsCommandInterfaceOpen;
                    commandButton.Enabled = CrewManager.CanIssueOrders;
                }
                if (tabMenuButton != null)
                {
                    tabMenuButton.Selected = IsTabMenuOpen;
                }
                TopLeftButtonGroup.AddToGUIUpdateList();
            }

            if (GameMain.NetworkMember != null)
            {
                GameMain.NetLobbyScreen?.HeadSelectionList?.AddToGUIUpdateList();
                GameMain.NetLobbyScreen?.JobSelectionFrame?.AddToGUIUpdateList();
            }
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (GUI.DisableHUD) { return; }

            if (tabMenu == null)
            {
                if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
                {
                    ToggleTabMenu();
                }
            }
            else
            {
                tabMenu.Update();

                if (PlayerInput.KeyHit(InputType.InfoTab) && GUI.KeyboardDispatcher.Subscriber is GUITextBox == false)
                {
                    ToggleTabMenu();
                }
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

            HintManager.Update();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            GameMode?.Draw(spriteBatch);
        }
    }
}
