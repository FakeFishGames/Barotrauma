using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Barotrauma.Items.Components;
using System.Text;
using FarseerPhysics;

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

    static partial class DebugConsole
    {
        const int MaxMessages = 200;

        public static List<ColoredText> Messages = new List<ColoredText>();

        private delegate void QuestionCallback(string answer);
        private static QuestionCallback activeQuestionCallback;
#if CLIENT
        private static GUIComponent activeQuestionText;
#endif

        private static string[] SplitCommand(string command)
        {
            command = command.Trim();

            List<string> commands = new List<string>();
            int escape = 0;
            bool inQuotes = false;
            string piece = "";
            
            for (int i = 0; i < command.Length; i++)
            {
                if (command[i] == '\\')
                {
                    if (escape == 0) escape = 2;
                    else piece += '\\';
                }
                else if (command[i] == '"')
                {
                    if (escape == 0) inQuotes = !inQuotes;
                    else piece += '"';
                }
                else if (command[i] == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrWhiteSpace(piece)) commands.Add(piece);
                    piece = "";
                }
                else if (escape == 0) piece += command[i];

                if (escape > 0) escape--;
            }

            if (!string.IsNullOrWhiteSpace(piece)) commands.Add(piece); //add final piece

            return commands.ToArray();
        }
        
        public static void ExecuteCommand(string command, GameMain game)
        {
            if (activeQuestionCallback != null)
            {
#if CLIENT
                activeQuestionText = null;
#endif
                NewMessage(command, Color.White);
                //reset the variable before invoking the delegate because the method may need to activate another question
                var temp = activeQuestionCallback;
                activeQuestionCallback = null;
                temp(command);
                return;
            }

            if (string.IsNullOrWhiteSpace(command)) return;

            string[] commands = SplitCommand(command);
            
            if (!commands[0].ToLowerInvariant().Equals("admin"))
            {
                NewMessage(command, Color.White);
            }

#if !DEBUG && CLIENT
            if (GameMain.Client != null && !IsCommandPermitted(commands[0].ToLowerInvariant(), GameMain.Client))
            {
                ThrowError("You're not permitted to use the command \"" + commands[0].ToLowerInvariant()+"\"!");
                return;
            }
#endif

            switch (commands[0].ToLowerInvariant())
            {
                case "clientlist":
                    if (GameMain.Server == null) break;
                    NewMessage("***************", Color.Cyan);
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        NewMessage("- " + c.ID.ToString() + ": " + c.name + ", " + c.Connection.RemoteEndPoint.Address.ToString(), Color.Cyan);
                    }
                    NewMessage("***************", Color.Cyan);
                    break;
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

                    NewMessage("teleport: teleport the controlled character to the position of the cursor", Color.Cyan);
                    NewMessage("teleport [character name]: teleport the specified character to the position of the cursor", Color.Cyan);
                    NewMessage("heal: restore the controlled character to full health", Color.Cyan);
                    NewMessage("heal [character name]: restore the specified character to full health", Color.Cyan);
                    NewMessage("revive: bring the controlled character back from the dead", Color.Cyan);
                    NewMessage("revive [character name]: bring the specified character back from the dead", Color.Cyan);
                    NewMessage("killmonsters: immediately kills all AI-controlled enemies in the level", Color.Cyan);

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
                                float closestDist = -1.0f;
                                foreach (WayPoint wp in WayPoint.WayPointList)
                                {
                                    if (wp.Submarine != null) continue;

                                    //don't spawn inside hulls
                                    if (Hull.FindHull(wp.WorldPosition, null) != null) continue;

                                    float dist = Vector2.Distance(wp.WorldPosition, GameMain.GameScreen.Cam.WorldViewCenter);

                                    if (closestDist < 0.0f || dist < closestDist)
                                    {
                                        spawnPoint = wp;
                                        closestDist = dist;
                                    }
                                }
                                break;
                            case "cursor":
                                spawnPosition = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                                break;
                            default:
                                spawnPoint = WayPoint.GetRandom(commands[1].ToLowerInvariant() == "human" ? SpawnType.Human : SpawnType.Enemy);
                                break;
                        }
                    }
                    else
                    {
                        spawnPoint = WayPoint.GetRandom(commands[1].ToLowerInvariant() == "human" ? SpawnType.Human : SpawnType.Enemy);
                    }

                    if (string.IsNullOrWhiteSpace(commands[1])) return;

                    if (spawnPoint != null) spawnPosition = spawnPoint.WorldPosition;

                    if (commands[1].ToLowerInvariant() == "human")
                    {
                        spawnedCharacter = Character.Create(Character.HumanConfigFile, spawnPosition);

#if CLIENT
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
#endif
                    }
                    else
                    {
                        spawnedCharacter = Character.Create(
                            "Content/Characters/"
                            + commands[1].First().ToString().ToUpper() + commands[1].Substring(1)
                            + "/" + commands[1].ToLower() + ".xml", spawnPosition);
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
                        ThrowError("Item \"" + itemName + "\" not found!");
                        return;
                    }

                    if (spawnPos == null && spawnInventory == null)
                    {
                        var wp = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                        spawnPos = wp == null ? Vector2.Zero : wp.WorldPosition;
                    }

                    if (spawnPos != null)
                    {
                        Item.Spawner.AddToSpawnQueue(itemPrefab, (Vector2)spawnPos);

                    }
                    else if (spawnInventory != null)
                    {
                        Item.Spawner.AddToSpawnQueue(itemPrefab, spawnInventory);
                    }

                    break;
                case "disablecrewai":
                    HumanAIController.DisableCrewAI = !HumanAIController.DisableCrewAI;
                    break;
                case "enablecrewai":
                    HumanAIController.DisableCrewAI = false;
                    break;
                /*case "admin":
                    if (commands.Length < 2) break;

                    if (GameMain.Server != null)
                    {
                        GameMain.Server.AdminAuthPass = commands[1];

                    }
                    else if (GameMain.Client != null)
                    {
                        GameMain.Client.RequestAdminAuth(commands[1]);
                    }
                    break;*/
                case "kick":
                    if (GameMain.NetworkMember != null && commands.Length >= 2)
                    {
                        string playerName = string.Join(" ", commands.Skip(1));

                        ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"?", (reason) =>
                        {
                            GameMain.NetworkMember.KickPlayer(playerName, reason);
                        });
                    }
                    break;
                case "kickid":
                case "banid":
                    if (GameMain.Server != null && commands.Length >= 2)
                    {
                        bool ban = commands[0].ToLowerInvariant() == "banid";

                        int id = 0;
                        int.TryParse(commands[1], out id);
                        var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                        if (client == null)
                        {
                            ThrowError("Client id \"" + id + "\" not found.");
                            return;
                        }

                        ShowQuestionPrompt(ban ? "Reason for banning \"" + client.name + "\"?" : "Reason for kicking \"" + client.name + "\"?", (reason) =>
                        {
                            if (ban)
                            {
                                ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\")", (duration) =>
                                {
                                    TimeSpan? banDuration = null;
                                    if (!string.IsNullOrWhiteSpace(duration))
                                    {
                                        TimeSpan parsedBanDuration;
                                        if (!TryParseTimeSpan(duration, out parsedBanDuration))
                                        {
                                            ThrowError("\"" + duration + "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                            return;
                                        }
                                        banDuration = parsedBanDuration;
                                    }

                                    GameMain.Server.BanPlayer(client.name, reason, false, banDuration);
                                });
                            }
                            else
                            {
                                GameMain.Server.KickPlayer(client.name, reason);
                            }
                        });
                    }
                    break;
                case "ban":
                    if (GameMain.NetworkMember != null || commands.Length >= 2)
                    {
                        string clientName = string.Join(" ", commands.Skip(1));
                        ShowQuestionPrompt("Reason for banning \"" + clientName + "\"?", (reason) =>
                        {
                            ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\")", (duration) =>
                            {
                                TimeSpan? banDuration = null;
                                if (!string.IsNullOrWhiteSpace(duration))
                                {
                                    TimeSpan parsedBanDuration;
                                    if (!TryParseTimeSpan(duration, out parsedBanDuration))
                                    {
                                        ThrowError("\"" + duration + "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                        return;
                                    }
                                    banDuration = parsedBanDuration;
                                }

                                GameMain.NetworkMember.BanPlayer(clientName, reason, false, banDuration);
                            });
                        });
                    }            
                    break;
                case "banip":                    
                    if (GameMain.Server != null || commands.Length >= 2)
                    {
                        ShowQuestionPrompt("Reason for banning the ip \"" + commands[1] + "\"?", (reason) =>
                        {
                            ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\")", (duration) =>
                            {
                                TimeSpan? banDuration = null;
                                if (!string.IsNullOrWhiteSpace(duration))
                                {
                                    TimeSpan parsedBanDuration;
                                    if (!TryParseTimeSpan(duration, out parsedBanDuration))
                                    {
                                        ThrowError("\""+duration+ "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                        return;
                                    }
                                    banDuration = parsedBanDuration;
                                }

                                var client = GameMain.Server.ConnectedClients.Find(c => c.Connection.RemoteEndPoint.Address.ToString() == commands[1]);
                                if (client == null)
                                {
                                    GameMain.Server.BanList.BanPlayer("Unnamed", commands[1], reason, banDuration);
                                }
                                else
                                {
                                    GameMain.Server.KickClient(client, reason);
                                }
                            });
                        });
                    }                              
                    break;
                case "teleportcharacter":
                case "teleport":
                    var tpCharacter = FindMatchingCharacter(commands, false);

                    if (commands.Length < 2)
                    {
                        tpCharacter = Character.Controlled;
                    }

                    if (tpCharacter != null)
                    {
                        var cam = GameMain.GameScreen.Cam;
                        tpCharacter.AnimController.CurrentHull = null;
                        tpCharacter.Submarine = null;
                        tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition)));
                        tpCharacter.AnimController.FindHull(cam.ScreenToWorld(PlayerInput.MousePosition), true);
                    }
                    break;
                case "godmode":
                    if (Submarine.MainSub == null) return;

                    Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                    NewMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", Color.White);
                    break;
                case "lockx":
                    Submarine.LockX = !Submarine.LockX;
                    break;
                case "locky":
                    Submarine.LockY = !Submarine.LockY;
                    break;
                case "dumpids":
                    try
                    {
                        int count = commands.Length < 2 ? 10 : int.Parse(commands[1]);
                        Entity.DumpIds(count);
                    }
                    catch
                    {
                        return;
                    }
                    break;
                case "heal":
                    Character healedCharacter = null;
                    if (commands.Length == 1)
                    {
                        healedCharacter = Character.Controlled;
                    }
                    else
                    {
                        healedCharacter = FindMatchingCharacter(commands);
                    }

                    if (healedCharacter != null)
                    {
                        healedCharacter.AddDamage(CauseOfDeath.Damage, -healedCharacter.MaxHealth, null);
                        healedCharacter.Oxygen = 100.0f;
                        healedCharacter.Bleeding = 0.0f;
                        healedCharacter.SetStun(0.0f, true);
                    }

                    break;
                case "revive":
                    Character revivedCharacter = null;
                    if (commands.Length == 1)
                    {
                        revivedCharacter = Character.Controlled;
                    }
                    else
                    {
                        revivedCharacter = FindMatchingCharacter(commands);
                    }

                    if (revivedCharacter != null)
                    {
                        revivedCharacter.Revive(false);
                        if (GameMain.Server != null)
                        {
                            foreach (Client c in GameMain.Server.ConnectedClients)
                            {
                                if (c.Character != revivedCharacter) continue;
                                //clients stop controlling the character when it dies, force control back
                                GameMain.Server.SetClientCharacter(c, revivedCharacter);
                                break;
                            }
                        }
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
                    if (GameMain.Client == null)
                    {
                        Hull.EditWater = !Hull.EditWater;
                        NewMessage(Hull.EditWater ? "Water editing on" : "Water editing off", Color.White);
                    }                   

                    break;
                case "explosion":
                    Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    float range = 500, force = 10, damage=50;
                    if (commands.Length > 1) float.TryParse(commands[1], out range);
                    if (commands.Length > 2) float.TryParse(commands[2], out force);
                    if (commands.Length > 3) float.TryParse(commands[3], out damage);
                    new Explosion(range, force, damage, damage).Explode(explosionPos);
                    break;
                case "fire":
                    if (GameMain.Client == null)
                    {
                        Hull.EditFire = !Hull.EditFire;
                        NewMessage(Hull.EditWater ? "Fire spawning on" : "Fire spawning off", Color.White);
                    }
                    
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

                    if (GameMain.Server != null)
                    {
                        reactorItem.CreateServerEvent(reactor);
                    }
                    break;
                case "oxygen":
                case "air":
                    foreach (Hull hull in Hull.hullList)
                    {
                        hull.OxygenPercentage = 100.0f;
                    }
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

                default:
                    if (!ExecProjSpecific(commands)) NewMessage("Command not found", Color.Red);
                    break;
            }
        }
        
        private static Character FindMatchingCharacter(string[] commands, bool ignoreRemotePlayers = false)
        {
            if (commands.Length < 2) return null;

            int characterIndex;
            string characterName;
            if (int.TryParse(commands.Last(), out characterIndex) && commands.Length > 2)
            {
                characterName = string.Join(" ", commands.Skip(1).Take(commands.Length - 2)).ToLowerInvariant();
            }
            else
            {
                characterName = string.Join(" ", commands.Skip(1)).ToLowerInvariant();
                characterIndex = -1;
            }

            var matchingCharacters = Character.CharacterList.FindAll(c => (!ignoreRemotePlayers || !c.IsRemotePlayer) && c.Name.ToLowerInvariant() == characterName);

            if (!matchingCharacters.Any())
            {
                NewMessage("Matching characters not found", Color.Red);
                return null;
            }

            if (characterIndex == -1)
            {
                if (matchingCharacters.Count > 1)
                {
                    NewMessage(
                        "Found multiple matching characters. " +
                        "Use \"" + commands[0] + " [charactername] [0-" + (matchingCharacters.Count - 1) + "]\" to choose a specific character.",
                        Color.LightGray);
                }
                return matchingCharacters[0];
            }
            else if (characterIndex < 0 || characterIndex >= matchingCharacters.Count)
            {
                ThrowError("Character index out of range. Select an index between 0 and " + (matchingCharacters.Count - 1));
            }
            else
            {
                return matchingCharacters[characterIndex];
            }

            return null;
        }

        public static void NewMessage(string msg, Color color)
        {
            if (string.IsNullOrEmpty((msg))) return;

#if SERVER
            Messages.Add(new ColoredText(msg, color));

            //TODO: REMOVE
            Console.ForegroundColor = XnaToConsoleColor.Convert(color);
            Console.WriteLine(msg);
            Console.ForegroundColor = ConsoleColor.White;

            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }
