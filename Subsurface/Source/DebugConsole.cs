using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Networking;

namespace Subsurface
{
    struct ColoredText
    {
        public string text;
        public Color color;

        public ColoredText(string text, Color color)
        {
            this.text = text;
            this.color = color;
        }
    }

    static class DebugConsole
    {
        public static List<ColoredText> messages = new List<ColoredText>();

        static bool isOpen;

        static GUITextBox textBox;
        
        //used for keeping track of the message entered when pressing up/down
        static int selectedIndex;

        public static bool IsOpen
        {
            get { return isOpen; }
        }

        public static void Init(GameWindow window)
        {            
            textBox = new GUITextBox(new Rectangle(30, 480,780, 30), Color.Black, Color.White, Alignment.Left, Alignment.Left);
            NewMessage("Press F3 to open/close the debug console", Color.Green);        
        }

        public static void Update(Game1 game, float deltaTime)
        {
            if (PlayerInput.KeyHit(Keys.F3))
            {
                isOpen = !isOpen;
                if (isOpen)
                {
                    textBox.Select();
                }
                else
                {
                    textBox.Deselect();
                }

                //keyboardDispatcher.Subscriber = (isOpen) ? textBox : null;
            }

            if (isOpen)
            {
                Character.DisableControls = true;

                if (PlayerInput.KeyHit(Keys.Up))
                {
                    SelectMessage(-1);
                }
                else if (PlayerInput.KeyHit(Keys.Down))
                {
                    SelectMessage(1);
                }
                
                textBox.Update(deltaTime);

                if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.Enter) && textBox.Text != "")
                {
                    messages.Add(new ColoredText(textBox.Text, Color.White));
                    ExecuteCommand(textBox.Text, game);
                    textBox.Text = "";

                    selectedIndex = messages.Count;
                }
            }
        }

        private static void SelectMessage(int direction)
        {
            int messageCount = messages.Count;
            if (messageCount == 0) return;

            direction = Math.Min(Math.Max(-1, direction), 1);
            
            selectedIndex += direction;
            if (selectedIndex < 0) selectedIndex = messageCount - 1;
            selectedIndex = selectedIndex % messageCount;

            textBox.Text = messages[selectedIndex].text;    
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (!isOpen) return;

            int x = 20, y = 20;
            int width = 800, height = 500;

            int margin = 5;

            GUI.DrawRectangle(spriteBatch,
                new Vector2(x, y),
                new Vector2(width, height),
                new Color(0.4f, 0.4f, 0.4f, 0.6f), true);

            GUI.DrawRectangle(spriteBatch,
                new Vector2(x + margin, y + margin),
                new Vector2(width - margin * 2, height - margin * 2),
                new Color(0.0f, 0.0f, 0.0f, 0.6f), true);

            //remove messages that won't fit on the screen
            while (messages.Count() * 20 > height-70)
            {
                messages.RemoveAt(0);
            }

            Vector2 messagePos = new Vector2(x + margin * 2, y + height - 70 - messages.Count()*20);
            foreach (ColoredText message in messages)
            {
                spriteBatch.DrawString(GUI.Font, message.text, messagePos, message.color); 
                messagePos.Y += 20;
            }

            textBox.Draw(spriteBatch);
        }

        public static void ExecuteCommand(string command, Game1 game)
        {
            if (Game1.Client!=null)
            {
                ThrowError("Console commands are disabled in multiplayer mode");
                return;
            }

            if (command == "") return;
            string[] commands = command.Split(' ');

            switch (commands[0].ToLower())
            {
                case "spawn":
                    if (commands.Length == 1) return;
                    
                    if (commands[1].ToLower()=="human")
                    {
                        WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Human);
                        Character.Controlled = new Character(Character.HumanConfigFile, (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition);
                        if (Game1.GameSession != null)
                        {
                            SinglePlayerMode mode = Game1.GameSession.gameMode as SinglePlayerMode;
                            if (mode == null) break;
                            mode.crewManager.AddCharacter(Character.Controlled);
                            mode.crewManager.SelectCharacter(Character.Controlled);
                        }
                    }
                    else
                    {
                        WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Enemy);
                        new Character("Content/Characters/" + commands[1] + "/" + commands[1] + ".xml", (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition);
                    }

                    break;
                //case "startserver":
                //    if (Game1.Server==null)
                //        Game1.NetworkMember = new GameServer();
                //    break;
                case "kick":
                    if (Game1.Server == null) break;
                    Game1.Server.KickPlayer(commands[1]);
                    break;
                case "startclient":
                    if (commands.Length == 1) return;
                    if (Game1.Client == null)
                    {
                        Game1.NetworkMember = new GameClient("Name");
                        Game1.Client.ConnectToServer(commands[1]);
                    }
                    break;
                case "mainmenuscreen":
                case "mainmenu":
                case "menu":
                    Game1.MainMenuScreen.Select();
                    break;
                case "gamescreen":
                case "game":
                    Game1.GameScreen.Select();
                    break;
                case "editmapscreen":
                case "editmap":
                case "edit":              
                    Game1.EditMapScreen.Select();
                    break;
                case "editcharacter":
                case "editchar":
                    Game1.EditCharacterScreen.Select();
                    break;
                case "freecamera":
                case "freecam":
                    Character.Controlled = null;
                    Game1.GameScreen.Cam.TargetPos = Vector2.Zero;
                    break;
                case "editwater":
                case "water":
                    if (Game1.Client== null)
                    {
                        Hull.EditWater = !Hull.EditWater;
                    }
                    break;
                case "generatelevel":
                    Game1.Level = new Level("asdf", 50.0f, 500,500, 50);
                    Game1.Level.Generate(100.0f);
                    break;
                case "fixitems":
                    foreach (Item it in Item.itemList)
                    {
                        it.Condition = 100.0f;
                    }
                    break;
                case "shake":
                    Game1.GameScreen.Cam.Shake = 10.0f;
                    break;
                case "losenabled":
                case "los":
                case "drawlos":
                    Game1.LightManager.LosEnabled = !Game1.LightManager.LosEnabled;
                    break;
                case "lighting":
                case "lightingenabled":
                case "light":
                case "lights":
                    Game1.LightManager.LightingEnabled = !Game1.LightManager.LightingEnabled;
                    break;
                case "oxygen":
                case "air":
                    foreach (Hull hull in Hull.hullList)
                    {
                        hull.OxygenPercentage = 100.0f;
                    }
                    break;
                case "lobbyscreen":
                case "lobby":
                    Game1.LobbyScreen.Select();
                    break;
                case "savemap":
                    if (commands.Length < 2) break;
                    Submarine.SaveCurrent("Content/SavedMaps/" + string.Join(" ", commands.Skip(1)));
                    NewMessage("map saved", Color.Green);
                    break;
                case "loadmap":
                    if (commands.Length < 2) break;
                    Submarine.Load("Content/SavedMaps/" + string.Join(" ", commands.Skip(1)));
                    break;
                //case "savegame":
                //    SaveUtil.SaveGame();
                //    break;
                //case "loadgame":
                //    SaveUtil.LoadGame(SaveUtil.SaveFolder + "save");
                //    break;
                case "messagebox":
                    if (commands.Length < 3) break;
                    new GUIMessageBox(commands[1], commands[2]);
                    break;
                case "debugdraw":
                    //Hull.DebugDraw = !Hull.DebugDraw;
                    //Ragdoll.DebugDraw = !Ragdoll.DebugDraw;
                    Game1.DebugDraw = !Game1.DebugDraw;
                    break;
                default:
                    NewMessage("Command not found", Color.Red);
                    break;
            }
        }

        public static void NewMessage(string msg, Color color)
        {
            if (String.IsNullOrEmpty((msg))) return;
            messages.Add(new ColoredText(msg, color));

            if (textBox != null && textBox.Text == "") selectedIndex = messages.Count;
        }

        public static void ThrowError(string error, Exception e = null)
        {            
            if (e != null) error += " {" + e.Message + "}";
            System.Diagnostics.Debug.WriteLine(error);
            NewMessage(error, Color.Red);
            isOpen = true;
        }
    }
}
