using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class GameSession
    {
        private InfoFrameTab selectedTab;
        private GUIButton infoButton;
        public GUIButton ingameInfoButton;
        private GUIFrame infoFrame;
        public static InGameInfo inGameInfo;

        private RoundSummary roundSummary;
        public RoundSummary RoundSummary
        {
            get { return roundSummary; }
        }

        private bool ToggleInfoFrame(GUIButton button, object obj)
        {
            if (infoFrame == null)
            {
                if (CrewManager != null && CrewManager.CrewCommander != null && CrewManager.CrewCommander.IsOpen)
                {
                    CrewManager.CrewCommander.ToggleGUIFrame();
                }

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


            infoFrame = new GUIFrame(
                Rectangle.Empty, Color.Black * 0.8f, null);

            var innerFrame = new GUIFrame(
                new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), "", infoFrame);

            innerFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            var crewButton = new GUIButton(new Rectangle(0, -30, 100, 20), "Crew", "", innerFrame);
            crewButton.UserData = InfoFrameTab.Crew;
            crewButton.OnClicked = SelectInfoFrameTab;

            var missionButton = new GUIButton(new Rectangle(100, -30, 100, 20), "Mission", "", innerFrame);
            missionButton.UserData = InfoFrameTab.Mission;
            missionButton.OnClicked = SelectInfoFrameTab;

            if (GameMain.Server != null)
            {
                var manageButton = new GUIButton(new Rectangle(200, -30, 130, 20), "Manage players", "", innerFrame);
                manageButton.UserData = InfoFrameTab.ManagePlayers;
                manageButton.OnClicked = SelectInfoFrameTab;
            }

            var closeButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Close", Alignment.BottomCenter, "", innerFrame);
            closeButton.OnClicked = ToggleInfoFrame;

        }

        private bool SelectInfoFrameTab(GUIButton button, object userData)
        {
            selectedTab = (InfoFrameTab)userData;

            CreateInfoFrame();

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                    CrewManager.CreateCrewFrame(CrewManager.GetCharacters(), infoFrame.children[0] as GUIFrame);
                    break;
                case InfoFrameTab.Mission:
                    CreateMissionInfo(infoFrame.children[0] as GUIFrame);
                    break;
                case InfoFrameTab.ManagePlayers:
                    GameMain.Server.ManagePlayersFrame(infoFrame.children[0] as GUIFrame);
                    break;
            }

            return true;
        }

        private void CreateMissionInfo(GUIFrame infoFrame)
        {
            if (Mission == null)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 50), "No mission", "", infoFrame, true);
                return;
            }

            new GUITextBlock(new Rectangle(0, 0, 0, 40), Mission.Name, "", infoFrame, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 50, 0, 20), "Reward: " + Mission.Reward, "", infoFrame, true);
            new GUITextBlock(new Rectangle(0, 70, 0, 50), Mission.Description, "", infoFrame, true);


        }

        public void AddToGUIUpdateList()
        {
            infoButton.AddToGUIUpdateList();
            ingameInfoButton.AddToGUIUpdateList();
            inGameInfo.AddToGUIUpdateList();

            if (GameMode != null) GameMode.AddToGUIUpdateList();

            if (infoFrame != null) infoFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            EventManager.Update(deltaTime);

            if (GUI.DisableHUD) return;

            if (GameMain.Client != null)
            {
                if(GameMain.Client.HasPermission(Networking.ClientPermissions.Kick) || GameMain.Client.HasPermission(Networking.ClientPermissions.Ban))
                {
                    //ingameInfoButton.Visible = true;
                    ingameInfoButton.Visible = false;
                }
                else
                {
                    ingameInfoButton.Visible = false;
                }
            }

            //if (GameMain.Server == null) ingameInfoButton.Visible = false;

            //guiRoot.Update(deltaTime);
            infoButton.Update(deltaTime);
            ingameInfoButton.Update(deltaTime);


            if (GameMode != null) GameMode.Update(deltaTime);
            if (Mission != null) Mission.Update(deltaTime);
            if (infoFrame != null)
            {
                infoFrame.Update(deltaTime);

                if (CrewManager != null && CrewManager.CrewCommander != null && CrewManager.CrewCommander.IsOpen)
                {
                    infoFrame = null;
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;

            infoButton.Draw(spriteBatch);
            ingameInfoButton.Draw(spriteBatch);
            inGameInfo.Draw(spriteBatch);

            if (GameMode != null) GameMode.Draw(spriteBatch);
            if (infoFrame != null) infoFrame.Draw(spriteBatch);
        }
    }
}