#elif CLIENT
            lock (queuedMessages)
            {
                queuedMessages.Enqueue(new ColoredText(msg, color));
            }
#endif
        }

        private static void ShowQuestionPrompt(string question, QuestionCallback onAnswered)
        {
            NewMessage("   >>" + question, Color.Cyan);
            activeQuestionCallback += onAnswered;
#if CLIENT
            if (listBox != null && listBox.children.Count > 0)
            {
                activeQuestionText = listBox.children[listBox.children.Count - 1];
            }
#endif
        }

        private static bool TryParseTimeSpan(string s, out TimeSpan timeSpan)
        {
            timeSpan = new TimeSpan();
            if (string.IsNullOrWhiteSpace(s)) return false;

            string currNum = "";
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                {
                    currNum += c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    continue;
                }
                else
                {
                    int parsedNum = 0;
                    if (!int.TryParse(currNum, out parsedNum))
                    {
                        return false;
                    }

                    switch (c)
                    {
                        case 'd':
                            timeSpan += new TimeSpan(parsedNum, 0, 0, 0, 0);
                            break;
                        case 'h':
                            timeSpan += new TimeSpan(0, parsedNum, 0, 0, 0);
                            break;
                        case 'm':
                            timeSpan += new TimeSpan(0, 0, parsedNum, 0, 0);
                            break;
                        case 's':
                            timeSpan += new TimeSpan(0, 0, 0, parsedNum, 0);
                            break;
                        default:
                            return false;
                    }

                    currNum = "";
                }
            }

            return true;
        }


        public static void Log(string message)
        {
            if (GameSettings.VerboseLogging) NewMessage(message, Color.Gray);
        }

        public static void ThrowError(string error, Exception e = null)
        {
            if (e != null)
            {
                error += " {" + e.Message + "}\n" + e.StackTrace;
            }
            System.Diagnostics.Debug.WriteLine(error);
            NewMessage(error, Color.Red);
#if CLIENT
            isOpen = true;
#endif
        }
    }
}
