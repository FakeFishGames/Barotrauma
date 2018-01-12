using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    struct ColoredText
    {
        public string Text;
        public Color Color;
		public bool IsCommand;

        public readonly string Time;

        public ColoredText(string text, Color color, bool isCommand)
        {
            this.Text = text;
            this.Color = color;
			this.IsCommand = isCommand;

            Time = DateTime.Now.ToString();
        }
    }

    static partial class DebugConsole
    {
        public class Command
        {
            public readonly string[] names;
            public readonly string help;
            
            private Action<string[]> onExecute;

            /// <summary>
            /// Executed when a client uses the command. If not set, the command is relayed to the server as-is.
            /// </summary>
            private Action<string[]> onClientExecute;

            /// <summary>
            /// Executed server-side when a client attempts to use the command.
            /// </summary>
            private Action<Client, Vector2, string[]> onClientRequestExecute;

            public Func<string[][]> GetValidArgs;

            public bool RelayToServer
            {
                get { return onClientExecute == null; }
            }

            /// <param name="name">The name of the command. Use | to give multiple names/aliases to the command.</param>
            /// <param name="help">The text displayed when using the help command.</param>
            /// <param name="onExecute">The default action when executing the command.</param>
            /// <param name="onClientExecute">The action when a client attempts to execute the command. If null, the command is relayed to the server as-is.</param>
            /// <param name="onClientRequestExecute">The server-side action when a client requests executing the command. If null, the default action is executed.</param>
            public Command(string name, string help, Action<string[]> onExecute, Action<string[]> onClientExecute, Action<Client, Vector2, string[]> onClientRequestExecute, Func<string[][]> getValidArgs = null)
            {
                names = name.Split('|');
                this.help = help;

                this.onExecute = onExecute;
                this.onClientExecute = onClientExecute;
                this.onClientRequestExecute = onClientRequestExecute;

                this.GetValidArgs = getValidArgs;
            }
            

            /// <summary>
            /// Use this constructor to create a command that executes the same action regardless of whether it's executed by a client or the server.
            /// </summary>
            public Command(string name, string help, Action<string[]> onExecute, Func<string[][]> getValidArgs = null)
            {
                names = name.Split('|');
                this.help = help;

                this.onExecute = onExecute;
                this.onClientExecute = onExecute;

                this.GetValidArgs = getValidArgs;
            }

            public void Execute(string[] args)
            {
                onExecute(args);
            }

            public void ClientExecute(string[] args)
            {
                onClientExecute(args);
            }

            public void ServerExecuteOnClientRequest(Client client, Vector2 cursorWorldPos, string[] args)
            {
                if (onClientRequestExecute == null)
                {
                    onExecute(args);
                }
                else
                {
                    onClientRequestExecute(client, cursorWorldPos, args);
                }
            }
        }

        const int MaxMessages = 200;

        public static List<ColoredText> Messages = new List<ColoredText>();

        public delegate void QuestionCallback(string answer);
        private static QuestionCallback activeQuestionCallback;

        private static List<Command> commands = new List<Command>();
        public static List<Command> Commands
        {
            get { return commands; }
        }
        
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
                    NewMessage("- " + c.ID.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Connection.RemoteEndPoint.Address.ToString(), Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }, null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.ID.ToString() + ": " + c.Name + ", " + c.Connection.RemoteEndPoint.Address.ToString(), client);
                }
                GameMain.Server.SendConsoleMessage("***************", client);
            }));

            commands.Add(new Command("traitorlist", "traitorlist: List all the traitors and their targets.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null) return;
                foreach (Traitor t in traitorManager.TraitorList)
                {
                    NewMessage("- Traitor " + t.Character.Name + "'s target is " + t.TargetCharacter.Name + ".", Color.Cyan);
                }
                NewMessage("The code words are: " + traitorManager.codeWords + ", response: " + traitorManager.codeResponse + ".", Color.Cyan);
            }));

            commands.Add(new Command("itemlist", "itemlist: List all the item prefabs available for spawning.", (string[] args) =>
            {
                NewMessage("***************", Color.Cyan);
                foreach (MapEntityPrefab ep in MapEntityPrefab.List)
                {
                    var itemPrefab = ep as ItemPrefab;
                    if (itemPrefab == null || itemPrefab.Name == null) continue;
                    NewMessage("- " + itemPrefab.Name, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));

            commands.Add(new Command("createfilelist", "", (string[] args) =>
            {
                UpdaterUtil.SaveFileList("filelist.xml");
            }));

            commands.Add(new Command("spawn|spawncharacter", "spawn [creaturename] [near/inside/outside/cursor]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine).", (string[] args) =>
            {
                string errorMsg;
                SpawnCharacter(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), out errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            }, 
            null,
            (Client client, Vector2 cursorPos, string[] args) =>
            {
                string errorMsg;
                SpawnCharacter(args, cursorPos, out errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            },
            () => 
            {
                List<string> characterFiles = GameMain.Config.SelectedContentPackage.GetFilesOfType(ContentType.Character);
                for (int i = 0; i < characterFiles.Count; i++)
                {
                    characterFiles[i] = Path.GetFileNameWithoutExtension(characterFiles[i]).ToLowerInvariant();
                }

                return new string[][]
                {
                    characterFiles.ToArray(),
                    new string[] { "near", "inside", "outside", "cursor" }
                };
            }));

            commands.Add(new Command("spawnitem", "spawnitem [itemname] [cursor/inventory]: Spawn an item at the position of the cursor, in the inventory of the controlled character or at a random spawnpoint if the last parameter is omitted.",
            (string[] args) =>
            {
                string errorMsg;
                SpawnItem(args, GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), out errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                string errorMsg;
                SpawnItem(args, cursorWorldPos, out errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            }, 
            () =>
            {
                List<string> itemNames = new List<string>();
                foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
                {
                    ItemPrefab itemPrefab = prefab as ItemPrefab;
                    if (itemPrefab != null) itemNames.Add(itemPrefab.Name);
                }

                return new string[][]
                {
                    itemNames.ToArray(),
                    new string[] { "cursor", "inventory" }
                };
            }));

            commands.Add(new Command("disablecrewai", "disablecrewai: Disable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled", Color.White);
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled by \"" + client.Name + "\"", Color.White);
                GameMain.Server.SendConsoleMessage("Crew AI disabled", client);
            }));

            commands.Add(new Command("enablecrewai", "enablecrewai: Enable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled", Color.White);
            }, 
            null, 
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled by \"" + client.Name + "\"", Color.White);
                GameMain.Server.SendConsoleMessage("Crew AI enabled", client);
            }));

            commands.Add(new Command("autorestart", "autorestart [true/false]: Enable or disable round auto-restart.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                bool enabled = GameMain.Server.AutoRestart;
                if (args.Length > 0)
                {
                    bool.TryParse(args[0], out enabled);
                }
                else
                {
                    enabled = !enabled;
                }
                if (enabled != GameMain.Server.AutoRestart)
                {
                    if (GameMain.Server.AutoRestartInterval <= 0) GameMain.Server.AutoRestartInterval = 10;
                    GameMain.Server.AutoRestartTimer = GameMain.Server.AutoRestartInterval;
                    GameMain.Server.AutoRestart = enabled;
#if CLIENT
                    GameMain.NetLobbyScreen.SetAutoRestart(enabled, GameMain.Server.AutoRestartTimer);
#endif
                    GameMain.NetLobbyScreen.LastUpdateID++;
                }
                NewMessage(GameMain.Server.AutoRestart ? "Automatic restart enabled." : "Automatic restart disabled.", Color.White);
            }, null, null));
            
            commands.Add(new Command("autorestartinterval", "autorestartinterval [seconds]: Set how long the server waits between rounds before automatically starting a new one. If set to 0, autorestart is disabled.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    int parsedInt = 0;
                    if (int.TryParse(args[0], out parsedInt))
                    {
                        if (parsedInt >= 0)
                        {
                            GameMain.Server.AutoRestart = true;
                            GameMain.Server.AutoRestartInterval = parsedInt;
                            if (GameMain.Server.AutoRestartTimer >= GameMain.Server.AutoRestartInterval) GameMain.Server.AutoRestartTimer = GameMain.Server.AutoRestartInterval;
                            NewMessage("Autorestart interval set to " + GameMain.Server.AutoRestartInterval + " seconds.", Color.White);
                        }
                        else
                        {
                            GameMain.Server.AutoRestart = false;
                            NewMessage("Autorestart disabled.", Color.White);
                        }
#if CLIENT
                        GameMain.NetLobbyScreen.SetAutoRestart(GameMain.Server.AutoRestart, GameMain.Server.AutoRestartTimer);
#endif
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }
                }
            }, null, null));
            
            commands.Add(new Command("autorestarttimer", "autorestarttimer [seconds]: Set the current autorestart countdown to the specified value.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    int parsedInt = 0;
                    if (int.TryParse(args[0], out parsedInt))
                    {
                        if (parsedInt >= 0)
                        {
                            GameMain.Server.AutoRestart = true;
                            GameMain.Server.AutoRestartTimer = parsedInt;
                            if (GameMain.Server.AutoRestartInterval <= GameMain.Server.AutoRestartTimer) GameMain.Server.AutoRestartInterval = GameMain.Server.AutoRestartTimer;
                            GameMain.NetLobbyScreen.LastUpdateID++;
                            NewMessage("Autorestart timer set to " + GameMain.Server.AutoRestartTimer + " seconds.", Color.White);
                        }
                        else
                        {
                            GameMain.Server.AutoRestart = false;
                            NewMessage("Autorestart disabled.", Color.White);
                        }
#if CLIENT
                        GameMain.NetLobbyScreen.SetAutoRestart(GameMain.Server.AutoRestart, GameMain.Server.AutoRestartTimer);
#endif
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }
                }
            }, null, null));
            
            commands.Add(new Command("giveperm", "giveperm [id]: Grants administrative permissions to the player with the specified client ID.", (string[] args) =>
            {
                //todo: allow client usage

                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("giveperm [id]: Grants administrative permissions to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int id;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                NewMessage("Valid permissions are:",Color.White);
                NewMessage(" - all",Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(),Color.White);
                }
                ShowQuestionPrompt("Permission to grant to \"" + client.Name + "\"?", (perm) =>
                {
                    ClientPermissions permission = ClientPermissions.None;
                    if (perm.ToLower() == "all")
                    {
                        permission = ClientPermissions.EndRound | ClientPermissions.Kick | ClientPermissions.Ban | 
                            ClientPermissions.SelectSub | ClientPermissions.SelectMode | ClientPermissions.ManageCampaign | ClientPermissions.ConsoleCommands;
                    }
                    else
                    {
                        if (!Enum.TryParse(perm, true, out permission))
                        {
                            NewMessage(perm + " is not a valid permission!", Color.Red);
                            return;
                        }
                    }
                    client.GivePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Granted " + perm + " permissions to " + client.Name + ".", Color.White);
                });
            }, 
            (string[] args) =>
            {
#if CLIENT
                if (args.Length < 1) return;

                int id;
                if (!int.TryParse(args[0], out id))
                {
                    ThrowError("\"" + id + "\" is not a valid client ID.");
                    return;
                }

                NewMessage("Valid permissions are:", Color.White);
                NewMessage(" - all", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(), Color.White);
                }
                ShowQuestionPrompt("Permission to grant to client #" + id + "?", (perm) =>
                {
                    GameMain.Client.SendConsoleCommand("giveperm " +id + " " + perm);
                });
#endif
            },
            (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
            {
                if (args.Length < 2) return;

                int id;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                    return;
                }

                string perm = string.Join("", args.Skip(1));

                ClientPermissions permission = ClientPermissions.None;
                if (perm.ToLower() == "all")
                {
                    permission = ClientPermissions.EndRound | ClientPermissions.Kick | ClientPermissions.Ban |
                        ClientPermissions.SelectSub | ClientPermissions.SelectMode | ClientPermissions.ManageCampaign | ClientPermissions.ConsoleCommands;
                }
                else
                {
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient);
                        return;
                    }
                }
                client.GivePermission(permission);
                GameMain.Server.UpdateClientPermissions(client);
                GameMain.Server.SendConsoleMessage("Granted " + perm + " permissions to " + client.Name + ".", senderClient);
                NewMessage(senderClient.Name + " granted " + perm + " permissions to " + client.Name + ".", Color.White);
            }));

            commands.Add(new Command("revokeperm", "revokeperm [id]: Revokes administrative permissions to the player with the specified client ID.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("revokeperm [id]: Revokes administrative permissions to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int id;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                NewMessage("Valid permissions are:", Color.White);
                NewMessage(" - all", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(), Color.White);
                }
                ShowQuestionPrompt("Permission to revoke from \"" + client.Name + "\"?", (perm) =>
                {
                    ClientPermissions permission = ClientPermissions.None;
                    if (perm.ToLower() == "all")
                    {
                        permission = ClientPermissions.EndRound | ClientPermissions.Kick | ClientPermissions.Ban | 
                            ClientPermissions.SelectSub | ClientPermissions.SelectMode | ClientPermissions.ManageCampaign | ClientPermissions.ConsoleCommands;
                    }
                    else
                    {
                        if (!Enum.TryParse(perm, true, out permission))
                        {
                            NewMessage(perm + " is not a valid permission!", Color.Red);
                            return;
                        }
                    }
                    client.RemovePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Revoked " + perm + " permissions from " + client.Name + ".", Color.White);
                });
            },
            (string[] args) =>
            {
#if CLIENT
                if (args.Length < 1) return;

                int id;
                if (!int.TryParse(args[0], out id))
                {
                    ThrowError("\"" + id + "\" is not a valid client ID.");
                    return;
                }

                NewMessage("Valid permissions are:", Color.White);
                NewMessage(" - all", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(), Color.White);
                }

                ShowQuestionPrompt("Permission to revoke from client #" + id + "?", (perm) =>
                {
                    GameMain.Client.SendConsoleCommand("revokeperm " + id + " " + perm);
                });
