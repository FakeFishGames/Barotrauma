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

        //two locations used as the start and end in the MP mode
        private Location[] dummyLocations;

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

        public Location StartLocation
        {
            get 
            {
                if (Map != null) return Map.CurrentLocation;

                if (dummyLocations==null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[0]; 
            }
        }
         
        public Location EndLocation
        {
            get
            {
                if (Map != null) return Map.SelectedLocation;

                if (dummyLocations == null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[1];
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

            infoButton = new GUIButton(new Rectangle(10, 10, 100, 20), "Info", "", null);
            infoButton.OnClicked = ToggleInfoFrame;

            if (gameModePreset != null) gameMode = gameModePreset.Instantiate(missionType);
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

        private void CreateDummyLocations()
        {
            dummyLocations = new Location[2];

            string seed = "";
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null)
            {
                seed = GameMain.GameSession.Level.Seed;
            }
            else if (GameMain.NetLobbyScreen != null)
            {
                seed = GameMain.NetLobbyScreen.LevelSeed;
            }

            MTRandom rand = new MTRandom(ToolBox.StringToInt(seed));
            for (int i = 0; i < 2; i++)
            {
                dummyLocations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f));
            }
        }

        public void StartShift(string levelSeed, bool loadSecondSub = false)
        {
            Level randomLevel = Level.CreateRandom(levelSeed);

            StartShift(randomLevel, true, loadSecondSub);
        }

        public void StartShift(Level level, bool reloadSub = true, bool loadSecondSub = false)
        {
            GameMain.LightManager.LosEnabled = GameMain.NetworkMember == null || GameMain.NetworkMember.CharacterInfo != null;
                        
            this.level = level;

            if (submarine==null)
            {
                DebugConsole.ThrowError("Couldn't start game session, submarine not selected");
                return;
            }

            if (reloadSub || Submarine.MainSub != submarine) submarine.Load(true);
            Submarine.MainSub = submarine;
            if (loadSecondSub)
            {
                if (Submarine.MainSubs[1] == null)
                {
                    Submarine.MainSubs[1] = new Submarine(Submarine.MainSub.FilePath,Submarine.MainSub.MD5Hash.Hash,true);
                    Submarine.MainSubs[1].Load(false);
                }
                else if (reloadSub)
                {
                    Submarine.MainSubs[1].Load(false);
                }
            }
            
            if (level != null)
            {
                level.Generate();

                submarine.SetPosition(submarine.FindSpawnPos(level.StartPosition - new Vector2(0.0f, 2000.0f)));
                
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

            if (gameMode != null) gameMode.MsgBox();

            Entity.Spawner = new EntitySpawner();

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
                GUIMessageBox.MessageBoxes.Add(summaryFrame);
                var okButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Ok", Alignment.BottomRight, "", summaryFrame.children[0]);
                okButton.OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; };
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
                if (CrewManager != null && CrewManager.CrewCommander!= null && CrewManager.CrewCommander.IsOpen)
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
                    CrewManager.CreateCrewFrame(CrewManager.characters, infoFrame.children[0] as GUIFrame);
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
                new GUITextBlock(new Rectangle(0,0,0,50), "No mission", "", infoFrame, true);
                return;
            }

            new GUITextBlock(new Rectangle(0, 0, 0, 40), Mission.Name, "", infoFrame, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 50, 0, 20), "Reward: "+Mission.Reward, "", infoFrame, true);
            new GUITextBlock(new Rectangle(0, 70, 0, 50), Mission.Description, "", infoFrame, true);

            
        }
        
        public void AddToGUIUpdateList()
        {
            infoButton.AddToGUIUpdateList();
            
            if (gameMode != null) gameMode.AddToGUIUpdateList();
            
            if (infoFrame != null) infoFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            TaskManager.Update(deltaTime);

            if (GUI.DisableHUD) return;
            
            //guiRoot.Update(deltaTime);
            infoButton.Update(deltaTime);

            if (gameMode != null)   gameMode.Update(deltaTime);
            if (Mission != null)    Mission.Update(deltaTime);
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
            
            if (gameMode != null)   gameMode.Draw(spriteBatch);
            if (infoFrame != null)  infoFrame.Draw(spriteBatch);            
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument(
                new XElement("Gamesession"));

            var now = DateTime.Now;
            doc.Root.Add(new XAttribute("savetime", now.ToShortTimeString() + ", " + now.ToShortDateString()));

            doc.Root.Add(new XAttribute("submarine", submarine==null ? "" : submarine.Name));

            doc.Root.Add(new XAttribute("mapseed", Map.Seed));

            ((SinglePlayerMode)gameMode).Save(doc.Root);

            try
            {
                doc.Save(filePath);
            }
            catch
            {
                DebugConsole.ThrowError("Saving gamesession to \"" + filePath + "\" failed!");
            }
        }
    }
}
