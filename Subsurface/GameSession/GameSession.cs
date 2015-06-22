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

        private GUIListBox chatBox;
        private GUITextBox textBox;

        private string savePath;

        private Map selectedMap;   

        public GameSession(Map selectedMap, GameModePreset gameModePreset)
            :this(selectedMap, gameModePreset.Instantiate())
        {

        }

        public GameSession(Map selectedMap, GameMode gameMode = null)
        {
            taskManager = new TaskManager(this);

            savePath = SaveUtil.CreateSavePath(SaveUtil.SaveFolder);

            guiRoot = new GUIFrame(new Rectangle(0,0,Game1.GraphicsWidth,Game1.GraphicsWidth), Color.Transparent);

            int width = 350, height = 100;
            if (Game1.Client!=null || Game1.Server!=null)
            {
                chatBox = new GUIListBox(new Rectangle(
                    Game1.GraphicsWidth - (int)GUI.style.smallPadding.X - width, 
                    Game1.GraphicsHeight - (int)GUI.style.smallPadding.W*2 - 25 - height, 
                    width, height), 
                    Color.White * 0.5f, guiRoot);

                textBox = new GUITextBox(
                    new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + (int)GUI.style.smallPadding.W, chatBox.Rect.Width, 25),
                    Color.White * 0.5f, Color.Black, Alignment.Bottom, Alignment.Left, guiRoot);
                            textBox.OnEnter = EnterChatMessage;
            }
             
            this.gameMode = gameMode;
            //if (gameMode != null && !gameMode.IsSinglePlayer)
            //{                
            //    gameMode.Start(Game1.NetLobbyScreen.GameDuration);
            //}

            //startTime = DateTime.Now;
            //endTime = startTime + gameDuration;

            this.selectedMap = selectedMap;
            
            //if (!save) return;

            //CreateSaveFile(selectedMapFile);
          
        }

        public GameSession(Map selectedMap, string savePath, string filePath)
            : this(selectedMap)
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

        public void StartShift(TimeSpan duration, int scriptedEventCount = 1)
        {
            //if (crewManager.characterInfos.Count == 0) return;

            if (Map.Loaded!=selectedMap) selectedMap.Load();

            if (gameMode!=null) gameMode.Start(duration);

            //crewManager.StartShift();
            taskManager.StartShift(scriptedEventCount);
        }

        public bool EndShift(GUIButton button, object obj)
        {
            if (Game1.Server!=null)
            {
                string endMessage = gameMode.EndMessage;
                
                Game1.Server.EndGame(endMessage);

            }
            else if (Game1.Client==null)
            {                
                Game1.LobbyScreen.Select();

                SaveUtil.SaveGame(savePath);
            }

            taskManager.EndShift();
            //gameMode.End();

            return true;
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

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            if (Game1.Server!=null)
            {
                Game1.Server.SendChatMessage(message);
            }
            else if (Game1.Client!=null)
            {
                Game1.Client.SendChatMessage(Game1.Client.Name + ": " + message);
            }

            textBox.Deselect();

            return true;
        }
        
        public void NewChatMessage(string text, Color color)
        {
            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), text,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, color,
                Alignment.Left, null, true);

            msg.Padding = new Vector4(GUI.style.smallPadding.X, 0, 0, 0);
            chatBox.AddChild(msg);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children.First());
            }
        }
        
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

            if (PlayerInput.KeyHit(Keys.Tab))
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

            gameMode.Draw(spriteBatch);

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