#endif
            },
            (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
            {
                if (args.Length < 2) return;

                int id;
                int.TryParse(args[0], out id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                    return;
                }

                string perm = string.Join("", args.Skip(1));

                ClientPermissions permission = ClientPermissions.None;
                if (perm.ToLower() == "all")
                {
                    permission = ClientPermissions.EndRound | ClientPermissions.Kick | ClientPermissions.Ban |
                        ClientPermissions.SelectSub | ClientPermissions.SelectMode | ClientPermissions.ManageCampaign | ClientPermissions.ConsoleCommands;
                }
                else
                {
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient);
                        return;
                    }
                }
                client.RemovePermission(permission);
                GameMain.Server.UpdateClientPermissions(client);
                GameMain.Server.SendConsoleMessage("Revoked " + perm + " permissions from " + client.Name + ".", senderClient);
                NewMessage(senderClient.Name + " revoked " + perm + " permissions from " + client.Name + ".", Color.White);
            }));

            commands.Add(new Command("togglekarma", "togglekarma: Toggles the karma system.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                GameMain.Server.KarmaEnabled = !GameMain.Server.KarmaEnabled;
            }));

            commands.Add(new Command("kick", "kick [name]: Kick a player out of the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;
                
                string playerName = string.Join(" ", args);

                ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"?", (reason) =>
                {
                    GameMain.NetworkMember.KickPlayer(playerName, reason);
                });                
            },
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                };
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

                ShowQuestionPrompt("Reason for kicking \"" + client.Name + "\"?", (reason) =>
                {
                    GameMain.Server.KickPlayer(client.Name, reason);                    
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
            },
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray()
                };
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

                ShowQuestionPrompt("Reason for banning \"" + client.Name + "\"?", (reason) =>
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

                        GameMain.Server.BanPlayer(client.Name, reason, false, banDuration);
                    });
                });
            }));


            commands.Add(new Command("banip", "banip [ip]: Ban the IP address from the server.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                
                ShowQuestionPrompt("Reason for banning the ip \"" + args[0] + "\"?", (reason) =>
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
                        
                        var clients = GameMain.Server.ConnectedClients.FindAll(c => c.Connection.RemoteEndPoint.Address.ToString() == args[0]);
                        if (clients.Count == 0)
                        {
                            GameMain.Server.BanList.BanPlayer("Unnamed", args[0], reason, banDuration);
                        }
                        else
                        {
                            foreach (Client cl in clients)
                            {
                                GameMain.Server.BanClient(cl, reason, false, banDuration);
                            }
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
            }, 
            null, 
            (Client client, Vector2 cursorWorldPos, string[] args) => 
            {
                Character tpCharacter = null;

                if (args.Length == 0)
                {
                    tpCharacter = client.Character;
                }
                else
                {
                    tpCharacter = FindMatchingCharacter(args, false);
                }

                if (tpCharacter == null) return;

                var cam = GameMain.GameScreen.Cam;
                tpCharacter.AnimController.CurrentHull = null;
                tpCharacter.Submarine = null;
                tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cursorWorldPos));
                tpCharacter.AnimController.FindHull(cursorWorldPos, true);
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("godmode", "godmode: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                NewMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", Color.White);
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                NewMessage((Submarine.MainSub.GodMode ? "Godmode turned on by \"" : "Godmode off by \"") + client.Name+"\"", Color.White);
                GameMain.Server.SendConsoleMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", client);
            }));

            commands.Add(new Command("lockx", "lockx: Lock horizontal movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockX = !Submarine.LockX;
            }, null, null));

            commands.Add(new Command("locky", "loxky: Lock vertical movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockY = !Submarine.LockY;
            }, null, null));

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
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                Character healedCharacter = null;
                if (args.Length == 0)
                {
                    healedCharacter =  client.Character;
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
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
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
            }, 
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) => 
            {
                Character revivedCharacter = null;
                if (args.Length == 0)
                {
                    revivedCharacter = client.Character;
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
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("freeze", "", (string[] args) =>
            {
                if (Character.Controlled != null) Character.Controlled.AnimController.Frozen = !Character.Controlled.AnimController.Frozen;
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) => 
            {
                if (client.Character != null) client.Character.AnimController.Frozen = !client.Character.AnimController.Frozen;
            }));

            commands.Add(new Command("ragdoll", "ragdoll [character name]: Force-ragdoll the specified character. If the name parameter is omitted, the controlled character will be ragdolled.", (string[] args) =>
            {
                Character ragdolledCharacter = null;
                if (args.Length == 0)
                {
                    ragdolledCharacter = Character.Controlled;
                }
                else
                {
                    ragdolledCharacter = FindMatchingCharacter(args);
                }

                if (ragdolledCharacter != null)
                {
                    ragdolledCharacter.IsForceRagdolled = !ragdolledCharacter.IsForceRagdolled;
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
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
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) => 
            {
                Vector2 explosionPos = cursorWorldPos;
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
            }, null, null));

            commands.Add(new Command("fixhulls|fixwalls", "fixwalls/fixhulls: Fixes all walls.", (string[] args) =>
            {
                foreach (Structure w in Structure.WallList)
                {
                    for (int i = 0; i < w.SectionCount; i++)
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }
            }, null, null));

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
            }, null, null));

            commands.Add(new Command("oxygen|air", "oxygen/air: Replenishes the oxygen levels in every room to 100%.", (string[] args) =>
            {
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                }
            }, null, null));

            commands.Add(new Command("kill", "kill [character]: Immediately kills the specified character.", (string[] args) =>
            {
                Character killedCharacter = null;
                if (args.Length == 0)
                {
                    killedCharacter = Character.Controlled;
                }
                else
                {
                    killedCharacter = FindMatchingCharacter(args);
                }

                if (killedCharacter != null)
                {
                    killedCharacter.AddDamage(CauseOfDeath.Damage, killedCharacter.MaxHealth * 2, null);
                }
            }));

            commands.Add(new Command("killmonsters", "killmonsters: Immediately kills all AI-controlled enemies in the level.", (string[] args) =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!(c.AIController is EnemyAIController)) continue;
                    c.AddDamage(CauseOfDeath.Damage, c.MaxHealth * 2, null);
                }
            }, null, null));

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
                string clientName = string.Join(" ", argsLeft);

                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == clientName);
                if (client == null)
                {
                    ThrowError("Client \"" + clientName + "\" not found.");
                }

                var character = FindMatchingCharacter(argsRight, false);
                GameMain.Server.SetClientCharacter(client, character);
            },
            null,
            (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
            {
                int separatorIndex = Array.IndexOf(args, ";");
                if (separatorIndex == -1 || args.Length < 3)
                {
                    GameMain.Server.SendConsoleMessage("Invalid parameters. The command should be formatted as \"setclientcharacter [client] ; [character]\"", senderClient);
                    return;
                }

                string[] argsLeft = args.Take(separatorIndex).ToArray();
                string[] argsRight = args.Skip(separatorIndex + 1).ToArray();
                string clientName = string.Join(" ", argsLeft);

                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == clientName);
                if (client == null)
                {
                    GameMain.Server.SendConsoleMessage("Client \"" + clientName + "\" not found.", senderClient);
                }

                var character = FindMatchingCharacter(argsRight, false);
                GameMain.Server.SetClientCharacter(client, character);
            },
            () =>
            {
                if (GameMain.NetworkMember == null) return null;

                return new string[][]
                {
                    GameMain.NetworkMember.ConnectedClients.Select(c => c.Name).ToArray(),
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
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
            },
            (string[] args) =>
            {
#if CLIENT
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
                        GameMain.Client.SendConsoleCommand("campaigndestination " + destinationIndex);
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
                    GameMain.Client.SendConsoleCommand("campaigndestination " + destinationIndex);
                }
#endif
            },
            (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
            {
                var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                if (campaign == null)
                {
                    GameMain.Server.SendConsoleMessage("No campaign active!", senderClient);
                    return;
                }

                int destinationIndex = -1;
                if (args.Length < 1 || !int.TryParse(args[0], out destinationIndex)) return;
                if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                {
                    GameMain.Server.SendConsoleMessage("Index out of bounds!", senderClient);
                    return;
                }
                Location location = campaign.Map.CurrentLocation.Connections[destinationIndex].OtherLocation(campaign.Map.CurrentLocation);
                campaign.Map.SelectLocation(location);
                GameMain.Server.SendConsoleMessage(location.Name + " selected.", senderClient);
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
            }, null, null));
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
            string[] splitCommand = SplitCommand(command);
            string[] args = splitCommand.Skip(1).ToArray();

            //if an argument is given or the last character is a space, attempt to autocomplete the argument
            if (args.Length > 0 || (command.Length > 0 && command.Last() == ' '))
            {
                Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0]));
                if (matchingCommand == null || matchingCommand.GetValidArgs == null) return command;

                int autoCompletedArgIndex = args.Length > 0 && command.Last() != ' ' ? args.Length - 1 : args.Length;

                //get all valid arguments for the given command
                string[][] allArgs = matchingCommand.GetValidArgs();
                if (allArgs == null || allArgs.GetLength(0) < autoCompletedArgIndex + 1) return command;

                if (string.IsNullOrEmpty(currentAutoCompletedCommand))
                {
                    currentAutoCompletedCommand = autoCompletedArgIndex > args.Length - 1 ? " " : args.Last();
                }

                //find all valid autocompletions for the given argument
                string[] validArgs = allArgs[autoCompletedArgIndex].Where(arg => 
                    currentAutoCompletedCommand.Trim().Length <= arg.Length && 
                    arg.Substring(0, currentAutoCompletedCommand.Trim().Length).ToLower() == currentAutoCompletedCommand.Trim().ToLower()).ToArray();

                if (validArgs.Length == 0) return command;

                currentAutoCompletedIndex = currentAutoCompletedIndex % validArgs.Length;
                string autoCompletedArg = validArgs[currentAutoCompletedIndex++];

                //add quotation marks to args that contain spaces
                if (autoCompletedArg.Contains(' ')) autoCompletedArg = '"' + autoCompletedArg + '"';
                for (int i = 0; i < splitCommand.Length; i++)
                {
                    if (splitCommand[i].Contains(' ')) splitCommand[i] = '"' + splitCommand[i] + '"';
                }

                return string.Join(" ", autoCompletedArgIndex >= args.Length ? splitCommand : splitCommand.Take(splitCommand.Length - 1)) + " " + autoCompletedArg;
            }
            else
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
        }

        private static string AutoCompleteStr(string str, IEnumerable<string> validStrings)
        {
            if (string.IsNullOrEmpty(str)) return str;
            foreach (string validStr in validStrings)
            {
                if (validStr.Length > str.Length && validStr.Substring(0, str.Length) == str) return validStr;
            }
            return str;
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

			int i = 0;
			do
			{
				selectedIndex += direction;
				if (selectedIndex < 0) selectedIndex = Messages.Count - 1;
				selectedIndex = selectedIndex % Messages.Count;
				if (++i >= Messages.Count) break;
			} while (!Messages[selectedIndex].IsCommand);

            return Messages[selectedIndex].Text;            
        }

        public static void ExecuteCommand(string command)
        {
            if (activeQuestionCallback != null)
            {
#if CLIENT
                activeQuestionText = null;
#endif
                NewMessage(command, Color.White, true);
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
                NewMessage(command, Color.White, true);
            }

#if CLIENT
            if (GameMain.Client != null)
            {
                if (GameMain.Client.HasConsoleCommandPermission(splitCommand[0].ToLowerInvariant()))
                {
                    Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0].ToLowerInvariant()));

                    //if the command is not defined client-side, we'll relay it anyway because it may be a custom command at the server's side
                    if (matchingCommand == null || matchingCommand.RelayToServer)
                    {
                        GameMain.Client.SendConsoleCommand(command);
                    }
                    else
                    {
                        matchingCommand.ClientExecute(splitCommand.Skip(1).ToArray());
                    }

                    NewMessage("Server command: " + command, Color.White);
                    return;
                }
