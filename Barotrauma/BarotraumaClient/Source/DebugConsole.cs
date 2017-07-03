using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using Barotrauma.Items.Components;
using System.Text;
using FarseerPhysics;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        static bool isOpen;

        private static Queue<ColoredText> queuedMessages = new Queue<ColoredText>();

        //used for keeping track of the message entered when pressing up/down
        static int selectedIndex;

        public static bool IsOpen
        {
            get
            {
                return isOpen;
            }
        }

        static GUIFrame frame;
        static GUIListBox listBox;
        static GUITextBox textBox;
        
        public static void Init(GameWindow window)
        {
            int x = 20, y = 20;
            int width = 800, height = 500;

            frame = new GUIFrame(new Rectangle(x, y, width, height), new Color(0.4f, 0.4f, 0.4f, 0.8f));
            frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            listBox = new GUIListBox(new Rectangle(0, 0, 0, frame.Rect.Height - 40), Color.Black, "", frame);
            //listBox.Color = Color.Black * 0.7f;

            textBox = new GUITextBox(new Rectangle(0, 0, 0, 20), Color.Black, Color.White, Alignment.BottomLeft, Alignment.Left, "", frame);
            //textBox.Color = Color.Black * 0.7f;

            //messages already added before initialization -> add them to the listbox
            List<ColoredText> unInitializedMessages = new List<ColoredText>(Messages);
            Messages.Clear();

            foreach (ColoredText msg in unInitializedMessages)
            {
                NewMessage(msg.Text, msg.Color);
            }

            NewMessage("Press F3 to open/close the debug console", Color.Cyan);
            NewMessage("Enter \"help\" for a list of available console commands", Color.Cyan);

        }

        public static void AddToGUIUpdateList()
        {
            if (isOpen)
            {
                frame.AddToGUIUpdateList();
            }
        }

        public static void Update(GameMain game, float deltaTime)
        {
            while (queuedMessages.Count > 0)
            {
                AddMessage(queuedMessages.Dequeue());
            }

            if (PlayerInput.KeyHit(Keys.F3))
            {
                isOpen = !isOpen;
                if (isOpen)
                {
                    textBox.Select();
                    AddToGUIUpdateList();
                }
                else
                {
                    GUIComponent.ForceMouseOn(null);
                    textBox.Deselect();
                }
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
                
                if (PlayerInput.KeyHit(Keys.Enter))
                {
                    ExecuteCommand(textBox.Text, game);
                    textBox.Text = "";
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

            frame.Draw(spriteBatch);
        }

        private static bool IsCommandPermitted(string command, GameClient client)
        {
            switch (command)
            {
                case "kick":
                    return client.HasPermission(ClientPermissions.Kick);
                case "ban":
                case "banip":
                    return client.HasPermission(ClientPermissions.Ban);
                case "netstats":
                case "help":
                case "dumpids":
                case "admin":
                case "entitylist":
                    return true;
                default:
                    return false;
            }
        }

        private static void AddMessage(ColoredText msg)
        {
            //listbox not created yet, don't attempt to add
            if (listBox == null) return;

            if (listBox.children.Count > MaxMessages)
            {
                listBox.children.RemoveRange(0, listBox.children.Count - MaxMessages);
            }
            
            Messages.Add(msg);
            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }

            try
            {
                var textBlock = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width, 0), msg.Text, "", Alignment.TopLeft, Alignment.Left, null, true, GUI.SmallFont);
                textBlock.CanBeFocused = false;
                textBlock.TextColor = msg.Color;

                listBox.AddChild(textBlock);
                listBox.BarScroll = 1.0f;
            }
            catch (Exception e)
            {
                ThrowError("Failed to add a message to the debug console.", e);
            }

            selectedIndex = listBox.children.Count;

            if (activeQuestionText != null)
            {
                //make sure the active question stays at the bottom of the list
                listBox.children.Remove(activeQuestionText);
                listBox.children.Add(activeQuestionText);
            }
        }

        private static bool ExecProjSpecific(string[] commands)
        {
            switch (commands[0].ToLowerInvariant())
            {
                case "startclient":
                    if (commands.Length == 1) return true;
                    if (GameMain.Client == null)
                    {
                        GameMain.NetworkMember = new GameClient("Name");
                        GameMain.Client.ConnectToServer(commands[1]);
                    }
                    break;
                case "mainmenuscreen":
                case "mainmenu":
                case "menu":
                    GameMain.GameSession = null;

                    List<Character> characters = new List<Character>(Character.CharacterList);
                    foreach (Character c in characters)
                    {
                        c.Remove();
                    }

                    GameMain.MainMenuScreen.Select();
                    break;
                case "gamescreen":
                case "game":
                    GameMain.GameScreen.Select();
                    break;
                case "editmapscreen":
                case "editmap":
                case "edit":
                    if (commands.Length > 1)
                    {
                        Submarine.Load(string.Join(" ", commands.Skip(1)), true);
                    }
                    GameMain.EditMapScreen.Select();
                    break;
                case "editcharacter":
                case "editchar":
                    GameMain.EditCharacterScreen.Select();
                    break;
                case "controlcharacter":
                case "control":
                    {
                        if (commands.Length < 2) break;

                        var character = FindMatchingCharacter(commands, true);

                        if (character != null)
                        {
                            Character.Controlled = character;
                        }
                    }
                    break;
                case "setclientcharacter":
                    {
                        if (GameMain.Server == null) break;

                        int separatorIndex = Array.IndexOf(commands, ";");

                        if (separatorIndex == -1 || commands.Length < 4)
                        {
                            ThrowError("Invalid parameters. The command should be formatted as \"setclientcharacter [client] ; [character]\"");
                            break;
                        }

                        string[] commandsLeft = commands.Take(separatorIndex).ToArray();
                        string[] commandsRight = commands.Skip(separatorIndex).ToArray();

                        string clientName = String.Join(" ", commandsLeft.Skip(1));

                        var client = GameMain.Server.ConnectedClients.Find(c => c.name == clientName);
                        if (client == null)
                        {
                            ThrowError("Client \"" + clientName + "\" not found.");
                        }

                        var character = FindMatchingCharacter(commandsRight, false);
                        GameMain.Server.SetClientCharacter(client, character);
                    }
                    break;
                case "test":
                    Submarine.Load("aegir mark ii", true);
                    GameMain.DebugDraw = true;
                    GameMain.LightManager.LosEnabled = false;
                    GameMain.EditMapScreen.Select();
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
                case "tutorial":
                    TutorialMode.StartTutorial(Tutorials.TutorialType.TutorialTypes[0]);
                    break;
                case "editortutorial":
                    GameMain.EditMapScreen.Select();
                    GameMain.EditMapScreen.StartTutorial();
                    break;
                case "lobbyscreen":
                case "lobby":
                    GameMain.LobbyScreen.Select();
                    break;
                case "savemap":
                case "savesub":
                case "save":
                    if (commands.Length < 2) break;

                    if (GameMain.EditMapScreen.CharacterMode)
                    {
                        GameMain.EditMapScreen.ToggleCharacterMode();
                    }

                    string fileName = string.Join(" ", commands.Skip(1));
                    if (fileName.Contains("../"))
                    {
                        DebugConsole.ThrowError("Illegal symbols in filename (../)");
                        return true;
                    }

                    if (Submarine.SaveCurrent(System.IO.Path.Combine(Submarine.SavePath, fileName + ".sub")))
                    {
                        NewMessage("Sub saved", Color.Green);
                        //Submarine.Loaded.First().CheckForErrors();
                    }

                    break;
                case "loadmap":
                case "loadsub":
                case "load":
                    if (commands.Length < 2) break;

                    Submarine.Load(string.Join(" ", commands.Skip(1)), true);
                    break;
                case "cleansub":
                    for (int i = MapEntity.mapEntityList.Count - 1; i >= 0; i--)
                    {
                        MapEntity me = MapEntity.mapEntityList[i];

                        if (me.SimPosition.Length() > 2000.0f)
                        {
                            DebugConsole.NewMessage("Removed " + me.Name + " (simposition " + me.SimPosition + ")", Color.Orange);
                            MapEntity.mapEntityList.RemoveAt(i);
                        }
                        else if (me.MoveWithLevel)
                        {
                            DebugConsole.NewMessage("Removed " + me.Name + " (MoveWithLevel==true)", Color.Orange);
                            MapEntity.mapEntityList.RemoveAt(i);
                        }
                        else if (me is Item)
                        {
                            Item item = me as Item;
                            var wire = item.GetComponent<Wire>();
                            if (wire == null) continue;

                            if (wire.GetNodes().Count > 0 && !wire.Connections.Any(c => c != null))
                            {
                                wire.Item.Drop(null);
                                DebugConsole.NewMessage("Dropped wire (ID: " + wire.Item.ID + ") - attached on wall but no connections found", Color.Orange);
                            }
                        }

                    }
                    break;
                case "messagebox":
                    if (commands.Length < 3) break;
                    new GUIMessageBox(commands[1], commands[2]);
                    break;
                case "debugdraw":
                    GameMain.DebugDraw = !GameMain.DebugDraw;
                    break;
                case "disablehud":
                case "hud":
                    GUI.DisableHUD = !GUI.DisableHUD;
                    GameMain.Instance.IsMouseVisible = !GameMain.Instance.IsMouseVisible;
                    break;
                case "followsub":
                    Camera.FollowSub = !Camera.FollowSub;
                    break;
                case "drawaitargets":
                case "showaitargets":
                    AITarget.ShowAITargets = !AITarget.ShowAITargets;
                    break;
#if DEBUG
                case "spamevents":
                    foreach (Item item in Item.ItemList)
                    {
                        for (int i = 0; i<item.components.Count; i++)
                        {
                            if (item.components[i] is IServerSerializable)
                            {
                                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, i });
                            }
                            var itemContainer = item.GetComponent<ItemContainer>();
                            if (itemContainer != null)
                            {
                                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.InventoryState });
                            }

                            GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.Status });

                            item.NeedsPositionUpdate = true;
                        }
                    }

                    foreach (Character c in Character.CharacterList)
                    {
                        GameMain.Server.CreateEntityEvent(c, new object[] { NetEntityEvent.Type.Status });
                    }

                    foreach (Structure wall in Structure.WallList)
                    {
                        GameMain.Server.CreateEntityEvent(wall);
                    }
                    break;
                case "spamchatmessages":
                    int msgCount = 1000;
                    if (commands.Length > 1) int.TryParse(commands[1], out msgCount);
                    int msgLength = 50;
                    if (commands.Length > 2) int.TryParse(commands[2], out msgLength);

                    for (int i = 0; i < msgCount; i++)
                    {
                        if (GameMain.Server != null)
                        {
                            GameMain.Server.SendChatMessage(ToolBox.RandomSeed(msgLength), ChatMessageType.Default);
                        }
                        else
                        {
                            GameMain.Client.SendChatMessage(ToolBox.RandomSeed(msgLength));
                        }
                    }
                    break;
