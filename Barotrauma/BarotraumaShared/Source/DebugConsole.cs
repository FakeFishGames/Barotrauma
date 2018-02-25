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
            public readonly CommandType Type;
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
            

            public Command(string name, CommandType type, string help, Action<string[]> onExecute, Action<string[]> onClientExecute, Action<Client, Vector2, string[]> onClientRequestExecute, Func<string[][]> getValidArgs = null)
            {
                names = name.Split('|');
                this.help = help;
                this.Type = type;

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

            /// <summary>
            /// Use this constructor to create a command that executes the same action regardless of whether it's executed by a client or the server.
            /// </summary>
            public Command(string name, CommandType type, string help, Action<string[]> onExecute, Func<string[][]> getValidArgs = null)
            {
                names = name.Split('|');
                this.help = help;
                this.Type = type;

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

        public enum CommandType
        {
            Generic,
            Spawning,
            Render,
            Debug,
            DebugHide,  //To Hide commands when not in DEBUG mode
            GamePower,
            Character,
            Network
        }

        const int MaxMessages = 600;

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
            commands.Add(new Command("help", CommandType.Generic, "", (string[] args) =>
            {
                if (args.Length == 0)
                {
                    List<Command> matchingCommands;
                    /*
                    foreach (Command c in commands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }
                    */

                    //Generic Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Generic));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                             Generic                              *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //Debug & DebugHide Commands list
#if DEBUG
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Debug | cmd.Type == CommandType.DebugHide));
#else
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Debug));
#endif
                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                              Debug                               *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //Render Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Render));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                              Render                              *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //Network Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Network));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                             Network                             *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //GamePower Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.GamePower));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                        Game Powers                        *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //Character Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Character));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                            Character                            *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
                    {
                        if (string.IsNullOrEmpty(c.help)) continue;
                        NewMessage(c.help, Color.Cyan);
                    }

                    //Character Commands list
                    matchingCommands = commands.FindAll(cmd => (cmd.Type == CommandType.Spawning));

                    NewMessage("**********************************************", Color.Cyan);
                    NewMessage("*                              Spawns                             *", Color.Cyan);
                    NewMessage("**********************************************", Color.Cyan);

                    foreach (Command c in matchingCommands)
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

            commands.Add(new Command("clientlist", CommandType.Network, "clientlist: List all the clients connected to the server.", (string[] args) =>
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


            commands.Add(new Command("createfilelist", CommandType.Debug, "", (string[] args) =>
            {
                UpdaterUtil.SaveFileList("filelist.xml");
            }));

            commands.Add(new Command("spawn|spawncharacter", CommandType.Spawning, "spawn [creaturename] [near/inside/outside/cursor]: Spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine).", (string[] args) =>
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

            commands.Add(new Command("spawnitem", CommandType.Spawning, "spawnitem [itemname] [cursor/inventory]: Spawn an item at the position of the cursor, in the inventory of the controlled character or at a random spawnpoint if the last parameter is omitted.",
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

            commands.Add(new Command("disablecrewai", CommandType.Debug, "disablecrewai: Disable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled", Color.White);
#if CLIENT
                if (GameMain.Server != null)
                {
                    GameMain.Server.ToggleCrewAIButton.Text = "Crew AI On";
                    GameMain.Server.ToggleCrewAIButton.ToolTip = "Turns the AI Crews AI On.";
                }
#endif
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                HumanAIController.DisableCrewAI = true;
                NewMessage("Crew AI disabled by \"" + client.Name + "\"", Color.White);
                GameMain.Server.SendConsoleMessage("Crew AI disabled", client);
            }));

            commands.Add(new Command("enablecrewai", CommandType.Debug, "enablecrewai: Enable the AI of the NPCs in the crew.", (string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled", Color.White);
#if CLIENT
                if (GameMain.Server != null)
                {
                    GameMain.Server.ToggleCrewAIButton.Text = "Crew AI Off";
                    GameMain.Server.ToggleCrewAIButton.ToolTip = "Turns the AI Crews AI Off.";
                }
#endif
            }, 
            null, 
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                HumanAIController.DisableCrewAI = false;
                NewMessage("Crew AI enabled by \"" + client.Name + "\"", Color.White);
                GameMain.Server.SendConsoleMessage("Crew AI enabled", client);
            }));

            commands.Add(new Command("autorestart", CommandType.Network, "autorestart [true/false]: Enable or disable round auto-restart.", (string[] args) =>
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
            
            commands.Add(new Command("autorestartinterval", CommandType.Network, "autorestartinterval [seconds]: Set how long the server waits between rounds before automatically starting a new one. If set to 0, autorestart is disabled.", (string[] args) =>
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
            
            commands.Add(new Command("autorestarttimer", CommandType.Network, "autorestarttimer [seconds]: Set the current autorestart countdown to the specified value.", (string[] args) =>
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

            commands.Add(new Command("kick", CommandType.Network, "kick [name]: Kick a player out of the server.", (string[] args) =>
            {
                if (GameMain.NetworkMember == null || args.Length == 0) return;
                
                string playerName = string.Join(" ", args);

                ShowQuestionPrompt("Reason for kicking \"" + playerName + "\"?", (reason) =>
                {
                    GameMain.NetworkMember.KickPlayer(playerName, reason, GameMain.NilMod.AdminKickStateNameTimer, GameMain.NilMod.AdminKickDenyRejoinTimer);
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

            commands.Add(new Command("kickid", CommandType.Network, "kickid [id]: Kick the player with the specified client ID out of the server.", (string[] args) =>
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
                    GameMain.Server.KickPlayer(client.Name, reason, GameMain.NilMod.AdminKickStateNameTimer, GameMain.NilMod.AdminKickDenyRejoinTimer);
                });
            }));

            commands.Add(new Command("ban", CommandType.Network, "ban [name]: Kick and ban the player from the server.", (string[] args) =>
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

            commands.Add(new Command("banid", CommandType.Network, "banid [id]: Kick and ban the player with the specified client ID from the server.", (string[] args) =>
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


            commands.Add(new Command("banip", CommandType.Network, "banip [ip]: Ban the IP address from the server.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;

            ShowQuestionPrompt("Enter the name to give the ip \"" + args[0] + "\"?", (name) =>
            {
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

                        if (name == null || name == "") name = "IP Banned";

                        var client = GameMain.Server.ConnectedClients.Find(c => c.Connection.RemoteEndPoint.Address.ToString() == args[0]);

                        if (client == null)
                        {
                            GameMain.Server.BanList.BanPlayer(name, args[0], reason, banDuration);
                        }
                        else
                        {
                            for(int i = GameMain.Server.ConnectedClients.Count - 1;i >= 0; i--)
                            {
                                if(GameMain.Server.ConnectedClients[i].Connection.RemoteEndPoint.Address.ToString() == args[0])
                                {
                                    GameMain.Server.KickBannedClient(client.Connection, reason);
                                }
                            }
                            GameMain.Server.BanList.BanPlayer(name, args[0], reason, banDuration);
                            //GameMain.Server.BanClient(client, reason,false, banDuration);
                        }
                    });
                });
            });

            }));

            commands.Add(new Command("teleportcharacter|teleport", CommandType.Character, "teleport [character name]: Teleport the specified character to the position of the cursor. If the name parameter is omitted, the controlled character will be teleported.", (string[] args) =>
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

            commands.Add(new Command("godmode", CommandType.GamePower, "godmode: Toggle submarine godmode. Makes the main submarine invulnerable to damage.", (string[] args) =>
            {
                if (Submarine.MainSub == null) return;

                Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (Submarine.MainSub.GodMode)
                    {
                        GameMain.Server.ToggleGodmodeButton.Text = "GodMode Off";
                        GameMain.Server.ToggleGodmodeButton.ToolTip = "Turns off godmode which allows for submarine damage.";
                    }
                    else
                    {
                        GameMain.Server.ToggleGodmodeButton.Text = "GodMode On";
                        GameMain.Server.ToggleGodmodeButton.ToolTip = "Turns on godmode which stops submarine damage.";
                    }
                }
#endif
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

            commands.Add(new Command("lockx", CommandType.GamePower, "lockx: Lock horizontal movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockX = !Submarine.LockX;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (Submarine.LockX)
                    {
                        GameMain.Server.LockSubXButton.Text = "Unlock Sub X";
                        GameMain.Server.LockSubXButton.ToolTip = "Allows again any submarine/shuttle to Move Left/Right.";
                    }
                    else
                    {
                        GameMain.Server.LockSubXButton.Text = "Lock Sub X";
                        GameMain.Server.LockSubXButton.ToolTip = "Prevents any submarine/shuttle from moving Left/Right.";
                    }
                }
#endif
            }, null, null));

            commands.Add(new Command("locky", CommandType.GamePower, "locky: Lock vertical movement of the main submarine.", (string[] args) =>
            {
                Submarine.LockY = !Submarine.LockY;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (Submarine.LockY)
                    {
                        GameMain.Server.LockSubYButton.Text = "Unlock Sub Y";
                        GameMain.Server.LockSubYButton.ToolTip = "Allows again any submarine/shuttle to move Up/Down.";
                    }
                    else
                    {
                        GameMain.Server.LockSubYButton.Text = "Lock Sub Y";
                        GameMain.Server.LockSubYButton.ToolTip = "Prevents any submarine/shuttle from moving Up/Down.";
                    }
                }
#endif
            }, null, null));

            commands.Add(new Command("dumpids", CommandType.Debug, "", (string[] args) =>
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

            commands.Add(new Command("heal", CommandType.Character, "heal [character name]: Restore the specified character to full health. If the name parameter is omitted, the controlled character will be healed.", (string[] args) =>
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
                    healedCharacter.Heal();
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
                    healedCharacter.Heal();
                }
            },
            () =>
            {
                return new string[][]
                {
                    Character.CharacterList.Select(c => c.Name).Distinct().ToArray()
                };
            }));

            commands.Add(new Command("revive", CommandType.Character, "revive [character name]: Bring the specified character back from the dead. If the name parameter is omitted, the controlled character will be revived.", (string[] args) =>
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

                revivedCharacter.Revive(true);
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

            commands.Add(new Command("freeze", CommandType.GamePower, "freeze [character name]: Freezes the controls of the specified client character", (string[] args) =>
            {
                Character frozenCharacter = null;
                if (args.Length == 0)
                {
                    frozenCharacter = Character.Controlled;
                }
                else
                {
                    frozenCharacter = FindMatchingCharacter(args);
                }

                if (frozenCharacter == null) return;

                if(GameMain.NilMod.FrozenCharacters.Find(c => c == frozenCharacter) != null)
                {
                    if (GameMain.Server != null)
                    {
                        if (GameMain.Server.ConnectedClients.Find(c => c.Character == frozenCharacter) != null)
                        {
                            var chatMsg = ChatMessage.Create(
                            "Server Message",
                            ("You have been unfrozen by the server\n\nYou may now move again and perform actions."),
                            (ChatMessageType)ChatMessageType.MessageBox,
                            null);

                            GameMain.Server.SendChatMessage(chatMsg, GameMain.Server.ConnectedClients.Find(c => c.Character == frozenCharacter));
                        }
                    }
                    GameMain.NilMod.FrozenCharacters.Remove(frozenCharacter);
                }
                else
                {
                    GameMain.NilMod.FrozenCharacters.Add(frozenCharacter);
                    if (GameMain.Server != null)
                    {
                        if (GameMain.Server.ConnectedClients.Find(c => c.Character == frozenCharacter) != null)
                        {
                            var chatMsg = ChatMessage.Create(
                            "Server Message",
                            ("You have been frozen by the server\n\nYou may still talk if able, but no longer perform any actions or movements."),
                            (ChatMessageType)ChatMessageType.MessageBox,
                            null);

                            GameMain.Server.SendChatMessage(chatMsg, GameMain.Server.ConnectedClients.Find(c => c.Character == frozenCharacter));
                        }
                    }
                }

                //if (frozenCharacter != null) frozenCharacter.AnimController.Frozen = !frozenCharacter.AnimController.Frozen;

            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) => 
            {
                //Needs client support
                //if (client.Character != null) client.Character.AnimController.Frozen = !client.Character.AnimController.Frozen;
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

            commands.Add(new Command("freecamera|freecam", CommandType.Render, "freecam: Detach the camera from the controlled character.", (string[] args) =>
            {
                Character.Spied = null;
                Character.Controlled = null;
                GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            }));

            commands.Add(new Command("water|editwater", CommandType.GamePower, "water/editwater: Toggle water editing. Allows adding water into rooms by holding the left mouse button and removing it by holding the right mouse button.", (string[] args) =>
            {
                if (GameMain.Client == null)
                {
                    Hull.EditWater = !Hull.EditWater;
#if CLIENT
                    if (GameMain.Server != null)
                    {
                        if (Hull.EditWater)
                        {
                            GameMain.Server.WaterButton.Text = "Water Off";
                            GameMain.Server.WaterButton.ToolTip = "Turns off water control.";
                            GameMain.Server.FiresButton.Text = "Fire On";
                            GameMain.Server.FiresButton.ToolTip = "Turns on fire control, Left click to add fires.";
                            Hull.EditFire = false;
                        }
                        else
                        {
                            GameMain.Server.WaterButton.Text = "Water On";
                            GameMain.Server.WaterButton.ToolTip = "Turns on water control, Left click to add water, Right click to remove.";
                        }
                    }
#endif
                    NewMessage(Hull.EditWater ? "Water editing on" : "Water editing off", Color.White);
                }
            }));

            commands.Add(new Command("fire|editfire", CommandType.GamePower, "fire/editfire: Allows putting up fires by left clicking.", (string[] args) =>
            {
                if (GameMain.Client == null)
                {
                    Hull.EditFire = !Hull.EditFire;
#if CLIENT
                    if (GameMain.Server != null)
                    {
                        if (Hull.EditFire)
                        {
                            GameMain.Server.FiresButton.Text = "Fire Off";
                            GameMain.Server.FiresButton.ToolTip = "Turns off fire control.";
                            GameMain.Server.WaterButton.Text = "Water On";
                            GameMain.Server.WaterButton.ToolTip = "Turns on water control, Left click to add water, Right click to remove.";
                            Hull.EditWater = false;
                        }
                        else
                        {
                            GameMain.Server.FiresButton.Text = "Fire On";
                            GameMain.Server.FiresButton.ToolTip = "Turns on fire control, Left click to add fires.";
                        }
                    }
#endif
                    NewMessage(Hull.EditFire ? "Fire spawning on" : "Fire spawning off", Color.White);
                }
            }));

            commands.Add(new Command("explosion", CommandType.GamePower, "explosion [range] [force] [damage] [structuredamage] [emp strength]: Creates an explosion at the position of the cursor.", (string[] args) =>
            {
                Vector2 explosionPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                float range = 500, force = 10, damage = 50, structureDamage = 10, empStrength = 0.0f;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                if (args.Length > 4) float.TryParse(args[4], out empStrength);
                new Explosion(range, force, damage, structureDamage, empStrength).Explode(explosionPos);
            },
            null,
            (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                Vector2 explosionPos = cursorWorldPos;
                float range = 500, force = 10, damage = 50, structureDamage = 10, empStrength = 0.0f; ;
                if (args.Length > 0) float.TryParse(args[0], out range);
                if (args.Length > 1) float.TryParse(args[1], out force);
                if (args.Length > 2) float.TryParse(args[2], out damage);
                if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                if (args.Length > 4) float.TryParse(args[4], out empStrength);
                new Explosion(range, force, damage, structureDamage, empStrength).Explode(explosionPos);
            }));

            commands.Add(new Command("fixitems", CommandType.GamePower, "fixitems: Repairs all items and restores them to full condition.", (string[] args) =>
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Condition = it.Prefab.Health;
                }
            }, null, null));

            commands.Add(new Command("fixhulls|fixwalls", CommandType.GamePower, "fixwalls/fixhulls: Fixes all walls.", (string[] args) =>
            {
                foreach (Structure w in Structure.WallList)
                {
                    for (int i = 0; i < w.SectionCount; i++)
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }
            }, null, null));

            commands.Add(new Command("power", CommandType.GamePower, "power [temperature]: Immediately sets the temperature of the nuclear reactor to the specified value.", (string[] args) =>
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

            commands.Add(new Command("oxygen|air", CommandType.GamePower, "oxygen/air: Replenishes the oxygen levels in every room to 100%.", (string[] args) =>
            {
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                }
            }, null, null));

            commands.Add(new Command("kill", CommandType.GamePower, "kill [character]: Immediately kills the specified character.", (string[] args) =>
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
                    //Use high damage values due to health multipliers
                    killedCharacter.AddDamage(CauseOfDeath.Damage, 1000000.0f, null);
                    //If still not dead make extra sure they are due to anti death code.
                    if(!killedCharacter.IsDead) killedCharacter.Kill(CauseOfDeath.Damage, true);
                }
            }));

            commands.Add(new Command("killmonsters", CommandType.GamePower, "killmonsters: Immediately kills all AI-controlled enemies in the level.", (string[] args) =>
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!(c.AIController is EnemyAIController)) continue;
                    //Use high damage values due to health multipliers
                    c.AddDamage(CauseOfDeath.Damage, 1000000.0f, null);
                    //If still not dead make extra sure they are due to anti death code.
                    if (!c.IsDead) c.Kill(CauseOfDeath.Damage, true);
                }
            }, null, null));

            commands.Add(new Command("netstats", CommandType.Network, "netstats: Toggles the visibility of the network statistics UI.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
#if CLIENT
                GameMain.Server.ShowLagDiagnostics = false;
                GameMain.Server.ShowNetStats = !GameMain.Server.ShowNetStats;
                if (GameMain.Server.ShowNetStats)
                {
                    GameMain.Server.ShowNetStatsButton.Text = "NetStats Off";
                    GameMain.Server.ShowNetStatsButton.ToolTip = "Turns off the netstats screen which shows the latencies and IP Addresses of connections.";
                }
                else
                {
                    GameMain.Server.ShowNetStatsButton.Text = "NetStats On";
                    GameMain.Server.ShowNetStatsButton.ToolTip = "Turns on the netstats screen which shows the latencies and IP Addresses of connections.";
                }
                GameMain.Server.ShowLagDiagnosticsButton.Text = "Lag Profiler On";
                GameMain.Server.ShowLagDiagnosticsButton.ToolTip = "Turns on the lag profiling information.";
#endif
            }));

            commands.Add(new Command("setclientcharacter", CommandType.Network, "setclientcharacter [client name] ; [character name]: Gives the client control of the specified character.", (string[] args) =>
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

            commands.Add(new Command("campaigninfo|campaignstatus", CommandType.Generic, "campaigninfo: Display information about the state of the currently active campaign.", (string[] args) =>
            {
                var campaign = GameMain.GameSession?.GameMode as CampaignMode;
                if (campaign == null)
                {
                    ThrowError("No campaign active!");
                    return;
                }

                campaign.LogState();
            }));

            commands.Add(new Command("campaigndestination|setcampaigndestination", CommandType.Generic, "campaigndestination [index]: Set the location to head towards in the currently active campaign.", (string[] args) =>
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

            commands.Add(new Command("spamevents", CommandType.DebugHide, "A debug command that immediately creates entity events for all items, characters and structures.", (string[] args) =>
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

            commands.Add(new Command("messagebox", CommandType.Network, "messagebox [text here]: Sends a messagebox to all connected clients displaying the text.", (string[] args) =>
            {
                if (args != null && args.Count() > 0)
                {
                    if (GameMain.Server != null)
                    {
                        var chatMsg = ChatMessage.Create(
                        "Server Message",
                        (string.Join(" ", args)),
                        (ChatMessageType)ChatMessageType.MessageBox,
                        null);

                        if (GameMain.Server.ConnectedClients.Count() > 0)
                        {
                            foreach (Client c in GameMain.Server.ConnectedClients)
                            {
                                GameMain.Server.SendChatMessage(chatMsg, c);
                            }
                        }
#if CLIENT
                        new GUIMessageBox("Server Broadcast", chatMsg.Text);
#endif
                    }
#if CLIENT
                    else
                    {
                        new GUIMessageBox("", string.Join(" ", args));
                    }
#endif
                }
                else
                {
                    DebugConsole.NewMessage("Messagebox [Text here] is the correct usage, messagebox cancelled (Command requires text)", Color.Red);
                }
            }));

            //NilMod Commands
            AddNilModCommands();

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

            if(GameMain.NilMod.DebugConsoleTimeStamp)
            {
                return Messages[selectedIndex].Text.Substring(22);
            }
            else
            {
                return Messages[selectedIndex].Text;
            }
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

            var matchingCharacters = Character.CharacterList.FindAll(c => ((!ignoreRemotePlayers || !c.IsRemotePlayer) && c.Name.ToLowerInvariant() == characterName) && GameMain.NilMod.convertinghusklist.Find(ch => ch.character == c) == null);

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
                //Custom NilMod Humans Code
                spawnedCharacter.TeamID = 1;
                //TODO - fix this for other translations where it may break?
                spawnedCharacter.Info = new CharacterInfo(Character.HumanConfigFile, spawnedCharacter.Name, Gender.None, JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == "mechanic" || jp.Name.ToLowerInvariant() != "captain" && (jp.Skills.Find(s => s.Name.ToLowerInvariant() == "construction")?.LevelRange.X >= 40)));
                spawnedCharacter.Info.TeamID = 1;
                HumanAIController humanai = (HumanAIController)spawnedCharacter.AIController;
                //Give the Fckr a use and make them run around being useful
                //Scratch that they turn into complete idiots usually
                //humanai.SetOrder(Order.PrefabList.Find(o => o.Name == "Fix Leaks"),"");

                spawnedCharacter.GiveJobItems(WayPoint.GetRandom(SpawnType.Human, spawnedCharacter.Info.Job));

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
                        spawnedCharacter = Character.Create(characterFile, spawnPosition);

#if CLIENT
                        if (GameMain.Server != null)
                        {
                            GameSession.inGameInfo.AddNoneClientCharacter(spawnedCharacter);
                            GameSession.inGameInfo.UpdateGameInfoGUIList();
                        }
#endif
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
#if CLIENT
            if (spawnedCharacter != null && GameMain.Server != null)
            {
                GameSession.inGameInfo.AddNoneClientCharacter(spawnedCharacter);
                GameSession.inGameInfo.UpdateGameInfoGUIList();
            }
#endif
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
                    if (Character.Spied != null)
                    {
                        spawnInventory = Character.Spied.Inventory;
                    }
                    else
                    {
                        spawnInventory = Character.Controlled == null ? null : Character.Controlled.Inventory;
                    }
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
            if (GameMain.NilMod.DebugConsoleTimeStamp)
            {
                Messages.Add(new ColoredText("[" + DateTime.Now.ToString() + "] " + msg, color, isCommand));
            }
            else
            {
                Messages.Add(new ColoredText(msg, color, isCommand));
            }

            //TODO: REMOVE
            Console.ForegroundColor = XnaToConsoleColor.Convert(color);
            
            if (GameMain.NilMod.DebugConsoleTimeStamp)
                {
                    Console.WriteLine("[" + DateTime.Now.ToString() + "] " + msg);
                }
                else
                {
                    Console.WriteLine(msg);
                }
            Console.ForegroundColor = ConsoleColor.White;

            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }            

#elif CLIENT
            lock (queuedMessages)
            {
                if (GameMain.NilMod != null && GameMain.NilMod.DebugConsoleTimeStamp)
                {
                    queuedMessages.Enqueue(new ColoredText("[" + DateTime.Now.ToString() + "] " + msg, color, isCommand));
                }
                else
                {
                    queuedMessages.Enqueue(new ColoredText(msg, color, isCommand));
                }
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

        //NilMod Custom Commands
        public static void AddNilModCommands()
        {
            commands.Add(new Command("rechargepower", CommandType.GamePower, "rechargepower [SUBID]: Recharges every power container on a submarine.", (string[] args) =>
            {
                int subtorecharge = -1;

                if (args.Length < 1)
                {
                    if (Submarine.MainSubs[0] != null)
                    {
                        subtorecharge = 0;
                    }
                    else
                    {
                        NewMessage("Cannot recharge submarine - There are no submarines loaded yet.", Color.Red);
                    }
                }
                else
                {
                    if (args[0].All(Char.IsDigit))
                    {
                        if (Convert.ToInt16(args[0]) <= Submarine.MainSubs.Count() - 1)
                        {
                            subtorecharge = Convert.ToInt16(args[0]);
                        }
                        else
                        {
                            NewMessage("MainSub ID Range is from 0 to " + (Submarine.MainSubs.Count() - 1), Color.Red);
                        }
                    }
                    else
                    {
                        NewMessage("Command only accepts numerics starting from 0", Color.Red);
                    }
                }
                //Not Null? Lets try it! XD
                if (GameMain.Server != null)
                {
                    if (Submarine.MainSubs[subtorecharge] != null)
                    {
                        GameMain.Server.GrantPower(subtorecharge);
                    }
                    else
                    {
                        NewMessage("Cannot recharge submarine - Submarine ID: " + subtorecharge + " Is not loaded in the game (If not multiple submarines use 0 or leave blank)", Color.Red);
                    }
                }
                else
                {
                    NewMessage("Cannot recharge submarine - The Server is not running.", Color.Red);
                }
            }));

            commands.Add(new Command("forceshuttle", CommandType.GamePower, "forceshuttle: Recalls and then sends out the respawn shuttle.", (string[] args) =>
            {
                if (GameMain.Server != null)
                {
                    if (GameMain.Server.respawnManager != null)
                    {
                        GameMain.Server.respawnManager.ForceShuttle();
                    }
                    else
                    {
                        NewMessage("The respawn manager is currently not loaded - No shuttle exists to force out", Color.Red);
                    }
                }
                else
                {
                    NewMessage("The respawn manager is currently not loaded - The Server is not running.", Color.Red);
                }
            }));

            commands.Add(new Command("recallshuttle", CommandType.GamePower, "recallshuttle: Recalls the respawn shuttle immediately.", (string[] args) =>
            {
                if (GameMain.Server != null)
                {
                    if (GameMain.Server.respawnManager != null)
                    {
                        GameMain.Server.respawnManager.RecallShuttle();
                    }
                    else
                    {
                        NewMessage("The respawn manager is currently not loaded - No shuttle exists to recall", Color.Red);
                    }
                }
                else
                {
                    NewMessage("The respawn manager is currently not loaded - The Server is not running.", Color.Red);
                }
            }));

            commands.Add(new Command("setrespawns", CommandType.DebugHide, "setrespawns [new Player Respawn Count]: Sets the number of characters left that can respawn shuttle will dispatch this round.", (string[] args) =>
            {
                //Code Here
            }));

            commands.Add(new Command("getrespawns", CommandType.DebugHide, "getrespawns: Gets the number of characters left that can respawn via a shuttle.", (string[] args) =>
            {
                //Code Here
            }));
#if CLIENT
            commands.Add(new Command("movemainsub|movesub|teleportsub|teleportmainsub", CommandType.GamePower, "movemainsub|movesub|teleportsub|teleportmainsub [SUBID]: Teleports submarine to cursor then sets it to maintain position.", (string[] args) =>
            {
                int subtotp = -1;

                if (args.Length < 1)
                {
                    if (Submarine.MainSubs[0] != null)
                    {
                        subtotp = 0;
                    }
                    else
                    {
                        NewMessage("Cannot teleport submarine - There are no submarines loaded yet.", Color.Red);
                    }
                }
                else
                {
                    if (args[0].All(Char.IsDigit))
                    {
                        if (Convert.ToInt16(args[0]) <= Submarine.MainSubs.Count() - 1)
                        {
                            subtotp = Convert.ToInt16(args[0]);
                        }
                        else
                        {
                            NewMessage("MainSub ID Range is from 0 to " + (Submarine.MainSubs.Count() - 1), Color.Red);
                        }
                    }
                    else
                    {
                        NewMessage("Command only accepts numerics starting from 0", Color.Red);
                    }
                }
                //Not Null? Lets try it! XD
                if (GameMain.Server != null)
                {
                    if (subtotp >= 0)
                    {
                        if (Submarine.MainSubs[subtotp] != null)
                        {
                            var cam = GameMain.GameScreen.Cam;
                            GameMain.Server.MoveSub(subtotp, cam.ScreenToWorld(PlayerInput.MousePosition));
                        }
                        else
                        {
                            NewMessage("Cannot teleport submarine - Submarine ID: " + subtotp + " Is not loaded in the game (If not multiple submarines use 0 or leave blank)", Color.Red);
                        }
                    }
                }
                else
                {
                    NewMessage("Cannot teleport submarine - The Server is not running.", Color.Red);
                }
            }));

            commands.Add(new Command("lagprofiler|showlagdiagnostics|togglelagdiagnostics", CommandType.Generic, "lagprofiler: Toggles the visibility of the CPUTime statistics UI.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                GameMain.Server.ShowNetStats = false;
                GameMain.Server.ShowLagDiagnostics = !GameMain.Server.ShowLagDiagnostics;
                if (GameMain.Server.ShowLagDiagnostics)
                {
                    GameMain.Server.ShowLagDiagnosticsButton.Text = "Lag Profiler Off";
                    GameMain.Server.ShowLagDiagnosticsButton.ToolTip = "Turns off the lag profiling information.";
                }
                else
                {
                    GameMain.Server.ShowLagDiagnosticsButton.Text = "Lag Profiler On";
                    GameMain.Server.ShowLagDiagnosticsButton.ToolTip = "Turns on the lag profiling information.";
                }
                GameMain.Server.ShowNetStatsButton.Text = "NetStats On";
                GameMain.Server.ShowNetStatsButton.ToolTip = "Turns on the netstats screen which shows the latencies and IP Addresses of connections.";
            }));
#endif
            commands.Add(new Command("nilmodreload", CommandType.Generic, "nilmodreload: Reloads then saves NilMod Settings during runtime, good for tweaking mid-round!", (string[] args) =>
            {
                //If this is a client
                if (GameMain.Client != null)
                {
                    //DebugConsole.NewMessage("Clients may not use this command even if the server allows its usage.", Color.Red);
                    return;
                }

                //Single player or server
                GameMain.NilMod.Load(false);

                //If this is a server
                if(GameMain.Server != null)
                {
                    if(GameMain.Server.ConnectedClients.Count > 0)
                    {
                        int nilModClientCounter = 0;
                        for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
                        {
                            if(GameMain.Server.ConnectedClients[i].IsNilModClient)
                            {
                                GameMain.Server.ConnectedClients[i].RequiresNilModSync = true;
                                //Have the packets to send out not all in the same update for each nilmodclient
                                GameMain.Server.ConnectedClients[i].NilModSyncResendTimer = NilMod.SyncResendInterval + (nilModClientCounter * 0.03f);
                                nilModClientCounter += 1;
                            }
                        }
                    }
                }
            }));

            commands.Add(new Command("nilmodreset", CommandType.Generic, "nilmodreset: Resets NilMod Settings during runtime (Does not save), used to get default barotrauma setup.", (string[] args) =>
            {
                //This is a client
                if (GameMain.Client != null) return;

                GameMain.NilMod.ResetToDefault();
            }));

            commands.Add(new Command("nilmodsave", CommandType.Generic, "nilmodsave: Saves the current NilMod settings without loading them.", (string[] args) =>
            {
                //This is a client
                if (GameMain.Client != null) return;

                GameMain.NilMod.Save();
            }));

            commands.Add(new Command("listcreatures", CommandType.Character, "listcreatures: A command that displays all living none-player controlled creatures on the map, dead or alive.", (string[] args) =>
            {
                Dictionary<string, int> creaturecount = new Dictionary<string, int>();
                foreach (Character c in Character.CharacterList)
                {
                    //Only count for this one ENEMY AIs
                    //if (!(c.AIController is EnemyAIController)) continue;

                    //Above code didn't seem so good, lets simply not check Human AI's and remote players instead.
                    //if (!c.IsRemotePlayer && !(c.AIController is HumanAIController))
                    if (!c.IsRemotePlayer && !(c.AIController is HumanAIController))
                    {
                        if (creaturecount.ContainsKey(c.Name.ToLowerInvariant()))
                        {
                            creaturecount[c.Name.ToLowerInvariant()] += 1;
                        }
                        else
                        {
                            creaturecount.Add(c.Name.ToLowerInvariant(), 1);
                        }
                    }
                }
                if (creaturecount.Count() != 0)
                {
                    foreach (KeyValuePair<string, int> countof in creaturecount)
                    {
                        NewMessage("AIEntity: \"" + countof.Key.ToString() + "\" Count: " + countof.Value.ToString(), Color.White);
                    }
                }
                else
                {
                    NewMessage("No creatures could be found (Has the round started yet?)", Color.Red);
                }
            }));

            commands.Add(new Command("listplayers", CommandType.Character, "listplayers: Used to get the count of players that exist.", (string[] args) =>
            {
                Dictionary<string, int> playercount = new Dictionary<string, int>();
                foreach (Character c in Character.CharacterList)
                {
                    //Only count for this one PLAYERS
                    if (c.IsRemotePlayer)
                    {
                        if (playercount.ContainsKey(c.Name.ToLowerInvariant()))
                        {
                            playercount[c.Name.ToLowerInvariant()] += 1;
                        }
                        else
                        {
                            playercount.Add(c.Name.ToLowerInvariant(), 1);
                        }
                    }
                }

                foreach (KeyValuePair<string, int> countof in playercount)
                {
                    //var playerlistchar = FindMatchingCharacter(countof.Key.ToString() + countof.Value.ToString(),false);

                    NewMessage("Player: \"" + countof.Key.ToString() + "\" Count: " + countof.Value.ToString(), Color.White);

                }
            }));

            commands.Add(new Command("finditem", CommandType.Spawning, "finditem [Partial Item Name]: Searches all possible spawnable items based off what you type, leave blank to list everything.", (string[] args) =>
            {
                List<ItemPrefab> FoundItems = new List<ItemPrefab>();
                string SearchItemName = null;
                if (args.Length < 1)
                {
                    SearchItemName = "";
                }
                else
                {
                    SearchItemName = string.Join(" ", args.Take(args.Length)).ToLowerInvariant();
                }

                //Find all the unique items and populate the list
                foreach (MapEntityPrefab searchitem in MapEntityPrefab.List)
                {
                    if (searchitem.Name.ToLowerInvariant().Contains(SearchItemName.ToLowerInvariant())
                    || searchitem.Name.ToLowerInvariant() == SearchItemName.ToLowerInvariant()
                    || searchitem.Category.ToString().ToLowerInvariant().Contains(SearchItemName.ToLowerInvariant())
                    || searchitem.Category.ToString().ToLowerInvariant() == SearchItemName.ToLowerInvariant()
                    )
                    {
                        var itemPrefab = searchitem as ItemPrefab;
                        if (searchitem is ItemPrefab && searchitem.Name != null) FoundItems.Add(itemPrefab);
                    }
                }
                List<string> FoundItemsText = new List<string>();
                foreach(ItemPrefab item in FoundItems)
                {
                    FoundItemsText.Add(item.Category + " - " + @"""" + item.Name + @"""");
                }
                //Sort the list
                FoundItemsText.Sort();
                //Now Display them in alphabetical order
                foreach (string itemtext in FoundItemsText)
                {
                    DebugConsole.NewMessage(itemtext, Color.White);
                }
            }));

            commands.Add(new Command("itemdetail", CommandType.Debug, "itemdetail [Partial Item Name]: Searches all possible spawnable items based off what you type, leave blank to list everything.", (string[] args) =>
            {
                //List<ItemPrefab> FoundItems = new List<ItemPrefab>();
                string SearchItemName = null;
                if (args.Length < 1)
                {
                    SearchItemName = "";
                }
                else
                {
                    SearchItemName = string.Join(" ", args.Take(args.Length)).ToLowerInvariant();
                }

                if (SearchItemName == "")
                {
                    return;
                }

                Boolean found = false;

                //Find all the unique items and populate the list
                foreach (MapEntityPrefab searchitem in MapEntityPrefab.List)
                {
                    if (searchitem.Name.ToLowerInvariant() == SearchItemName.ToLowerInvariant())
                    {
                        if (searchitem is ItemPrefab)
                        {
                            string temp;
                            var itemPrefab = searchitem as ItemPrefab;
                            DebugConsole.NewMessage(@"Found Matching item: """ + itemPrefab.Name + @""".", Color.Cyan);
                            DebugConsole.NewMessage("Category: " + itemPrefab.Category, Color.Cyan);

                            if (itemPrefab.Aliases != null && itemPrefab.Aliases.Count() > 0)
                            {
                                temp = "";
                                foreach (var Alias in itemPrefab.Aliases)
                                {
                                    temp = temp + ", " + Alias.ToString();
                                }

                                DebugConsole.NewMessage("Aliases: " + temp.Substring(2).Trim(), Color.Cyan);
                            }
                            else
                            {
                                DebugConsole.NewMessage("Item has no Aliases.", Color.Cyan);
                            }
                            
                            DebugConsole.NewMessage("Description: " + itemPrefab.Description, Color.Cyan);

                            if (itemPrefab.Tags != null && itemPrefab.Tags.Count() > 0)
                            {
                                temp = "";
                                foreach (var tag in itemPrefab.Tags)
                                {
                                    temp = temp + ", " + tag.ToString();
                                }

                                DebugConsole.NewMessage("Tags: " + temp.Substring(2).Trim(), Color.Cyan);
                            }
                            else
                            {
                                DebugConsole.NewMessage("Item has no Tags.", Color.Cyan);
                            }
                            
                            DebugConsole.NewMessage("Price: " + itemPrefab.Price, Color.Green);

                            if (itemPrefab.DeconstructItems != null && itemPrefab.DeconstructItems.Count() > 0)
                            {
                                foreach (var deconstructitem in itemPrefab.DeconstructItems)
                                {
                                    DebugConsole.NewMessage(@"Deconstruct Item: """ + deconstructitem.ItemPrefabName.ToString() + @"""" + " Min: " + deconstructitem.MinCondition + ", Max: " + deconstructitem.MaxCondition, Color.Green);
                                }
                            }
                            else
                            {
                                DebugConsole.NewMessage("Deconstructs into nothing.", Color.Green);
                            }

                            DebugConsole.NewMessage("CanSpriteFlipX: " + itemPrefab.CanSpriteFlipX, Color.Green);
                            DebugConsole.NewMessage("CanUseOnSelf: " + itemPrefab.CanUseOnSelf, Color.Green);
                            DebugConsole.NewMessage("ConfigFile: " + itemPrefab.ConfigFile, Color.Green);
                            DebugConsole.NewMessage("DeconstructTime: " + itemPrefab.DeconstructTime.ToString(), Color.Green);
                            DebugConsole.NewMessage("FireProof: " + itemPrefab.FireProof, Color.Green);
                            DebugConsole.NewMessage("FocusOnSelected: " + itemPrefab.FocusOnSelected, Color.Green);
                            DebugConsole.NewMessage("Health: " + itemPrefab.Health, Color.Green);
                            DebugConsole.NewMessage("ImpactTolerance: " + itemPrefab.ImpactTolerance, Color.Green);
                            DebugConsole.NewMessage("Indestructible: " + itemPrefab.Indestructible, Color.Green);
                            DebugConsole.NewMessage("InteractDistance: " + itemPrefab.InteractDistance, Color.Green);
                            DebugConsole.NewMessage("InteractPriority: " + itemPrefab.InteractPriority, Color.Green);
                            DebugConsole.NewMessage("InteractThroughWalls: " + itemPrefab.InteractThroughWalls, Color.Green);
                            DebugConsole.NewMessage("Linkable: " + itemPrefab.Linkable, Color.Green);
                            DebugConsole.NewMessage("OffsetOnSelected: " + itemPrefab.OffsetOnSelected, Color.Green);
                            DebugConsole.NewMessage("ResizeHorizontal: " + itemPrefab.ResizeHorizontal, Color.Green);
                            DebugConsole.NewMessage("ResizeVertical: " + itemPrefab.ResizeVertical, Color.Green);
                            DebugConsole.NewMessage("Size: " + itemPrefab.Size.ToString(), Color.Green);
                            DebugConsole.NewMessage("SpriteColor: " + itemPrefab.SpriteColor.ToString(), Color.Green);
                            if (itemPrefab.sprite != null)
                            {
                                DebugConsole.NewMessage("Sprite Depth: " + itemPrefab.sprite.Depth, Color.Green);
                                DebugConsole.NewMessage("Sprite FilePath: " + itemPrefab.sprite.FilePath, Color.Green);

                                DebugConsole.NewMessage("Sprite Origin: " + itemPrefab.sprite.Origin.ToString(), Color.Green);
                                DebugConsole.NewMessage("Sprite Rotation: " + itemPrefab.sprite.rotation, Color.Green);
                                DebugConsole.NewMessage("Sprite SourceRect: " + itemPrefab.sprite.SourceRect, Color.Green);
                            }
                            //Create a temporary item that can be removed after the checks are done
                            Item tempitem = new Item(itemPrefab, new Vector2(300000, 300000), null);

                            if (tempitem.AllowedSlots != null && tempitem.AllowedSlots.Count() > 0)
                            {
                                temp = "";
                                foreach (var slot in tempitem.AllowedSlots)
                                {
                                    temp = temp + ", " + slot.ToString();
                                }

                                DebugConsole.NewMessage("AllowedSlots: " + temp.Substring(2).Trim(), Color.Yellow);
                            }
                            else
                            {
                                DebugConsole.NewMessage("Item has no AllowedSlots.", Color.Yellow);
                            }

                            if (tempitem.components != null && tempitem.components.Count() > 0)
                            {
                                foreach (var component in tempitem.components)
                                {
                                    if (component.SerializableProperties != null && component.SerializableProperties.Count() > 0)
                                    {
                                        DebugConsole.NewMessage("   - Component: " + component.Name, Color.Green);

                                        foreach (var property in component.SerializableProperties)
                                        {
                                            //Don't read a list of serializableproperties, the base list seems to grab them too.
                                            if (property.Key.ToLowerInvariant() == "serializableproperties") continue;

                                            //Code for reading different property types differently : >
                                            switch (property.Value.PropertyType.ToString().Split('`')[0])
                                            {
                                                //Lists probably need more code
                                                case "System.Collections.Generic.List":
                                                    //Switch for specific types of list reading
                                                    switch (property.Value.PropertyType.ToString().Split('`')[1])
                                                    {
                                                        case "1[Barotrauma.DamageModifier]":
                                                            List<DamageModifier> damagemodifiers = (List<DamageModifier>)property.Value.GetValue();
                                                            DebugConsole.NewMessage("      - Propertylist: " + property.Key + " = " + damagemodifiers.Count() + " Items.", Color.Yellow);
                                                            foreach (DamageModifier modifier in damagemodifiers)
                                                            {
                                                                DebugConsole.NewMessage(
                                                                    "         - DamageModifier: Damage Type:" + modifier.DamageType
                                                                    + ", Damage:" + modifier.DamageMultiplier
                                                                    + "%, Bleed:" + modifier.BleedingMultiplier
                                                                    + "%, ArmorSector: (" + Math.Round(modifier.ArmorSector.X, 1)
                                                                    + "," + Math.Round(modifier.ArmorSector.Y, 1)
                                                                    + "), Deflect:" + modifier.DeflectProjectiles, Color.Yellow);
                                                            }
                                                            break;
                                                        case "1[Barotrauma.InvSlotType]":
                                                        //break;
                                                        case "1[Barotrauma.RelatedItem]":
                                                        //break;
                                                        default:
                                                            //property.Value.PropertyType.ToString()
                                                            DebugConsole.NewMessage("      - Property: " + property.Key + " = LIST: " + property.Value.PropertyType.ToString().Split('`')[1] + " reading is Unimplemented.", Color.IndianRed);
                                                            break;
                                                    }
                                                    /*
                                                    List<object> list = (List<object>)property.Value.GetValue();
                                                    if (list.Count() > 0)
                                                    {
                                                        foreach(string listitem in list)
                                                        {
                                                            DebugConsole.NewMessage(listitem.ToString(), Color.Yellow);
                                                        }
                                                    }
                                                    */
                                                    break;
                                                case "System.Boolean":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.GetValue().ToString() + " (Bool)", Color.Yellow);
                                                    break;
                                                case "System.String":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + @" = """ + property.Value.GetValue().ToString() + @""" (String)", Color.Yellow);
                                                    break;
                                                case "System.Single":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.GetValue().ToString() + " (Float)", Color.Yellow);
                                                    break;
                                                case "System.Int32":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.GetValue().ToString() + " (Int32)", Color.Yellow);
                                                    break;
                                                case "Microsoft.Xna.Framework.Vector2":
                                                    Vector2 vector2value = (Vector2)property.Value.GetValue();
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = (" + vector2value.X + "," + vector2value.Y + ") (Vector2)", Color.Yellow);
                                                    break;
                                                case "Barotrauma.InputType":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.GetValue().ToString() + " (InputType)", Color.Yellow);
                                                    break;
                                                case "Barotrauma.Item":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.GetValue().ToString() + " (InputType)", Color.Yellow);
                                                    break;
                                                case "Barotrauma.Character":
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + " (Character - shouldn't be modified)", Color.Yellow);
                                                    break;
                                                default:
                                                    DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.PropertyType.ToString() + "(Reading not implemented yet).", Color.IndianRed);
                                                    break;
                                                    //DebugConsole.NewMessage("      - Property: " + property.Key + " = " + property.Value.ToString(), Color.Yellow);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        DebugConsole.NewMessage("      - No Properties.", Color.Yellow);
                                    }
                                }
                            }
                            else
                            {
                                DebugConsole.NewMessage("Item has no components to list.", Color.Yellow);
                            }

                            tempitem.Remove();
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    DebugConsole.NewMessage(@"Item: """ + SearchItemName + @""" not found, performing search", Color.Red);
                    DebugConsole.ExecuteCommand("finditem " + SearchItemName);
                }
            }));

            commands.Add(new Command("checktraitor|traitor|traitorcheck",CommandType.Network, "checktraitor: States the current traitor if there was one and his target.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null)
                {
                    DebugConsole.NewMessage("No traitor existed this game.", Color.Cyan);
                    return;
                }
                //Needs Fixing
                //DebugConsole.NewMessage(traitorManager.TraitorCharacter + " Is the traitor and his target is: " + traitorManager.TargetCharacter, Color.Cyan);
            }));

            commands.Add(new Command("debugarmor|debugarmour", CommandType.Debug, "debugarmor|debugarmour [Armour Value]: Shows a series of damage estimations for a lone modifier using nilmod/Vanilla armour calculation! - 1.0 by default is no damage and 0.5 is half (Configure nilmodsettings.xml to change behaviour!)", (string[] args) =>
            {
                float armouramount;

                if (args.Length < 1)
                {
                    armouramount = 0f;
                    GameMain.NilMod.TestArmour(armouramount);
                }
                else
                {
                    if (float.TryParse(args[0], out armouramount))
                    {
                        if (Convert.ToSingle(args[0]) >= 0f)
                        {
                            GameMain.NilMod.TestArmour(armouramount);
                        }
                        else
                        {
                            NewMessage("Command requires positive or 0 armour values.", Color.Red);
                        }
                    }
                    else
                    {
                        NewMessage("Command only accepts decimal numerics.", Color.Red);
                    }
                }
            }));

            commands.Add(new Command("blankcommand", CommandType.DebugHide, "blankcommand: Does Nothing at all", (string[] args) =>
            {
                //Code
            }));

            commands.Add(new Command("listentityids", CommandType.Debug, "listentityids: Lists the IDs of all entities", (string[] args) =>
            {
                List<Entity> entitylist = Entity.GetEntityList();

                for (int i = 0; i < entitylist.Count() - 1; i++)
                {
                    if (entitylist[i] != null)
                    {
                        if (entitylist[i] is Character)
                        {
                            DebugConsole.NewMessage("ID: " + entitylist[i].ID + " = " + entitylist[i].ToString(), Color.Aqua);
                        }
                    }
                }
            }));

            commands.Add(new Command("togglecrush|toggleabyss", CommandType.GamePower, "togglecrush: Toggles the games abyss damage for all submarines.", (string[] args) =>
            {
                if (GameMain.Client != null) return;

                GameMain.NilMod.DisableCrushDamage = !GameMain.NilMod.DisableCrushDamage;
#if CLIENT
                if (GameMain.Server != null)
                {
                    if (GameMain.NilMod.DisableCrushDamage)
                    {
                        GameMain.Server.ToggleCrushButton.Text = "Depth Crush On";
                        GameMain.Server.ToggleCrushButton.ToolTip = "Turns on Abyss Crushing damage to submarines.";
                    }
                    else
                    {
                        GameMain.Server.ToggleCrushButton.Text = "Depth Crush Off";
                        GameMain.Server.ToggleCrushButton.ToolTip = "Turns off Abyss Crushing damage to submarines.";
                    }
                }
#endif
                NewMessage(GameMain.NilMod.DisableCrushDamage ? "Abyss crush damage off" : "Abyss crush damage on", Color.White);
            }));
        }
    }
}
