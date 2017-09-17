using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

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
        class Command
        {
            public readonly string[] names;
            public readonly string help;
            private Action<string[]> onExecute;

            public Command(string name, string help, Action<string[]> onExecute)
            {
                names = name.Split('|');
                this.help = help;

                this.onExecute = onExecute;
            }

            public void Execute(string[] args)
            {
                onExecute(args);
            }
        }

        const int MaxMessages = 200;

        public static List<ColoredText> Messages = new List<ColoredText>();

        public delegate void QuestionCallback(string answer);
        private static QuestionCallback activeQuestionCallback;
#if CLIENT
        private static GUIComponent activeQuestionText;
#endif

        private static List<Command> commands = new List<Command>();
        
        private static string currentAutoCompletedCommand;
        private static int currentAutoCompletedIndex;

        //used for keeping track of the message entered when pressing up/down
        static int selectedIndex;

        static DebugConsole()
        {
            commands.Add(new Command("help", "", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    foreach (Command c in commands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }
                }
                else
                {
                    var matchingCommand = commands.Find(c => c.names.Any(name => name == args[0]));
                    if (matchingCommand == null)
                    {
                        NewMessage("Command " + args[0] + " not found.", Color.Red);
                    }
                    else
                    {
                        NewMessage(matchingCommand.help, Color.Cyan);
                    }
                }
            }));

            commands.Add(new Command("clientlist", "clientlist: List all the clients connected to the server.", (string[] args) => 
            {
                if (GameMain.Server == null) return;
                NewMessage("***************", Color.Cyan);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    NewMessage("- " + c.ID.ToString() + ": " + c.name + ", " + c.Connection.RemoteEndPoint.Address.ToString(), Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));


            commands.Add(new Command("createfilelist", "", (string[] args) =>
            {
                UpdaterUtil.SaveFileList("filelist.xml");
            }));

            commands.Add(new Command("spawn|spawncharacter", "spawn [creaturename] [near/inside/outside]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine).", (string[] args) =>
            {
                if (args.Length == 0) return;

                Character spawnedCharacter = null;

                Vector2 spawnPosition = Vector2.Zero;
                WayPoint spawnPoint = null;

                if (args.Length > 1)
                {
                    switch (args[1].ToLowerInvariant())
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
                            spawnPoint = WayPoint.GetRandom(args[0].ToLowerInvariant() == "human" ? SpawnType.Human : SpawnType.Enemy);
                            break;
                    }
                }
                else
                {
                    spawnPoint = WayPoint.GetRandom(args[0].ToLowerInvariant() == "human" ? SpawnType.Human : SpawnType.Enemy);
                }

                if (string.IsNullOrWhiteSpace(args[0])) return;

                if (spawnPoint != null) spawnPosition = spawnPoint.WorldPosition;

                if (args[0].ToLowerInvariant() == "human")
                {
                    spawnedCharacter = Character.Create(Character.HumanConfigFile, spawnPosition);

#if CLIENT
                    if (GameMain.GameSession != null)
                    {
                        SinglePlayerCampaign mode = GameMain.GameSession.GameMode as SinglePlayerCampaign;
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
                        + args[0].First().ToString().ToUpper() + args[0].Substring(1)
                        + "/" + args[0].ToLower() + ".xml", spawnPosition);
                }
            }));

            commands.Add(new Command("spawnitem", "spawnitem [itemname] [cursor/inventory]: Spawn an item at the position of the cursor, in the inventory of the controlled character or at a random spawnpoint if the last parameter is omitted.", (string[] args) =>
            {
                if (args.Length < 1) return;

                Vector2? spawnPos = null;
                Inventory spawnInventory = null;

                int extraParams = 0;
                switch (args.Last())
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

                string itemName = string.Join(" ", args.Take(args.Length - extraParams)).ToLowerInvariant();

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
                    Entity.Spawner.AddToSpawnQueue(itemPrefab, (Vector2)spawnPos);

                }
                else if (spawnInventory != null)
                {
                    Entity.Spawner.AddToSpawnQueue(itemPrefab, spawnInventory);
                }
            }));

            commands.Add(new Command("disablecrewai", "disablecrewai: Disable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled", Color.White);
            }));

            commands.Add(new Command("enablecrewai", "enablecrewai: Enable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled", Color.White);
            }));

            commands.Add(new Command("autorestartinterval", "autorestartinterval [seconds]: Set how long the server waits between rounds before automatically starting a new one.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    int parsedInt = 0;
                    if (int.TryParse(args[0], out parsedInt) && parsedInt >= 0)
                    {
                        GameMain.Server.AutoRestartInterval = parsedInt;
                        NewMessage("Autorestart interval set to " + GameMain.Server.AutoRestartInterval + " seconds.", Color.White);
                    }
                }
            }));


            commands.Add(new Command("autorestarttimer", "autorestarttimer [seconds]: Set the current autorestart countdown to the specified value.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    int parsedInt = 0;
                    if (int.TryParse(args[0], out parsedInt) && parsedInt >= 0)
                    {
                        GameMain.Server.AutoRestartTimer = parsedInt;
                        GameMain.NetLobbyScreen.LastUpdateID++;
                        NewMessage("Autorestart timer set to " + GameMain.Server.AutoRestartTimer + " seconds.", Color.White);
                    }
                }
            }));

            commands.Add(new Command("kick", "kick [name]: Kick a player out of the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;
                
                string playerName = string.Join(" ", args);

                ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"?", (reason) =>
                {
                    GameMain.NetworkMember.KickPlayer(playerName, reason);
                });                
            }));

            commands.Add(new Command("kickid", "kickid [id]: Kick the player with the specified client ID out of the server.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;

                int id = 0;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Reason for kicking \"" + client.name + "\"?", (reason) =>
                {
                    GameMain.Server.KickPlayer(client.name, reason);                    
                });
            }));

            commands.Add(new Command("ban", "ban [name]: Kick and ban the player from the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;
                
                string clientName = string.Join(" ", args);
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
            }));

            commands.Add(new Command("banid", "banid [id]: Kick and ban the player with the specified client ID from the server.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;

                int id = 0;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Reason for banning \"" + client.name + "\"?", (reason) =>
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
                });
            }));


            commands.Add(new Command("banip", "banip [ip]: Ban the IP address from the server.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                
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
                                ThrowError("\"" + duration + "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                return;
                            }
                            banDuration = parsedBanDuration;
                        }

                        var client = GameMain.Server.ConnectedClients.Find(c => c.Connection.RemoteEndPoint.Address.ToString() == args[0]);
                        if (client == null)
                        {
                            GameMain.Server.BanList.BanPlayer("Unnamed", args[0], reason, banDuration);
                        }
                        else
                        {
                            GameMain.Server.KickClient(client, reason);
                        }
                    });
                });
                
            }));

            commands.Add(new Command("teleportcharacter|teleport", "teleport [character name]: Teleport the specified character to the position of the cursor. If the name parameter is omitted, the controlled character will be teleported.", (string[] args) =>
            {
                Character tpCharacter = null; 

                if (args.Length == 0)
                {
                    tpCharacter = Character.Controlled;
                }
                else
                {
                    tpCharacter = FindMatchingCharacter(args, false);
                }

                if (tpCharacter == null) return;
                
                var cam = GameMain.GameScreen.Cam;
                tpCharacter.AnimController.CurrentHull = null;
                tpCharacter.Submarine = null;
                tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cam.ScreenToWorld(PlayerInput.MousePosition)));
                tpCharacter.AnimController.FindHull(cam.ScreenToWorld(PlayerInput.MousePosition), true);                
            }));

            commands.Add(new Command("godmode", "godmode: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                NewMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", Color.White);
            }));

            commands.Add(new Command("lockx", "lockx: Lock horizontal movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockX = !Submarine.LockX;
            }));

            commands.Add(new Command("locky", "loxky: Lock vertical movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockY = !Submarine.LockY;
            }));

            commands.Add(new Command("dumpids", "", (string[] args) =>
            {
                try
                {
                    int count = args.Length == 0 ? 10 : int.Parse(args[0]);
                    Entity.DumpIds(count);
                }
                catch (Exception e)
                {
                    ThrowError("Failed to dump ids", e);
                }
            }));

            commands.Add(new Command("heal", "heal [character name]: Restore the specified character to full health. If the name parameter is omitted, the controlled character will be healed.", (string[] args) =>
            {
                Character healedCharacter = null;
                if (args.Length == 0)
                {
                    healedCharacter = Character.Controlled;
                }
                else
                {
                    healedCharacter = FindMatchingCharacter(args);
                }

                if (healedCharacter != null)
                {
                    healedCharacter.AddDamage(CauseOfDeath.Damage, -healedCharacter.MaxHealth, null);
                    healedCharacter.Oxygen = 100.0f;
                    healedCharacter.Bleeding = 0.0f;
                    healedCharacter.SetStun(0.0f, true);
                }
            }));

            commands.Add(new Command("revive", "revive [character name]: Bring the specified character back from the dead. If the name parameter is omitted, the controlled character will be revived.", (string[] args) =>
            {
                Character revivedCharacter = null;
                if (args.Length == 0)
                {
                    revivedCharacter = Character.Controlled;
                }
                else
                {
                    revivedCharacter = FindMatchingCharacter(args);
                }

                if (revivedCharacter == null) return;
                
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
            }));

            commands.Add(new Command("freeze", "", (string[] args) =>
            {
                if (Character.Controlled != null) Character.Controlled.AnimController.Frozen = !Character.Controlled.AnimController.Frozen;
            }));

            commands.Add(new Command("freecamera|freecam", "freecam: Detach the camera from the controlled character.", (string[] args) =>
            {
                Character.Controlled = null;
                GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            }));

            commands.Add(new Command("water|editwater", "water/editwater: Toggle water editing. Allows adding water into rooms by holding the left mouse button and removing it by holding the right mouse button.", (string[] args) =>
            {
                if (GameMain.Client == null)
                {
                    Hull.EditWater = !Hull.EditWater;
                    NewMessage(Hull.EditWater ? "Water editing on" : "Water editing off", Color.White);
                }
            }));

            commands.Add(new Command("fire|editfire", "fire/editfire: Allows putting up fires by left clicking.", (string[] args) =>
            {
                if (GameMain.Client == null)
                {
                    Hull.EditFire = !Hull.EditFire;
                    NewMessage(Hull.EditFire ? "Fire spawning on" : "Fire spawning off", Color.White);
                }
            }));

            commands.Add(new Command("explosion", "explosion [range] [force] [damage] [structuredamage]: Creates an explosion at the position of the cursor.", (string[] args) =>
            {
                Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float range = 500, force = 10, damage = 50, structureDamage = 10;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                new Explosion(range, force, damage, structureDamage).Explode(explosionPos);
            }));

            commands.Add(new Command("fixitems", "fixitems: Repairs all items and restores them to full condition.", (string[] args) =>
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Condition = it.Prefab.Health;
                }
            }));

            commands.Add(new Command("fixhulls|fixwalls", "fixwalls/fixhulls: Fixes all walls.", (string[] args) =>
            {
                foreach (Structure w in Structure.WallList)
                {
                    for (int i = 0; i < w.SectionCount; i++)
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }
            }));

            commands.Add(new Command("power", "power [temperature]: Immediately sets the temperature of the nuclear reactor to the specified value.", (string[] args) =>
            {
                Item reactorItem = Item.ItemList.Find(i => i.GetComponent<Reactor>() != null);
                if (reactorItem == null) return;

                float power = 5000.0f;
                if (args.Length > 0) float.TryParse(args[0], out power);

                var reactor = reactorItem.GetComponent<Reactor>();
                reactor.ShutDownTemp = power == 0 ? 0 : 7000.0f;
                reactor.AutoTemp = true;
                reactor.Temperature = power;

                if (GameMain.Server != null)
                {
                    reactorItem.CreateServerEvent(reactor);
                }
            }));

            commands.Add(new Command("oxygen|air", "oxygen/air: Replenishes the oxygen levels in every room to 100%.", (string[] args) =>
            {
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                }
            }));

            commands.Add(new Command("killmonsters", "killmonsters: Immediately kills all AI-controlled enemies in the level.", (string[] args) =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!(c.AIController is EnemyAIController)) continue;
                    c.AddDamage(CauseOfDeath.Damage, 10000.0f, null);
                }
            }));

            commands.Add(new Command("netstats", "netstats: Toggles the visibility of the network statistics UI.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                GameMain.Server.ShowNetStats = !GameMain.Server.ShowNetStats;
            }));

            commands.Add(new Command("setclientcharacter", "setclientcharacter [client name] ; [character name]: Gives the client control of the specified character.", (string[] args) =>
            {
                if (GameMain.Server == null) return;

                int separatorIndex = Array.IndexOf(args, ";");

                if (separatorIndex == -1 || args.Length < 3)
                {
                    ThrowError("Invalid parameters. The command should be formatted as \"setclientcharacter [client] ; [character]\"");
                    return;
                }

                string[] argsLeft = args.Take(separatorIndex).ToArray();
                string[] argsRight = args.Skip(separatorIndex + 1).ToArray();

                string clientName = String.Join(" ", argsLeft);

                var client = GameMain.Server.ConnectedClients.Find(c => c.name == clientName);
                if (client == null)
                {
                    ThrowError("Client \"" + clientName + "\" not found.");
                }

                var character = FindMatchingCharacter(argsRight, false);
                GameMain.Server.SetClientCharacter(client, character);
            }));

            commands.Add(new Command("campaigninfo|campaignstatus", "campaigninfo: Display information about the state of the currently active campaign.", (string[] args) =>
            {
                var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                if (campaign == null)
                {
                    ThrowError("No campaign active!");
                    return;
                }

                campaign.LogState();
            }));

            commands.Add(new Command("campaigndestination|setcampaigndestination", "campaigndestination [index]: Set the location to head towards in the currently active campaign.", (string[] args) =>
            {
                var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                if (campaign == null)
                {
                    ThrowError("No campaign active!");
                    return;
                }

                if (args.Length == 0)
                {
                    int i = 0;
                    foreach (LocationConnection connection in campaign.Map.CurrentLocation.Connections)
                    {
                        NewMessage("     " + i + ". " + connection.OtherLocation(campaign.Map.CurrentLocation).Name, Color.White);
                        i++;
                    }
                    ShowQuestionPrompt("Select a destination (0 - " + (campaign.Map.CurrentLocation.Connections.Count - 1) + "):", (string selectedDestination) =>
                    {
                        int destinationIndex = -1;
                        if (!int.TryParse(selectedDestination, out destinationIndex)) return;
                        if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                        {
                            NewMessage("Index out of bounds!", Color.Red);
                            return;
                        }
                        Location location = campaign.Map.CurrentLocation.Connections[destinationIndex].OtherLocation(campaign.Map.CurrentLocation);
                        campaign.Map.SelectLocation(location);
                        NewMessage(location.Name+" selected.", Color.White);                        
                    });
                }
                else
                {
                    int destinationIndex = -1;
                    if (!int.TryParse(args[0], out destinationIndex)) return;
                    if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                    {
                        NewMessage("Index out of bounds!", Color.Red);
                        return;
                    }
                    Location location = campaign.Map.CurrentLocation.Connections[destinationIndex].OtherLocation(campaign.Map.CurrentLocation);
                    campaign.Map.SelectLocation(location);
                    NewMessage(location.Name + " selected.", Color.White);                    
                }
            }));

