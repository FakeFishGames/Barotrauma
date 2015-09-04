using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Text;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class GameSession
    {
        public readonly TaskManager taskManager;


        //protected DateTime startTime;
        //protected DateTime endTime;

        public readonly GameMode gameMode;
        
        private GUIFrame guiRoot;

        //private GUIListBox chatBox;
        //private GUITextBox textBox;

        private string saveFile;

        private Submarine submarine;


        public Quest Quest
        {
            get
            {
                if (gameMode != null) return gameMode.Quest;
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

        public GameSession(Submarine submarine, string saveFile, GameModePreset gameModePreset)
            :this(submarine, saveFile, gameModePreset.Instantiate())
        {

        }

        public GameSession(Submarine selectedSub, string saveFile, GameMode gameMode = null)
        {
            taskManager = new TaskManager(this);

            this.saveFile = saveFile;

            guiRoot = new GUIFrame(new Rectangle(0,0,Game1.GraphicsWidth,Game1.GraphicsWidth), Color.Transparent);

            this.gameMode = gameMode;
            this.submarine = selectedSub;
          
        }

        public GameSession(Submarine selectedSub, string saveFile, string filePath)
            : this(selectedSub, saveFile)
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "gamemode") continue;

                gameMode = new SinglePlayerMode(subElement);
            }
        }

        public void StartShift(TimeSpan duration, string levelSeed)
        {
            Level level = Level.CreateRandom(levelSeed);

            StartShift(duration, level);
        }

        public void StartShift(TimeSpan duration, Level level, bool reloadSub = true)
        {
            Game1.LightManager.LosEnabled = (Game1.Server==null && Character.Controlled==null);
                        
            this.level = level;

            if (reloadSub || Submarine.Loaded != submarine) submarine.Load();

            if (level != null)
            {
                level.Generate(submarine == null ? 100.0f : Math.Max(Submarine.Borders.Width, Submarine.Borders.Height));
                submarine.SetPosition(level.StartPosition - new Vector2(0.0f, 2000.0f));
            }

            if (Quest!=null) Quest.Start(Level.Loaded);

            if (gameMode!=null) gameMode.Start(duration);
            
            taskManager.StartShift(level);
        }

        public void EndShift(string endMessage)
        {
            if (Quest != null) Quest.End();

            if (Game1.Server!=null)
            {                
                CoroutineManager.StartCoroutine(Game1.Server.EndGame(endMessage));

            }
            else if (Game1.Client==null)
            {                
                Game1.LobbyScreen.Select();
            }
            
            taskManager.EndShift();
            //gameMode.End();

            //return true;
        }
        
        
        public void KillCharacter(Character character)
        {
            SinglePlayerMode singlePlayerMode = gameMode as SinglePlayerMode;
            if (singlePlayerMode == null) return;
            singlePlayerMode.CrewManager.KillCharacter(character);
        }

        public bool LoadPrevious(GUIButton button, object obj)
        {
            SaveUtil.LoadGame(saveFile);

            Game1.LobbyScreen.Select();

            return true;
        }

        public void Update(float deltaTime)
        {
            taskManager.Update(deltaTime);
            
            guiRoot.Update(deltaTime);

            if (gameMode != null) gameMode.Update(deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            guiRoot.Draw(spriteBatch);
            
            if (gameMode != null) gameMode.Draw(spriteBatch);
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument(
                new XElement((XName)"Gamesession"));

            var now = DateTime.Now;
            doc.Root.Add(new XAttribute("savetime", now.Hour + ":" + now.Minute + ", " + now.ToShortDateString()));

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
