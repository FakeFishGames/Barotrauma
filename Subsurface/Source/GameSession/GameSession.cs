using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma
{
    class GameSession
    {
        public readonly TaskManager TaskManager;
        
        public readonly GameMode gameMode;
                
        private GUIFrame guiRoot;
        
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
        }

        public string SaveFile
        {
            get { return saveFile; }
        }

        public ShiftSummary ShiftSummary
        {
            get { return shiftSummary; }
        }

        public GameSession(Submarine submarine, string saveFile, GameModePreset gameModePreset = null)
        {
            GameMain.GameSession = this;

            CrewManager = new CrewManager();

            TaskManager = new TaskManager(this);

            this.saveFile = saveFile;

            guiRoot = new GUIFrame(new Rectangle(0,0,GameMain.GraphicsWidth,GameMain.GraphicsWidth), Color.Transparent);

            if (gameModePreset!=null) gameMode = gameModePreset.Instantiate();
            this.submarine = submarine;
        }
        
        public GameSession(Submarine selectedSub, string saveFile, XDocument doc)
            : this(selectedSub, saveFile)
        {
            GameMain.GameSession = this;

            CrewManager = new CrewManager();

            selectedSub.Name = ToolBox.GetAttributeString(doc.Root, "submarine", selectedSub.Name);

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "gamemode") continue;

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

            if (reloadSub || Submarine.Loaded != submarine) submarine.Load();

            if (level != null)
            {
                level.Generate();

                submarine.SetPosition(level.StartPosition - new Vector2(0.0f, 2000.0f));

                GameMain.GameScreen.BackgroundCreatureManager.SpawnSprites(80);
            }

            if (gameMode.Mission!=null)
            {
                currentMission = gameMode.Mission;
                Mission.Start(Level.Loaded);
            }

            shiftSummary = new ShiftSummary(this);

            if (gameMode!=null) gameMode.Start();
            
            TaskManager.StartShift(level);

            GameMain.GameScreen.ColorFade(Color.Black, Color.TransparentBlack, 5.0f);
        }

        public void EndShift(string endMessage)
        {
            if (Mission != null) Mission.End();

            if (GameMain.Server!=null)
            {                
                CoroutineManager.StartCoroutine(GameMain.Server.EndGame(endMessage));

            }
            else if (GameMain.Client==null)
            {
                //Submarine.Unload();
                GameMain.LobbyScreen.Select();
            }
            
            GUIFrame summaryFrame = shiftSummary.CreateSummaryFrame(endMessage);
            GUIMessageBox.MessageBoxes.Enqueue(summaryFrame);
            var okButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Ok", Alignment.BottomRight, GUI.Style, summaryFrame.children[0]);
            okButton.OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Dequeue(); return true; };

            TaskManager.EndShift();

            currentMission = null;
            //gameMode.End();

            //return true;
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
            SaveUtil.LoadGame(saveFile);

            GameMain.LobbyScreen.Select();

            return true;
        }

        public void Update(float deltaTime)
        {
            TaskManager.Update(deltaTime);
            
            //guiRoot.Update(deltaTime);

            if (gameMode != null) gameMode.Update(deltaTime);
            if (Mission != null) Mission.Update(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            //guiRoot.Draw(spriteBatch);

            
            if (gameMode != null) gameMode.Draw(spriteBatch);
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