#if DEBUG
            commands.Add(new Command("spamevents", "A debug command that immediately creates entity events for all items, characters and structures.", (string[] args) =>
            {
                foreach (Item item in Item.ItemList)
                {
                    for (int i = 0; i < item.components.Count; i++)
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
            }));
#endif
            InitProjectSpecific();

            commands.Sort((c1, c2) => c1.names[0].CompareTo(c2.names[0]));
        }

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

        public static string AutoComplete(string command)
        {
            if (string.IsNullOrWhiteSpace(currentAutoCompletedCommand))
            {
                currentAutoCompletedCommand = command;
            }

            List<string> matchingCommands = new List<string>();
            foreach (Command c in commands)
            {
                foreach (string name in c.names)
                {
                    if (currentAutoCompletedCommand.Length > name.Length) continue;
                    if (currentAutoCompletedCommand == name.Substring(0, currentAutoCompletedCommand.Length))
                    {
                        matchingCommands.Add(name);
                    }
                }
            }

            if (matchingCommands.Count == 0) return command;

            currentAutoCompletedIndex = currentAutoCompletedIndex % matchingCommands.Count;
            return matchingCommands[currentAutoCompletedIndex++];
        }

        public static void ResetAutoComplete()
        {
            currentAutoCompletedCommand = "";
            currentAutoCompletedIndex = 0;
        }

        public static string SelectMessage(int direction)
        {
            if (Messages.Count == 0) return "";

            direction = MathHelper.Clamp(direction, -1, 1);

            selectedIndex += direction;
            if (selectedIndex < 0) selectedIndex = Messages.Count - 1;
            selectedIndex = selectedIndex % Messages.Count;

            return Messages[selectedIndex].Text;            
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

            string[] splitCommand = SplitCommand(command);
            
            if (!splitCommand[0].ToLowerInvariant().Equals("admin"))
            {
                NewMessage(command, Color.White);
            }

#if !DEBUG && CLIENT
            if (GameMain.Client != null && !IsCommandPermitted(splitCommand[0].ToLowerInvariant(), GameMain.Client))
            {
                ThrowError("You're not permitted to use the command \"" + splitCommand[0].ToLowerInvariant() + "\"!");
                return;
            }
#endif

            bool commandFound = false;
            foreach (Command c in commands)
            {
                if (c.names.Contains(splitCommand[0].ToLowerInvariant()))
                {
                    c.Execute(splitCommand.Skip(1).ToArray());
                    commandFound = true;
                    break;
                }
            }

            if (!commandFound)
            {
                ThrowError("Command \""+splitCommand[0]+"\" not found.");
            }
        }
        
        private static Character FindMatchingCharacter(string[] args, bool ignoreRemotePlayers = false)
        {
            if (args.Length == 0) return null;

            int characterIndex;
            string characterName;
            if (int.TryParse(args.Last(), out characterIndex) && args.Length > 1)
            {
                characterName = string.Join(" ", args.Take(args.Length - 1)).ToLowerInvariant();
            }
            else
            {
                characterName = string.Join(" ", args).ToLowerInvariant();
                characterIndex = -1;
            }

            var matchingCharacters = Character.CharacterList.FindAll(c => (!ignoreRemotePlayers || !c.IsRemotePlayer) && c.Name.ToLowerInvariant() == characterName);

            if (!matchingCharacters.Any())
            {
                NewMessage("Character \""+ characterName + "\" not found", Color.Red);
                return null;
            }

            if (characterIndex == -1)
            {
                if (matchingCharacters.Count > 1)
                {
                    NewMessage(
                        "Found multiple matching characters. " +
                        "Use \"[charactername] [0-" + (matchingCharacters.Count - 1) + "]\" to choose a specific character.",
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

        public static void ShowQuestionPrompt(string question, QuestionCallback onAnswered)
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
