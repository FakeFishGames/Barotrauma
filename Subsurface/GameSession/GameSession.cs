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
        private GUITextBox textBox;

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

            //int width = 350, height = 100;
            //if (Game1.NetworkMember!=null)
            //{
            //    chatBox = new GUIListBox(new Rectangle(
            //        Game1.GraphicsWidth - 20 - width, 
            //        Game1.GraphicsHeight - 40 - 25 - height, 
            //        width, height), 
            //        Color.White * 0.5f, GUI.style, guiRoot);

            //    textBox = new GUITextBox(
            //        new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
            //        Color.White * 0.5f, Color.Black, Alignment.Bottom, Alignment.Left, GUI.style, guiRoot);
            //                textBox.OnEnter = EnterChatMessage;
            //}
             
            this.gameMode = gameMode;
            //if (gameMode != null && !gameMode.IsSinglePlayer)
            //{                
            //    gameMode.Start(Game1.NetLobbyScreen.GameDuration);
            //}

            //startTime = DateTime.Now;
            //endTime = startTime + gameDuration;

            this.submarine = selectedSub;
            
            //if (!save) return;

            //CreateSaveFile(selectedMapFile);
          
        }

        public GameSession(Submarine selectedSub, string savePath, string filePath)
            : this(selectedSub)
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;

            //gameMode = GameModePreset.list.Find(gm => gm.Name == "Single Player").Instantiate();

            //day = ToolBox.GetAttributeInt(doc.Root,"day",1);

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
            Lights.LightManager.FowEnabled = (Game1.Server==null);

            this.level = level;

            if (reloadSub || Submarine.Loaded != submarine) submarine.Load();

            if (gameMode!=null) gameMode.Start(duration);

            if (level != null)
            {
                level.Generate(submarine == null ? 100.0f : Math.Max(Submarine.Borders.Width, Submarine.Borders.Height));
                submarine.SetPosition(level.StartPosition - new Vector2(0.0f, 2000.0f));
            }

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
            return true;
        }

        //public bool EnterChatMessage(GUITextBox textBox, string message)
        //{
        //    if (string.IsNullOrWhiteSpace(message)) return false;

        //    else if (Game1.NetworkMember != null)
        //    {
        //        Game1.NetworkMember.SendChatMessage(Game1.NetworkMember.Name + ": " + message);
        //    }

        //    textBox.Deselect();

        //    return true;
        //}
        
        //public void NewChatMessage(string text, Color color)
        //{
        //    GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), text,

        //        ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, color,
        //        Alignment.Left, null, null, true);

        //    msg.Padding = new Vector4(20.0f, 0, 0, 0);
        //    chatBox.AddChild(msg);

        //    while (chatBox.CountChildren > 20)
        //    {
        //        chatBox.RemoveChild(chatBox.children.First());
        //    }
        //}
        
        public void Update(float deltaTime)
        {
            taskManager.Update(deltaTime);
            //if (endShiftButton!=null) endShiftButton.Enabled = !taskManager.CriticalTasks;
            //endShiftButton.Enabled = true;
            
            guiRoot.Update(deltaTime);
            
            //endShiftButton.Update(deltaTime);

            //textBox.Update(deltaTime);

            if (gameMode != null) gameMode.Update(deltaTime);

            //double duration = (endTime - startTime).TotalSeconds;
            //double elapsedTime = (DateTime.Now-startTime).TotalSeconds;
            //timerBar.BarSize = (float)(elapsedTime / Math.Max(duration, 1.0));

            if (PlayerInput.KeyHit(Keys.Tab) && textBox!=null)
            {
                if (textBox.Selected)
                {
                    textBox.Deselect();
                    textBox.Text = "";
                }
                else
                {
                    textBox.Select();
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            guiRoot.Draw(spriteBatch);
            //crewManager.Draw(spriteBatch);
            taskManager.Draw(spriteBatch);

            if (gameMode!=null) gameMode.Draw(spriteBatch);

            //chatBox.Draw(spriteBatch);
            //textBox.Draw(spriteBatch);

            //timerBar.Draw(spriteBatch);

            //if (Game1.Client == null) endShiftButton.Draw(spriteBatch);
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument(
                new XElement((XName)"Gamesession"));

            ((SinglePlayerMode)gameMode).Save(doc.Root);

            //doc.Root.Add(new XAttribute("day", day));

            //crewManager.Save(doc.Root);
            
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