#if !DEBUG
                if (!IsCommandPermitted(splitCommand[0].ToLowerInvariant(), GameMain.Client))
                {
                    ThrowError("You're not permitted to use the command \"" + splitCommand[0].ToLowerInvariant() + "\"!");
                    return;
                }
#endif
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
                ThrowError("Command \"" + splitCommand[0] + "\" not found.");
            }
        }

        public static void ExecuteClientCommand(Client client, Vector2 cursorWorldPos, string command)
        {
            if (GameMain.Server == null) return;
            if (string.IsNullOrWhiteSpace(command)) return;
            if (!client.HasPermission(ClientPermissions.ConsoleCommands))
            {
                GameMain.Server.SendConsoleMessage("You are not permitted to use console commands!", client);
                GameServer.Log(client.Name + " attempted to execute the console command \"" + command + "\" without a permission to use console commands.", ServerLog.MessageType.ConsoleUsage);
                return;
            }

            string[] splitCommand = SplitCommand(command);
            Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0].ToLowerInvariant()));
            if (matchingCommand != null && !client.PermittedConsoleCommands.Contains(matchingCommand))
            {
                GameMain.Server.SendConsoleMessage("You are not permitted to use the command\"" + matchingCommand.names[0] + "\"!", client);
                GameServer.Log(client.Name + " attempted to execute the console command \"" + command + "\" without a permission to use the command.", ServerLog.MessageType.ConsoleUsage);
                return;
            }
            else if (matchingCommand == null)
            {
                GameMain.Server.SendConsoleMessage("Command \"" + splitCommand[0] + "\" not found.", client);
                return;
            }

            try
            {
                matchingCommand.ServerExecuteOnClientRequest(client, cursorWorldPos, splitCommand.Skip(1).ToArray());
                GameServer.Log("Console command \"" + command + "\" executed by " + client.Name + ".", ServerLog.MessageType.ConsoleUsage);
            }
            catch (Exception e)
            {
                ThrowError("Executing the command \"" + matchingCommand.names[0] + "\" by request from \"" + client.Name + "\" failed.", e);
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

        private static void SpawnCharacter(string[] args, Vector2 cursorWorldPos, out string errorMsg)
        {
            errorMsg = "";
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
                        spawnPosition = cursorWorldPos;
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
                List<string> characterFiles = GameMain.Config.SelectedContentPackage.GetFilesOfType(ContentType.Character);

                foreach (string characterFile in characterFiles)
                {
                    if (Path.GetFileNameWithoutExtension(characterFile).ToLowerInvariant() == args[0].ToLowerInvariant())
                    {
                        Character.Create(characterFile, spawnPosition);
                        return;
                    }
                }

                errorMsg = "No character matching the name \"" + args[0] + "\" found in the selected content package.";

                //attempt to open the config from the default path (the file may still be present even if it isn't included in the content package)
                string configPath = "Content/Characters/"
                    + args[0].First().ToString().ToUpper() + args[0].Substring(1)
                    + "/" + args[0].ToLower() + ".xml";
                Character.Create(configPath, spawnPosition);
            }
        }

        private static void SpawnItem(string[] args, Vector2 cursorPos, out string errorMsg)
        {
            errorMsg = "";
            if (args.Length < 1) return;

            Vector2? spawnPos = null;
            Inventory spawnInventory = null;

            int extraParams = 0;
            switch (args.Last())
            {
                case "cursor":
                    extraParams = 1;
                    spawnPos = cursorPos;
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

            var itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
            if (itemPrefab == null)
            {
                errorMsg = "Item \"" + itemName + "\" not found!";
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
        }

        public static void NewMessage(string msg, Color color, bool isCommand = false)
        {
            if (string.IsNullOrEmpty((msg))) return;

#if SERVER
            Messages.Add(new ColoredText(msg, color, isCommand));

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
                queuedMessages.Enqueue(new ColoredText(msg, color, isCommand));
            }
#endif
        }

        public static void ShowQuestionPrompt(string question, QuestionCallback onAnswered)
        {

#if CLIENT
            activeQuestionText = new GUITextBlock(new Rectangle(0, 0, listBox.Rect.Width, 30), "   >>" + question, "", Alignment.TopLeft, Alignment.Left, null, true, GUI.SmallFont);
            activeQuestionText.CanBeFocused = false;
            activeQuestionText.TextColor = Color.Cyan;
#else
            NewMessage("   >>" + question, Color.Cyan);
#endif
            activeQuestionCallback += onAnswered;
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

        public static Command FindCommand(string commandName)
        {
            commandName = commandName.ToLowerInvariant();
            return commands.Find(c => c.names.Any(n => n.ToLowerInvariant() == commandName));
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
