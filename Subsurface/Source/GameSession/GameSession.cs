using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma
{
    class GameSession
    {
        public enum InfoFrameTab { Crew, Mission, ManagePlayers };

        public readonly TaskManager TaskManager;
        
        public readonly GameMode gameMode;

        private InfoFrameTab selectedTab;
        private GUIButton infoButton;
        private GUIFrame infoFrame;
        
        private string saveFile;

        private Submarine submarine;

        public CrewManager CrewManager;
        
        private ShiftSummary shiftSummary;

        private Mission currentMission;

        public Mission Mission
        {
            get
            {
                return currentMission;
            }
        }

        private Level level;

        public Level Level
        {
            get { return level; }
        }

        public Map Map
        {
            get 
            {
                SinglePlayerMode mode = (gameMode as SinglePlayerMode);
                return (mode == null) ? null : mode.Map;
            }
        }
                
        public Submarine Submarine
        {
            get { return submarine; }
            set { submarine = value; }
        }

        public string SaveFile
        {
            get { return saveFile; }
        }

        public ShiftSummary ShiftSummary
        {
            get { return shiftSummary; }
        }

        public GameSession(Submarine submarine, string saveFile, GameModePreset gameModePreset = null, string missionType="")
        {
            Submarine.MainSub = submarine;

            GameMain.GameSession = this;

            CrewManager = new CrewManager();

            TaskManager = new TaskManager(this);

            this.saveFile = saveFile;

            //guiRoot = new GUIFrame(new Rectangle(0,0,GameMain.GraphicsWidth,GameMain.GraphicsWidth), Color.Transparent);

            infoButton = new GUIButton(new Rectangle(10, 10, 100, 20), "Info", GUI.Style, null);
            infoButton.OnClicked = ToggleInfoFrame;

            if (gameModePreset!=null) gameMode = gameModePreset.Instantiate(missionType);
            this.submarine = submarine;
        }
        
        public GameSession(Submarine selectedSub, string saveFile, XDocument doc)
            : this(selectedSub, saveFile)
        {
            Submarine.MainSub = submarine;

            GameMain.GameSession = this;

            CrewManager = new CrewManager();

            selectedSub.Name = ToolBox.GetAttributeString(doc.Root, "submarine", selectedSub.Name);

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "gamemode") continue;

                gameMode = new SinglePlayerMode(subElement);
            }
        }

        public void StartShift(string levelSeed)
        {
            Level level = Level.CreateRandom(levelSeed);

            StartShift(level);
        }

        public void StartShift(Level level, bool reloadSub = true)
        {
            GameMain.LightManager.LosEnabled = (GameMain.Server==null || GameMain.Server.CharacterInfo!=null);
                        
            this.level = level;

            if (submarine==null)
            {
                DebugConsole.ThrowError("Couldn't start game session, submarine not selected");
                return;
            }
            
            if (reloadSub || Submarine.MainSub != submarine) submarine.Load(true);
            Submarine.MainSub = submarine;

            //var secondSub = new Submarine(submarine.FilePath, submarine.MD5Hash.Hash);
            //secondSub.Load(false);

            if (level != null)
            {
                level.Generate();

                submarine.SetPosition(level.StartPosition - new Vector2(0.0f, 2000.0f));

                //secondSub.SetPosition(level.EndPosition - new Vector2(0.0f, 2000.0f));

                GameMain.GameScreen.BackgroundCreatureManager.SpawnSprites(80);
            }

            if (gameMode.Mission != null)
            {
                currentMission = gameMode.Mission;
            }

            shiftSummary = new ShiftSummary(this);

            if (gameMode!=null) gameMode.Start();

            if (gameMode.Mission != null) Mission.Start(Level.Loaded);
                        
            TaskManager.StartShift(level);

            GameMain.GameScreen.ColorFade(Color.Black, Color.TransparentBlack, 5.0f);
            SoundPlayer.SwitchMusic();
        }

        public void EndShift(string endMessage)
        {
            if (Mission != null) Mission.End();

            if (GameMain.Server != null)
            {
                //CoroutineManager.StartCoroutine(GameMain.Server.EndGame(endMessage));

            }
            else if (GameMain.Client == null)
            {
                //Submarine.Unload();
                GameMain.LobbyScreen.Select();
            }

            if (shiftSummary != null)
            {
                GUIFrame summaryFrame = shiftSummary.CreateSummaryFrame(endMessage);
                GUIMessageBox.MessageBoxes.Enqueue(summaryFrame);
                var okButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Ok", Alignment.BottomRight, GUI.Style, summaryFrame.children[0]);
                okButton.OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Dequeue(); return true; };
            }

            TaskManager.EndShift();

            currentMission = null;

            StatusEffect.StopAll();
        }
        
        
        public void KillCharacter(Character character)
        {
            CrewManager.KillCharacter(character);
        }

        public void ReviveCharacter(Character character)
        {
            CrewManager.ReviveCharacter(character);
        }

        public bool LoadPrevious(GUIButton button, object obj)
        {
            Submarine.Unload();

            SaveUtil.LoadGame(saveFile);

            GameMain.LobbyScreen.Select();

            return true;
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

            infoFrame = new GUIFrame(
                new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);

            infoFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            var crewButton = new GUIButton(new Rectangle(0, -30, 100, 20), "Crew", GUI.Style, infoFrame);
            crewButton.UserData = InfoFrameTab.Crew;
            crewButton.OnClicked = SelectInfoFrameTab;

            var missionButton = new GUIButton(new Rectangle(100, -30, 100, 20), "Mission", GUI.Style, infoFrame);
            missionButton.UserData = InfoFrameTab.Mission;
            missionButton.OnClicked = SelectInfoFrameTab;

            if (GameMain.Server != null)
            {
                var manageButton = new GUIButton(new Rectangle(200, -30, 100, 20), "Manage players", GUI.Style, infoFrame);
                manageButton.UserData = InfoFrameTab.ManagePlayers;
                manageButton.OnClicked = SelectInfoFrameTab;
            }

            var closeButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Close", Alignment.BottomCenter, GUI.Style, infoFrame);
            closeButton.OnClicked = ToggleInfoFrame;

        }

        private bool SelectInfoFrameTab(GUIButton button, object userData)
        {
            selectedTab = (InfoFrameTab)userData;

            CreateInfoFrame();

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                    CrewManager.CreateCrewFrame(CrewManager.characters, infoFrame);
                    break;
                case InfoFrameTab.Mission:
                    CreateMissionInfo(infoFrame);
                    break;
                case InfoFrameTab.ManagePlayers:
                    GameMain.Server.ManagePlayersFrame(infoFrame);
                    break;
            }

            return true;
        }

        private void CreateMissionInfo(GUIFrame infoFrame)
        {
            if (Mission == null)
            {
                new GUITextBlock(new Rectangle(0,0,0,50), "No mission", GUI.Style, infoFrame, true);
                return;
            }

            new GUITextBlock(new Rectangle(0, 0, 0, 40), Mission.Name, GUI.Style, infoFrame, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 50, 0, 20), "Reward: "+Mission.Reward, GUI.Style, infoFrame, true);
            new GUITextBlock(new Rectangle(0, 70, 0, 50), Mission.Description, GUI.Style, infoFrame, true);

            
        }
        
        public void Update(float deltaTime)
        {
            TaskManager.Update(deltaTime);
            
            //guiRoot.Update(deltaTime);
            infoButton.Update(deltaTime);

            if (gameMode != null)   gameMode.Update(deltaTime);
            if (Mission != null)    Mission.Update(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            //guiRoot.Draw(spriteBatch);
            infoButton.Draw(spriteBatch);
            
            if (gameMode != null)   gameMode.Draw(spriteBatch);
            if (infoFrame != null)
            {
                infoFrame.Update(0.016f);
                infoFrame.Draw(spriteBatch);
            }
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument(
                new XElement("Gamesession"));

            var now = DateTime.Now;
            doc.Root.Add(new XAttribute("savetime", now.Hour + ":" + now.Minute + ", " + now.ToShortDateString()));

            doc.Root.Add(new XAttribute("submarine", submarine==null ? "" : submarine.Name));

            ((SinglePlayerMode)gameMode).Save(doc.Root);

            try
            {
                doc.Save(filePath);
            }
            catch
            {
                DebugConsole.ThrowError("Saving gamesession to ''" + filePath + "'' failed!");
            }
        }
    }
}
