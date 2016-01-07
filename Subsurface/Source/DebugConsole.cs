using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    struct ColoredText
    {
        public string Text;
        public Color Color;

        public readonly string Time;

        public ColoredText(string text, Color color)
        {
            this.Text = text;
            this.Color = color;

            Time = DateTime.Now.ToString();
        }
    }

    static class DebugConsole
    {
        public static List<ColoredText> Messages = new List<ColoredText>();

        static bool isOpen;


        static GUIFrame frame;
        static GUIListBox listBox;
        static GUITextBox textBox;
        
        //used for keeping track of the message entered when pressing up/down
        static int selectedIndex;

        public static bool IsOpen
        {
            get 
            {                
                return isOpen; 
            }
        }

        public static void Init(GameWindow window)
        {
            int x = 20, y = 20;
            int width = 800, height = 500;

            frame = new GUIFrame(new Rectangle(x, y, width, height), new Color(0.4f, 0.4f, 0.4f, 0.5f));
            frame.Color = Color.White * 0.4f;
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            listBox = new GUIListBox(new Rectangle(0,0,0, frame.Rect.Height-40), Color.Black*0.9f, null, frame);

            textBox = new GUITextBox(new Rectangle(0,0,0,20), Color.Black*0.6f, Color.White, Alignment.BottomLeft, Alignment.Left, null, frame);
            NewMessage("Press F3 to open/close the debug console", Color.Cyan);
            NewMessage("Enter ''help'' for a list of available console commands", Color.Cyan);

        }

        public static void Update(GameMain game, float deltaTime)
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
                    GUIComponent.MouseOn = null;
                    textBox.Deselect();
                }

                //keyboardDispatcher.Subscriber = (isOpen) ? textBox : null;
            }

            if (isOpen)
            {
                frame.Update(deltaTime);

                Character.DisableControls = true;

                if (PlayerInput.KeyHit(Keys.Up))
                {
                    SelectMessage(-1);
                }
                else if (PlayerInput.KeyHit(Keys.Down))
                {
                    SelectMessage(1);
                }
                
                //textBox.Update(deltaTime);

                if (PlayerInput.GetKeyboardState.IsKeyDown(Keys.Enter) && textBox.Text != "")
                {
                    NewMessage(textBox.Text, Color.White);
                    ExecuteCommand(textBox.Text, game);
                    textBox.Text = "";

                    //selectedIndex = messages.Count;
                }
            }
        }

        private static void SelectMessage(int direction)
        {
            int messageCount = listBox.children.Count;
            if (messageCount == 0) return;

            direction = Math.Min(Math.Max(-1, direction), 1);
            
            selectedIndex += direction;
            if (selectedIndex < 0) selectedIndex = messageCount - 1;
            selectedIndex = selectedIndex % messageCount;

            textBox.Text = (listBox.children[selectedIndex] as GUITextBlock).Text;    
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            if (!isOpen) return;

            int margin = 5;

            //GUI.DrawRectangle(spriteBatch,
            //    new Vector2(x, y),
            //    new Vector2(width, height),
            //    new Color(0.4f, 0.4f, 0.4f, 0.6f), true);

            //GUI.DrawRectangle(spriteBatch,
            //    new Vector2(x + margin, y + margin),
            //    new Vector2(width - margin * 2, height - margin * 2),
            //    new Color(0.0f, 0.0f, 0.0f, 0.6f), true);

            //remove messages that won't fit on the screen
            //while (messages.Count() * 20 > height - 70)
            //{
            //    messages.RemoveAt(0);
            //}

            //Vector2 messagePos = new Vector2(x + margin * 2, y + height - 70 - messages.Count()*20);
            //foreach (ColoredText message in messages)
            //{
            //    spriteBatch.DrawString(GUI.Font, message.Text, messagePos, message.Color); 
            //    messagePos.Y += 20;
            //}

            //textBox.Draw(spriteBatch);

            frame.Draw(spriteBatch);
        }

        public static void ExecuteCommand(string command, GameMain game)
        {
#if !DEBUG
            if (GameMain.Client != null)
            {
                ThrowError("Console commands are disabled in multiplayer mode");
                return;
            }
#endif

            if (command == "") return;
            string[] commands = command.Split(' ');

            switch (commands[0].ToLower())
            {
                case "help":
                    NewMessage("menu: go to main menu", Color.Cyan);
                    NewMessage("game: enter the ''game screen''", Color.Cyan);
                    NewMessage("edit: switch to submarine editor", Color.Cyan);
                    NewMessage("load [submarine name]: load a submarine!", Color.Cyan);
                    NewMessage("save [submarine name]: save the current submarine using the specified name", Color.Cyan);

                    NewMessage(" ", Color.Cyan);
                    

                    NewMessage("spawn: spawn a creature at a random spawnpoint", Color.Cyan);
                    NewMessage("spawn: spawn a creature at a random spawnpoint", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("lights: disable lighting", Color.Cyan);
                    NewMessage("los: disable the line of sight effect", Color.Cyan);
                    NewMessage("freecam: detach the camera from the controlled character", Color.Cyan);
                    NewMessage("control [character name]: start controlling the specified character", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("water: allows adding water into rooms or removing it by holding the left/right mouse buttons", Color.Cyan);
                    NewMessage("fire: allows putting up fires by left clicking", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("heal: restore the controlled character to full health", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("fixwalls: fixes all the walls", Color.Cyan);
                    NewMessage("fixitems: fixes every item/device in the sub", Color.Cyan);
                    NewMessage("oxygen: replenishes the oxygen in every room to 100%", Color.Cyan);
                    NewMessage("power [amount]: immediately sets the temperature of the reactor to the specified value", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("debugdraw: toggles the ''debug draw mode''", Color.Cyan);
                    NewMessage("netstats: toggles the visibility of the network statistics panel", Color.Cyan);

                    
                    break;
                case "createfilelist":
                    UpdaterUtil.SaveFileList("filelist.xml");
                    break;
                case "spawn":
                    if (commands.Length == 1) return;
                    
                    if (commands[1].ToLower()=="human")
                    {
                        WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Human);
                        Character.Controlled = Character.Create(Character.HumanConfigFile, (spawnPoint == null) ? Vector2.Zero : spawnPoint.WorldPosition);
                        if (GameMain.GameSession != null)
                        {
                            SinglePlayerMode mode = GameMain.GameSession.gameMode as SinglePlayerMode;
                            if (mode == null) break;
                            GameMain.GameSession.CrewManager.AddCharacter(Character.Controlled);
                            GameMain.GameSession.CrewManager.SelectCharacter(null, Character.Controlled);
                        }
                    }
                    else
                    {
                        WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Enemy);
                        Character.Create("Content/Characters/" + commands[1] + "/" + commands[1] + ".xml", (spawnPoint == null) ? Vector2.Zero : spawnPoint.WorldPosition);
                    }

                    break;
                //case "startserver":
                //    if (Game1.Server==null)
                //        Game1.NetworkMember = new GameServer();
                //    break;
                case "kick":
                    if (GameMain.Server == null) break;
                    GameMain.Server.KickPlayer(commands[1]);
                    break;
                case "startclient":
                    if (commands.Length == 1) return;
                    if (GameMain.Client == null)
                    {
                        GameMain.NetworkMember = new GameClient("Name");
                        GameMain.Client.ConnectToServer(commands[1]);
                    }
                    break;
                case "mainmenuscreen":
                case "mainmenu":
                case "menu":
                    GameMain.MainMenuScreen.Select();
                    break;
                case "gamescreen":
                case "game":
                    GameMain.GameScreen.Select();
                    break;
                case "editmapscreen":
                case "editmap":
                case "edit":              
                    GameMain.EditMapScreen.Select();
                    break;
                case "test":
                    Submarine.Load("aegir mark ii");
                    GameMain.DebugDraw = true;
                    GameMain.LightManager.LosEnabled = false;
                    GameMain.EditMapScreen.Select();
                    break;                    
                case "editcharacter":
                case "editchar":
                    GameMain.EditCharacterScreen.Select();
                    break;
                case "controlcharacter":
                case "control":
                    if (commands.Length < 2) break;
                    commands[1] = commands[1].ToLower();
                    Character.Controlled = Character.CharacterList.Find(c => !c.IsNetworkPlayer && c.Name.ToLower() == commands[1]);
                    break;
                case "godmode":
                    Submarine.Loaded.GodMode = !Submarine.Loaded.GodMode;
                    break;
                case "heal":
                    if (Character.Controlled != null)
                    {
                        Character.Controlled.Health = Character.Controlled.MaxHealth;
                        Character.Controlled.Oxygen = 100.0f;
                        Character.Controlled.Bleeding = 0.0f;
                    }
                    break;
                case "freecamera":
                case "freecam":
                    Character.Controlled = null;
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    break;
                case "editwater":
                case "water":
                    if (GameMain.Client == null) Hull.EditWater = !Hull.EditWater;
                    
                    break;
                case "fire":
                    if (GameMain.Client == null) Hull.EditFire = !Hull.EditFire;
                    
                    break;
                case "generatelevel":
                    GameMain.Level = new Level("asdf", 50.0f, 500,500, 50);
                    GameMain.Level.Generate();
                    break;
                case "fixitems":
                    foreach (Item it in Item.ItemList)
                    {
                        it.Condition = 100.0f;
                    }
                    break;
                case "fixhull":
                case "fixwalls":
                    foreach (Structure w in Structure.WallList)
                    {
                        for (int i = 0 ; i < w.SectionCount; i++)
                        {
                            w.AddDamage(i, -100000.0f);
                        }
                    }
                    break;
                case "power":
                    Item reactorItem = Item.ItemList.Find(i => i.GetComponent<Reactor>() != null);
                    if (reactorItem == null) return;

                    float power = 5000.0f;
                    if (commands.Length>1) float.TryParse(commands[1], out power);

                    var reactor = reactorItem.GetComponent<Reactor>();
                    reactor.ShutDownTemp = 7000.0f;
                    reactor.AutoTemp = true;
                    reactor.Temperature = power;
                    break;
                case "shake":
                    GameMain.GameScreen.Cam.Shake = 10.0f;
                    break;
                case "losenabled":
                case "los":
                case "drawlos":
                    GameMain.LightManager.LosEnabled = !GameMain.LightManager.LosEnabled;
                    break;
                case "lighting":
                case "lightingenabled":
                case "light":
                case "lights":
                    GameMain.LightManager.LightingEnabled = !GameMain.LightManager.LightingEnabled;
                    break;
                case "oxygen":
                case "air":
                    foreach (Hull hull in Hull.hullList)
                    {
                        hull.OxygenPercentage = 100.0f;
                    }
                    break;
                case "tutorial":
                    TutorialMode.StartTutorial(Tutorials.TutorialType.TutorialTypes[0]);
                    break;
                case "lobbyscreen":
                case "lobby":
                    GameMain.LobbyScreen.Select();
                    break;
                case "savemap":
                case "savesub":
                case "save":
                    if (commands.Length < 2) break;

                    string fileName = string.Join(" ", commands.Skip(1));
                    if (fileName.Contains("../"))
                    {
                        DebugConsole.ThrowError("Illegal symbols in filename (../)");
                        return;
                    }
                    if (Submarine.SaveCurrent(fileName +".sub")) NewMessage("map saved", Color.Green);
                    break;
                case "loadmap":
                case "loadsub":
                case "load":
                    if (commands.Length < 2) break;

                    Submarine.Load(string.Join(" ", commands.Skip(1)));
                    break;
                case "cleansub":
                    for (int i = MapEntity.mapEntityList.Count-1; i>=0; i--)
                    {
                        MapEntity me = MapEntity.mapEntityList[i];

                        if (me.SimPosition.Length()>200.0f)
                        {
                            DebugConsole.NewMessage("Removed "+me.Name+" (simposition "+me.SimPosition+")", Color.Orange);
                            MapEntity.mapEntityList.RemoveAt(i);
                        }
                        else if (me.MoveWithLevel)
                        {
                            DebugConsole.NewMessage("Removed " + me.Name + " (MoveWithLevel==true)", Color.Orange);
                            MapEntity.mapEntityList.RemoveAt(i);
                        }
                    }
                    break;
                case "messagebox":
                    if (commands.Length < 3) break;
                    new GUIMessageBox(commands[1], commands[2]);
                    break;
                case "debugdraw":
                    //Hull.DebugDraw = !Hull.DebugDraw;
                    //Ragdoll.DebugDraw = !Ragdoll.DebugDraw;
                    GameMain.DebugDraw = !GameMain.DebugDraw;
                    break;
                case "netstats":
                    if (GameMain.Server == null) return;

                    GameMain.Server.ShowNetStats = !GameMain.Server.ShowNetStats;
                    break;
                case "cleanbuild":
                    GameMain.Config.MusicVolume = 0.5f;
                    GameMain.Config.SoundVolume = 0.5f;
                    GameMain.Config.Save("config.xml");
                    DebugConsole.NewMessage("Set music and sound volume to 0.5", Color.Green);

                    var saveFiles = System.IO.Directory.GetFiles(SaveUtil.SaveFolder);

                    foreach (string saveFile in saveFiles)
                    {
                        System.IO.File.Delete(saveFile);
                        DebugConsole.NewMessage("Deleted "+saveFile, Color.Green);
                    }

                    if (System.IO.File.Exists("filelist.xml"))
                    {
                        System.IO.File.Delete("filelist.xml");
                    }

                    if (!System.IO.File.Exists("Content/Map/TutorialSub.sub"))
                    {
                        DebugConsole.ThrowError("TutorialSub.sub not found!");
                    }
                    break;
                default:
                    NewMessage("Command not found", Color.Red);
                    break;
            }
        }

        public static void NewMessage(string msg, Color color)
        {
            if (String.IsNullOrEmpty((msg))) return;

            Messages.Add(new ColoredText(msg, color));

            try
            {
                var textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 15), msg, GUI.Style, Alignment.TopLeft, Alignment.Left, null, true, GUI.SmallFont);
                textBlock.CanBeFocused = false;
                textBlock.TextColor = color;

                listBox.AddChild(textBlock);
                listBox.BarScroll = 1.0f;
            }
            catch
            {
                return;
            }

            //messages.Add(new ColoredText(msg, color));

            if (textBox != null && textBox.Text == "") selectedIndex = listBox.children.Count;
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
