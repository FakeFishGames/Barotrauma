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
            }
            else
            {
                tabMenu = null;
                NetLobbyScreen.JobInfoFrame = null;
            }

            return true;
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            GameMode?.AddToGUIUpdateList();
            tabMenu?.AddToGUIUpdateList();

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
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            GameMode?.Draw(spriteBatch);
        }
    }
}
