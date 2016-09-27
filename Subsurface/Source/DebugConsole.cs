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
        const int MaxMessages = 100;

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

            listBox = new GUIListBox(new Rectangle(0, 0, 0, frame.Rect.Height - 40), Color.Black, GUI.Style, frame);
            listBox.Color = Color.Black * 0.7f;

            textBox = new GUITextBox(new Rectangle(0,0,0,20), Color.Black, Color.White, Alignment.BottomLeft, Alignment.Left, GUI.Style, frame);
            textBox.Color = Color.Black * 0.7f;

            NewMessage("Press F3 to open/close the debug console", Color.Cyan);
            NewMessage("Enter \"help\" for a list of available console commands", Color.Cyan);

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

                if (PlayerInput.KeyDown(Keys.Enter) && textBox.Text != "")
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
                    return true;
                default:
                    return false;
            }
        }

        public static void ExecuteCommand(string command, GameMain game)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            string[] commands = command.Split(' ');


#if !DEBUG
            if (GameMain.Client != null && !IsCommandPermitted(commands[0].ToLowerInvariant(), GameMain.Client))
            {
                ThrowError("You're not permitted to use the command \"" + commands[0].ToLowerInvariant()+"\"!");
                return;
            }
#endif

            switch (commands[0].ToLowerInvariant())
            {
                case "help":
                    NewMessage("menu: go to main menu", Color.Cyan);
                    NewMessage("game: enter the \"game screen\"", Color.Cyan);
                    NewMessage("edit: switch to submarine editor", Color.Cyan);
                    NewMessage("edit [submarine name]: load a submarine and switch to submarine editor", Color.Cyan);
                    NewMessage("load [submarine name]: load a submarine", Color.Cyan);
                    NewMessage("save [submarine name]: save the current submarine using the specified name", Color.Cyan);

                    NewMessage(" ", Color.Cyan);                    

                    NewMessage("spawn [creaturename] [near/inside/outside]: spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine)", Color.Cyan);
                    NewMessage("spawnitem [itemname] [cursor/inventory]: spawn an item at the position of the cursor, in the inventory of the controlled character or at a random spawnpoint if the last parameter is omitted", Color.Cyan);

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
                    NewMessage("revive: bring the controlled character back from the dead", Color.Cyan);

                    NewMessage(" ", Color.Cyan);
                    
                    NewMessage("fixwalls: fixes all the walls", Color.Cyan);
                    NewMessage("fixitems: fixes every item/device in the sub", Color.Cyan);
                    NewMessage("oxygen: replenishes the oxygen in every room to 100%", Color.Cyan);
                    NewMessage("power [amount]: immediately sets the temperature of the reactor to the specified value", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("kick [name]: kick a player out from the server", Color.Cyan);
                    NewMessage("ban [name]: kick and ban the player from the server", Color.Cyan);
                    NewMessage("banip [IP address]: ban the IP address from the server", Color.Cyan);
                    NewMessage("debugdraw: toggles the \"debug draw mode\"", Color.Cyan);
                    NewMessage("netstats: toggles the visibility of the network statistics panel", Color.Cyan);
                    
                    break;
                case "createfilelist":
                    UpdaterUtil.SaveFileList("filelist.xml");
                    break;
                case "spawn":
                case "spawncharacter":
                    if (commands.Length == 1) return;

                    Character spawnedCharacter = null;

                    Vector2 spawnPosition = Vector2.Zero;
                    WayPoint spawnPoint = null;

                    if (commands.Length > 2)
                    {
                        switch (commands[2].ToLowerInvariant())
                        {
                            case "inside":
                                spawnPoint = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                                break;
                            case "outside":
                                spawnPoint = WayPoint.GetRandom(SpawnType.Enemy);
                                break;
                            case "near":
                            case "close":
                                float closestDist = 0.0f;
                                foreach (WayPoint wp in WayPoint.WayPointList)
                                {
                                    if (wp.Submarine != null) continue;

                                    //don't spawn inside hulls
                                    if (Hull.FindHull(wp.WorldPosition, null) != null) continue;

                                    float dist = Vector2.Distance(wp.WorldPosition, GameMain.GameScreen.Cam.WorldViewCenter);

                                    if (spawnPoint == null || dist < closestDist)
                                    {
                                        spawnPoint = wp;
                                        closestDist = dist;
                                    }
                                }
                                break;
                            default:
                                spawnPoint = WayPoint.GetRandom(commands[1].ToLowerInvariant()=="human" ? SpawnType.Human : SpawnType.Enemy);
                                break;
                        }

                    }
                    else
                    {
                        spawnPoint = WayPoint.GetRandom(commands[1].ToLowerInvariant() == "human" ? SpawnType.Human : SpawnType.Enemy);
                    }

                    spawnPosition = spawnPoint == null ? Vector2.Zero : spawnPoint.WorldPosition;

                    if (commands[1].ToLowerInvariant()=="human")
                    {
                        spawnedCharacter = Character.Create(Character.HumanConfigFile, spawnPosition);                        

                        if (GameMain.GameSession != null)
                        {
                            SinglePlayerMode mode = GameMain.GameSession.gameMode as SinglePlayerMode;
                            if (mode != null)
                            {
                                Character.Controlled = spawnedCharacter;
                                GameMain.GameSession.CrewManager.AddCharacter(Character.Controlled);
                                GameMain.GameSession.CrewManager.SelectCharacter(null, Character.Controlled);
                            }
                        }
                    }
                    else
                    {
                        spawnedCharacter = Character.Create("Content/Characters/" + commands[1] + "/" + commands[1] + ".xml", spawnPosition);
                    }

                    if (spawnedCharacter != null && GameMain.Server != null)
                    {
                        spawnedCharacter.SpawnedMidRound = true;
                    }

                    break;
                case "spawnitem":
                    if (commands.Length < 2) return;
                    
                    Vector2? spawnPos = null;
                    Inventory spawnInventory = null;

                    int extraParams = 0;
                    switch (commands.Last())
                    {
                        case "cursor":
                            extraParams = 1;
                            spawnPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                            break;
                        case "inventory":
                            extraParams = 1;
                            spawnInventory = Character.Controlled == null ? null : Character.Controlled.Inventory;
                            break;
                        default:
                            extraParams = 0;
                            break;
                    }

                    string itemName = string.Join(" ", commands.Skip(1).Take(commands.Length - extraParams - 1)).ToLowerInvariant();

                    var itemPrefab = MapEntityPrefab.list.Find(ip => ip.Name.ToLowerInvariant() == itemName) as ItemPrefab;
                    if (itemPrefab == null)
                    {
                        ThrowError("Item \""+itemName+"\" not found!");
                        return;
                    }

                    if (spawnPos == null && spawnInventory == null)
                    {
                        var wp = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                        spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
                    }

                    if (spawnPos != null)
                    {
                        Item.Spawner.QueueItem(itemPrefab, (Vector2)spawnPos, false);

                    }
                    else if (spawnInventory != null)
                    {
                        Item.Spawner.QueueItem(itemPrefab, (Inventory)spawnInventory, false);
                    }

                    break;
                case "disablecrewai":
                    HumanAIController.DisableCrewAI = !HumanAIController.DisableCrewAI;
                    break;
                case "enablecrewai":
                    HumanAIController.DisableCrewAI = false;
                    break;
                case "kick":
                    if (GameMain.NetworkMember == null || commands.Length < 2) break;
                    GameMain.NetworkMember.KickPlayer(string.Join(" ", commands.Skip(1)), false);

                    break;
                case "ban":
                    if (GameMain.NetworkMember == null || commands.Length < 2) break;
                    GameMain.NetworkMember.KickPlayer(string.Join(" ", commands.Skip(1)), true);
               
                    break;
                case "banip":
                    if (GameMain.Server == null || commands.Length < 2) break;

                    var client = GameMain.Server.ConnectedClients.Find(c => c.Connection.RemoteEndPoint.Address.ToString() == commands[1]);
                    if (client == null)
                    {
                        GameMain.Server.BanList.BanPlayer("Unnamed", commands[1]);
                    }
                    else
                    {
                        //GameMain.Server.KickClient(client, true);   
                    }                    
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
                    if (commands.Length>1)
                    {
                        Submarine.Load(string.Join(" ", commands.Skip(1)), true);
                    }
                    GameMain.EditMapScreen.Select();
                    break;
                case "test":
                    Submarine.Load("aegir mark ii", true);
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

                    int characterIndex;
                    string characterName;
                    if (int.TryParse(commands.Last(), out characterIndex))
                    {
                        characterName = string.Join(" ", commands.Skip(1).Take(commands.Length-2)).ToLowerInvariant();
                    }
                    else
                    {
                        characterName = string.Join(" ", commands.Skip(1)).ToLowerInvariant();
                        characterIndex = -1;
                    }

                    var matchingCharacters = Character.CharacterList.FindAll(c => !c.IsNetworkPlayer && c.Name.ToLowerInvariant() == characterName);

                    if (!matchingCharacters.Any())
                    {
                        ThrowError("Matching characters not found");
                        return;
                    }

                    if (characterIndex==-1)
                    {
                        Character.Controlled = matchingCharacters.First();
                        if (matchingCharacters.Count > 1)
                        {
                            NewMessage(
                                "Found multiple matching characters. "+
                                "Use \"control [charactername] [0-"+(matchingCharacters.Count-1)+"]\" to choose which character to control.", 
                                Color.LightGray);
                        }
                    }
                    else if (characterIndex<0 || characterIndex>= matchingCharacters.Count)
                    {
                        ThrowError("Character index out of range. Select an index between 0 and " + (matchingCharacters.Count - 1));
                    }
                    else
                    {
                        Character.Controlled = matchingCharacters[characterIndex];
                    }
                    
                    break;
                case "godmode":
                    if (Submarine.MainSub == null) return;

                    Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                    break;
                case "dumpids":
                    int count = commands.Length < 2 ? 10 : int.Parse(commands[1]);

                    Entity.DumpIds(count);
                    break;
                case "heal":
                    if (Character.Controlled != null)
                    {
                        Character.Controlled.AddDamage(CauseOfDeath.Damage, -Character.Controlled.MaxHealth, null);
                        Character.Controlled.Oxygen = 100.0f;
                        Character.Controlled.Bleeding = 0.0f;
                        Character.Controlled.AnimController.StunTimer = 0.0f;
                    }
                    break;
                case "revive":
                    if (Character.Controlled != null)
                    {
                        Character.Controlled.Revive(false);
                    }
                    break;
                case "freeze":
                    if (Character.Controlled != null) Character.Controlled.AnimController.Frozen = !Character.Controlled.AnimController.Frozen;
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
                    reactor.ShutDownTemp = power == 0 ? 0 : 7000.0f;
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
                        return;
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

                        if (me.SimPosition.Length()>2000.0f)
                        {
                            DebugConsole.NewMessage("Removed "+me.Name+" (simposition "+me.SimPosition+")", Color.Orange);
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

                            if (wire.Nodes.Any() && !wire.Connections.Any(c => c != null))
                            {
                                wire.Item.Drop(null);
                                DebugConsole.NewMessage("Dropped wire (ID: "+wire.Item.ID+") - attached on wall but no connections found", Color.Orange);
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
                case "drawaitargets":
                case "showaitargets":
                    AITarget.ShowAITargets = !AITarget.ShowAITargets;
                    break;
                case "killmonsters":
                    foreach (Character c in Character.CharacterList)
                    {
                        if (!(c.AIController is EnemyAIController)) continue;
                        c.AddDamage(CauseOfDeath.Damage, 10000.0f, null);
                    }
                    break;
                case "netstats":
                    if (GameMain.Server == null) return;

                    GameMain.Server.ShowNetStats = !GameMain.Server.ShowNetStats;
                    break;
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
                        DebugConsole.ThrowError("MasterServerUrl \""+GameMain.Config.MasterServerUrl+"\"!");
                    }

                    GameMain.Config.Save("config.xml");

                    var saveFiles = System.IO.Directory.GetFiles(SaveUtil.SaveFolder);

                    foreach (string saveFile in saveFiles)
                    {
                        System.IO.File.Delete(saveFile);
                        DebugConsole.NewMessage("Deleted "+saveFile, Color.Green);
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
                            DebugConsole.NewMessage("Deleted "+logFile, Color.Green);
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
                var textBlock = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width, 0), msg, GUI.Style, Alignment.TopLeft, Alignment.Left, null, true, GUI.SmallFont);
                textBlock.CanBeFocused = false;
                textBlock.TextColor = color;

                listBox.AddChild(textBlock);
                listBox.BarScroll = 1.0f;
            }
            catch
            {
                return;
            }

            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }

            //messages.Add(new ColoredText(msg, color));

            selectedIndex = listBox.children.Count;
        }

        public static void Log(string message)
        {
            if (GameSettings.VerboseLogging) NewMessage(message, Color.Gray);
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