#endif
                case "cleanbuild":
                    GameMain.Config.MusicVolume = 0.5f;
                    GameMain.Config.SoundVolume = 0.5f;
                    DebugConsole.NewMessage("Music and sound volume set to 0.5", Color.Green);

                    GameMain.Config.GraphicsWidth = 0;
                    GameMain.Config.GraphicsHeight = 0;
                    GameMain.Config.WindowMode = WindowMode.Fullscreen;
                    DebugConsole.NewMessage("Resolution set to 0 x 0 (screen resolution will be used)", Color.Green);
                    DebugConsole.NewMessage("Fullscreen enabled", Color.Green);

                    GameSettings.VerboseLogging = false;

                    if (GameMain.Config.MasterServerUrl != "http://www.undertowgames.com/baromaster")
                    {
                        DebugConsole.ThrowError("MasterServerUrl \"" + GameMain.Config.MasterServerUrl + "\"!");
                    }

                    GameMain.Config.Save("config.xml");

                    var saveFiles = System.IO.Directory.GetFiles(SaveUtil.SaveFolder);

                    foreach (string saveFile in saveFiles)
                    {
                        System.IO.File.Delete(saveFile);
                        DebugConsole.NewMessage("Deleted " + saveFile, Color.Green);
                    }

                    if (System.IO.Directory.Exists(System.IO.Path.Combine(SaveUtil.SaveFolder, "temp")))
                    {
                        System.IO.Directory.Delete(System.IO.Path.Combine(SaveUtil.SaveFolder, "temp"), true);
                        DebugConsole.NewMessage("Deleted temp save folder", Color.Green);
                    }

                    if (System.IO.Directory.Exists(ServerLog.SavePath))
                    {
                        var logFiles = System.IO.Directory.GetFiles(ServerLog.SavePath);

                        foreach (string logFile in logFiles)
                        {
                            System.IO.File.Delete(logFile);
                            DebugConsole.NewMessage("Deleted " + logFile, Color.Green);
                        }
                    }

                    if (System.IO.File.Exists("filelist.xml"))
                    {
                        System.IO.File.Delete("filelist.xml");
                        DebugConsole.NewMessage("Deleted filelist", Color.Green);
                    }


                    if (System.IO.File.Exists("Submarines/TutorialSub.sub"))
                    {
                        System.IO.File.Delete("Submarines/TutorialSub.sub");

                        DebugConsole.NewMessage("Deleted TutorialSub from the submarine folder", Color.Green);
                    }

                    if (System.IO.File.Exists(GameServer.SettingsFile))
                    {
                        System.IO.File.Delete(GameServer.SettingsFile);
                        DebugConsole.NewMessage("Deleted server settings", Color.Green);
                    }

                    if (System.IO.File.Exists(GameServer.ClientPermissionsFile))
                    {
                        System.IO.File.Delete(GameServer.ClientPermissionsFile);
                        DebugConsole.NewMessage("Deleted client permission file", Color.Green);

                    }

                    if (System.IO.File.Exists("crashreport.txt"))
                    {
                        System.IO.File.Delete("crashreport.txt");
                        DebugConsole.NewMessage("Deleted crashreport.txt", Color.Green);
                    }

                    if (!System.IO.File.Exists("Content/Map/TutorialSub.sub"))
                    {
                        DebugConsole.ThrowError("TutorialSub.sub not found!");
                    }

                    break;
                default:
                    return false; //command not found
                    break;
            }
            return true; //command found
        }
    }
}
