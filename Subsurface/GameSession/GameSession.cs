using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Text;
using System.Collections.Generic;

namespace Subsurface
{
    class GameSession
    {
        public readonly TaskManager taskManager;
        public readonly CrewManager crewManager;
        public readonly HireManager hireManager;

        protected DateTime startTime;
        protected DateTime endTime;

        public readonly GameMode gameMode;

        private GUIListBox chatBox;
        private GUITextBox textBox;

        private GUIProgressBar timerBar;

        private GUIButton endShiftButton;

        string saveFile;

        private int day;

        public string SaveFile
        {
            get { return saveFile; }
        }

        public int Day
        {
            get { return day; }
        }

        public GameSession(string selectedMapFile, bool save, TimeSpan gameDuration, GameMode gameMode = null)
        {
            taskManager = new TaskManager(this);
            crewManager = new CrewManager(this);
            hireManager = new HireManager();

            hireManager.GenerateCharacters("Content/Characters/Human/human.xml", 10);

            int width = 350, height = 100;
            chatBox = new GUIListBox(new Rectangle(
                Game1.GraphicsWidth - (int)GUI.style.smallPadding.X - width, 
                Game1.GraphicsHeight - (int)GUI.style.smallPadding.W*2 - 25 - height, 
                width, height), 
                Color.White * 0.5f);

            endShiftButton = new GUIButton(new Rectangle(Game1.GraphicsWidth - 240, 20, 100, 25), "End shift", Color.White, Alignment.CenterX, null);
            endShiftButton.OnClicked = EndShift;

            timerBar = new GUIProgressBar(new Rectangle(Game1.GraphicsWidth - 120, 20, 100, 25), Color.Gold, 0.0f, null);

            textBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + (int)GUI.style.smallPadding.W, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.Bottom, Alignment.Left);
            textBox.OnEnter = EnterChatMessage;
            
            this.gameMode = gameMode;
            if (this.gameMode != null) this.gameMode.Start(Game1.NetLobbyScreen.GameDuration);

            startTime = DateTime.Now;
            endTime = startTime + gameDuration;            

            if (!save) return;

            CreateSaveFile(selectedMapFile);

            day = 1;
            
        }

        public bool TryHireCharacter(CharacterInfo characterInfo)
        {
            if (crewManager.Money < characterInfo.salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            crewManager.characterInfos.Add(characterInfo);

            crewManager.Money -= characterInfo.salary;

            return true;
        }

        public string GetMoney()
        {
            return ("Money: " + crewManager.Money);
        }

        public void StartShift(int scriptedEventCount = 1)
        {
            if (crewManager.characterInfos.Count == 0) return;

            crewManager.StartShift();
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
                StringBuilder sb = new StringBuilder();                
                List<Character> casualties = crewManager.characters.FindAll(c => c.IsDead);

                if (casualties.Any())
                {
                    sb.Append("Casualties: \n");
                    foreach (Character c in casualties)
                    {
                        sb.Append("    - " + c.info.name + "\n");
                    }
                }
                else
                {
                    sb.Append("No casualties!");
                }              

                new GUIMessageBox("Day #" + day + " is over!\n", sb.ToString());


                if (saveFile == null) return false;

                Map.Loaded.SaveAs(saveFile);

                crewManager.EndShift();

                Game1.LobbyScreen.Select();

                day++;
            }

            taskManager.EndShift();

            return true;
        }
        
        private void CreateSaveFile(string mapName)
        {
            string path = "Content/Data/Saves/";

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            string name = Path.GetFileNameWithoutExtension(mapName);
            string extension = Path.GetExtension(mapName);

            int i = 0;
            while (File.Exists(path + name + i + extension))
            {
                i++;
            }

            saveFile = path + name + i+extension;


            try
            {
                File.Copy(mapName, saveFile);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Copying map file ''" + mapName + "'' to ''" + saveFile + "'' failed", e);
            }
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
            endShiftButton.Enabled = !taskManager.CriticalTasks;

            endShiftButton.Update(deltaTime);

            textBox.Update(deltaTime);

            if (gameMode != null) gameMode.Update();

            double duration = (endTime - startTime).TotalSeconds;
            double elapsedTime = (DateTime.Now-startTime).TotalSeconds;
            timerBar.BarSize = (float)(elapsedTime / Math.Max(duration, 1.0));

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
            crewManager.Draw(spriteBatch);
            taskManager.Draw(spriteBatch);

            chatBox.Draw(spriteBatch);
            textBox.Draw(spriteBatch);

            timerBar.Draw(spriteBatch);

            if (Game1.Client == null) endShiftButton.Draw(spriteBatch);
        }
    }
}
