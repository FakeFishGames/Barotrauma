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

        public Mission Mission
        {
            get
            {
                if (gameMode != null) return gameMode.Mission;
                return null;
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
        
        public GameSession(Submarine selectedSub, string saveFile, string filePath)
            : this(selectedSub, saveFile)
        {
            GameMain.GameSession = this;

            CrewManager = new CrewManager();

            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;

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

            if (Mission!=null) Mission.Start(Level.Loaded);

            if (gameMode!=null) gameMode.Start();
            
            TaskManager.StartShift(level);
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
            
            TaskManager.EndShift();
            //gameMode.End();

            //return true;
        }
        
        
        public void KillCharacter(Character character)
        {
            CrewManager.KillCharacter(character);
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
            
            guiRoot.Update(deltaTime);

            if (gameMode != null) gameMode.Update(deltaTime);
            if (Mission != null) Mission.Update(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            guiRoot.Draw(spriteBatch);
            
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
