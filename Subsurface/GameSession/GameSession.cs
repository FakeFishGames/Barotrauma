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

        private string savePath;

        private Submarine submarine;

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
                return (mode == null) ? null : mode.map;
            }
        }
                
        public Submarine Submarine
        {
            get { return submarine; }
        }

        public string SavePath
        {
            get { return savePath; }
        }

        public GameSession(Submarine submarine, GameModePreset gameModePreset)
            :this(submarine, gameModePreset.Instantiate())
        {

        }

        public GameSession(Submarine selectedSub, GameMode gameMode = null)
        {
            taskManager = new TaskManager(this);

            savePath = SaveUtil.CreateSavePath(SaveUtil.SaveFolder);

            guiRoot = new GUIFrame(new Rectangle(0,0,Game1.GraphicsWidth,Game1.GraphicsWidth), Color.Transparent);

            this.gameMode = gameMode;
            this.submarine = selectedSub;
          
        }

        public GameSession(Submarine selectedSub, string savePath, string filePath)
            : this(selectedSub)
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "gamemode") continue;

                gameMode = new SinglePlayerMode(subElement);
            }

            this.savePath = savePath;
        }

        public void StartShift(TimeSpan duration, string levelSeed)
        {
            Level level = Level.CreateRandom(levelSeed);

            StartShift(duration, level);
        }

        public void StartShift(TimeSpan duration, Level level, bool reloadSub = true)
        {
            Game1.LightManager.LosEnabled = (Game1.Server==null);

            this.level = level;

            if (reloadSub || Submarine.Loaded != submarine) submarine.Load();

            if (level != null)
            {
                level.Generate(submarine == null ? 100.0f : Math.Max(Submarine.Borders.Width, Submarine.Borders.Height));
                submarine.SetPosition(level.StartPosition - new Vector2(0.0f, 2000.0f));
            }

            if (gameMode!=null) gameMode.Start(duration);



            taskManager.StartShift(level);
        }

        public void EndShift(string endMessage)
        {
            if (Game1.Server!=null)
            {                
                Game1.Server.EndGame(endMessage);

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
            singlePlayerMode.crewManager.KillCharacter(character);
        }

        public bool LoadPrevious(GUIButton button, object obj)
        {
            SaveUtil.LoadGame(savePath);

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
            taskManager.Draw(spriteBatch);

            if (gameMode != null) gameMode.Draw(spriteBatch);
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument(
                new XElement((XName)"Gamesession"));

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
