using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using FarseerPhysics;
using Barotrauma.Items.Components;
using System.Threading;
using System.IO;
using System.Text;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        public partial class Command
        {
            /// <summary>
            /// Executed server-side when a client attempts to use the command.
            /// </summary>
            public Action<Client, Vector2, string[]> OnClientRequestExecute;

            public void ServerExecuteOnClientRequest(Client client, Vector2 cursorWorldPos, string[] args)
            {
                if (!CheatsEnabled && IsCheat)
                {
                    NewMessage("Client \"" + client.Name + "\" attempted to use the command \"" + names[0] + "\". Cheats must be enabled using \"enablecheats\" before the command can be used.", Color.Red);
                    GameMain.Server.SendConsoleMessage("You need to enable cheats using the command \"enablecheats\" before you can use the command \"" + names[0] + "\".", client);

                    if (Steam.SteamManager.USE_STEAM)
                    {
                        NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                        GameMain.Server.SendConsoleMessage("Enabling cheats will disable Steam achievements during this play session.", client);
                        return;
                    }

                    return;
                }

                if (OnClientRequestExecute == null)
                {
                    if (OnExecute == null) return;
                    OnExecute(args);
                }
                else
                {
                    OnClientRequestExecute(client, cursorWorldPos, args);
                }
            }
        }

        public static List<string> QueuedCommands = new List<string>();
        public static Thread InputThread;

        public static void Update()
        {
            lock (QueuedCommands)
            {
                while (QueuedCommands.Count > 0)
                {
                    ExecuteCommand(QueuedCommands[0]);
                    QueuedCommands.RemoveAt(0);
                }
            }
            if (InputThread == null)
            {
                lock (queuedMessages)
                {
                    while (queuedMessages.Count > 0)
                    {
                        var msg = queuedMessages.Dequeue();
                        Messages.Add(msg);
                        if (GameSettings.SaveDebugConsoleLogs)
                        {
                            unsavedMessages.Add(msg);
                            if (unsavedMessages.Count >= messagesPerFile)
                            {
                                SaveLogs();
                                unsavedMessages.Clear();
                            }
                        }
                    }
                    if (Messages.Count > MaxMessages)
                    {
                        Messages.RemoveRange(0, Messages.Count - MaxMessages);
                    }
                }
            }
        }


        public static void UpdateCommandLine()
        {
            try
            {
                Console.Clear();
                string input = "";
                int memoryIndex = -1;
                List<string> commandMemory = new List<string>();
                while (true)
                {
                    int consoleWidth = Console.WindowWidth;
                    if (consoleWidth < 5) consoleWidth = 5;
                    int consoleHeight = Console.WindowHeight;
                    if (consoleHeight < 5) consoleHeight = 5;

                    //dequeue messages
                    lock (queuedMessages)
                    {
                        if (queuedMessages.Count > 0)
                        {
                            int inputLines = Math.Max((int)Math.Ceiling(input.Length / (float)Console.WindowWidth), 1);
                            Console.CursorLeft = 0;
                            Console.Write(new string(' ', consoleWidth));
                            Console.CursorTop = Math.Max(Console.CursorTop - inputLines, 0);
                            Console.CursorLeft = 0;
                            while (queuedMessages.Count > 0)
                            {
                                ColoredText msg = queuedMessages.Dequeue();
                                Messages.Add(msg);
                                if (GameSettings.SaveDebugConsoleLogs)
                                {
                                    unsavedMessages.Add(msg);
                                    if (unsavedMessages.Count >= messagesPerFile)
                                    {
                                        SaveLogs();
                                        unsavedMessages.Clear();
                                    }
                                }

                                string msgTxt = msg.Text;

                                if (msg.IsCommand) commandMemory.Add(msgTxt);

                                int paddingLen = consoleWidth - (msg.Text.Length % consoleWidth)-1;
                                msgTxt += new string(' ', paddingLen>0 ? paddingLen : 0);

                                Console.ForegroundColor = XnaToConsoleColor.Convert(msg.Color);
                                Console.WriteLine(msgTxt);
                            }
                            RewriteInputToCommandLine(input);
                        }
                        if (Messages.Count > MaxMessages)
                        {
                            Messages.RemoveRange(0, Messages.Count - MaxMessages);
                        }
                    }

                    //read player input
                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(true);
                        switch (key.Key)
                        {
                            case ConsoleKey.Enter:
                                lock (QueuedCommands)
                                {
                                    QueuedCommands.Add(input);
                                }
                                input = "";
                                memoryIndex = -1;
                                break;
                            case ConsoleKey.Backspace:
                                if (input.Length > 0) input = input.Substring(0, input.Length - 1);
                                memoryIndex = -1;
                                break;
                            case ConsoleKey.LeftArrow:
                                input = AutoComplete(input, -1);
                                break;
                            case ConsoleKey.RightArrow:
                                input = AutoComplete(input, 1);
                                break;
                            case ConsoleKey.UpArrow:
                                memoryIndex--;
                                if (memoryIndex < 0) memoryIndex = commandMemory.Count - 1;
                                if (memoryIndex >= commandMemory.Count) memoryIndex = commandMemory.Count - 1;
                                if (memoryIndex >= 0)
                                {
                                    input = commandMemory[memoryIndex];
                                }
                                break;
                            case ConsoleKey.DownArrow:
                                memoryIndex++;
                                if (memoryIndex < 0) memoryIndex = 0;
                                if (memoryIndex >= commandMemory.Count) memoryIndex = 0;
                                if (commandMemory.Count>0)
                                {
                                    input = commandMemory[memoryIndex];
                                }
                                break;
                            case ConsoleKey.Tab:
                                if (input.Length > 0)
                                {
                                    input = AutoComplete(input, 0);
                                    memoryIndex = -1;
                                }
                                break;
                            default:
                                if (key.KeyChar != 0)
                                {
                                    input += key.KeyChar;
                                    memoryIndex = -1;
                                }
                                ResetAutoComplete();
                                break;
                        }
                        
                        RewriteInputToCommandLine(input);
                    }
                    
                    //TODO: be more clever about it
                    Thread.Sleep(10); //sleep for 10ms to not pin the CPU super hard
                }
            }
            catch (ThreadAbortException)
            {
                //don't have anything to do here yet
            }
#if !DEBUG
            catch (Exception exception)
            {
                StreamWriter sw = new StreamWriter("inputthreadcrash.log");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Barotrauma Dedicated Server input thread crash report (generated on " + DateTime.Now + ")");
                sb.AppendLine("\n");
                sb.AppendLine("Exception: " + exception.Message);
                sb.AppendLine("Target site: " + exception.TargetSite.ToString());
                sb.AppendLine("Stack trace: ");
                sb.AppendLine(exception.StackTrace);

                sw.WriteLine(sb.ToString());
                sw.Close();

                GameMain.ShouldRun = false;
            }
#endif
        }

        private static void RewriteInputToCommandLine(string input)
        {
            if (Console.WindowWidth == 0 || Console.WindowHeight == 0) { return; }

            int consoleWidth = Math.Max(Console.WindowWidth, 5);
            int inputLines = Math.Max((int)Math.Ceiling(input.Length / (float)consoleWidth), 1);
            int cursorLine = Math.Max((int)Math.Ceiling((input.Length + 1) / (float)consoleWidth), 1);

            try
            {
                Console.WriteLine(""); Console.CursorTop -= inputLines;
                       
                string ln = input.Length > 0 ? AutoComplete(input, 0) : "";
                ln += new string(' ', consoleWidth - (ln.Length % consoleWidth));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.CursorLeft = 0;
                Console.Write(ln);
                Console.ForegroundColor = ConsoleColor.White;
                Console.CursorLeft = 0;
                Console.CursorTop -= cursorLine;
                Console.Write(input);
                Console.CursorLeft = input.Length % consoleWidth;
            }
            catch (Exception e)
            {
                string errorMsg = "Failed to write input to command line (window width: " + Console.WindowWidth + ", window height: " + Console.WindowHeight + ", inputLines:" + inputLines + ")\n"
                    + e.Message + "\n" + e.StackTrace;
                GameAnalyticsManager.AddErrorEventOnce("DebugConsole.RewriteInputToCommandLine", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
            }
        }

        private static void AssignOnClientRequestExecute(string names, Action<Client, Vector2, string[]> onClientRequestExecute)
        {
            var matchingCommand = commands.Find(c => c.names.Intersect(names.Split('|')).Count() > 0);
            if (matchingCommand == null)
            {
                throw new Exception("AssignOnClientRequestExecute failed. Command matching the name(s) \"" + names + "\" not found.");
            }
            else
            {
                matchingCommand.OnClientRequestExecute = onClientRequestExecute;
            }
        }

        private static void InitProjectSpecific()
        {
            AssignOnExecute("botcount", (string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                int botCount = GameMain.Server.ServerSettings.BotCount;
                int.TryParse(args[0], out botCount);
                GameMain.NetLobbyScreen.SetBotCount(botCount);
                NewMessage("Set the number of bots to " + botCount, Color.White);
            });

            AssignOnExecute("botspawnmode", (string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                if (Enum.TryParse(args[0], true, out BotSpawnMode spawnMode))
                {
                    GameMain.NetLobbyScreen.SetBotSpawnMode(spawnMode);
                    NewMessage("Set bot spawn mode to " + spawnMode, Color.White);
                }
                else
                {
                    NewMessage("\"" + args[0] + "\" is not a valid bot spawn mode. (Valid modes are Fill and Normal)", Color.White);
                }
            });

            AssignOnExecute("autorestart", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                bool enabled = GameMain.Server.ServerSettings.AutoRestart;
                if (args.Length > 0)
                {
                    bool.TryParse(args[0], out enabled);
                }
                else
                {
                    enabled = !enabled;
                }
                if (enabled != GameMain.Server.ServerSettings.AutoRestart)
                {
                    if (GameMain.Server.ServerSettings.AutoRestartInterval <= 0) GameMain.Server.ServerSettings.AutoRestartInterval = 10;
                    GameMain.Server.ServerSettings.AutoRestartTimer = GameMain.Server.ServerSettings.AutoRestartInterval;
                    GameMain.Server.ServerSettings.AutoRestart = enabled;
#if CLIENT
                    //TODO: reimplement
                    GameMain.NetLobbyScreen.SetAutoRestart(enabled, GameMain.Server.AutoRestartTimer);
#endif
                    GameMain.NetLobbyScreen.LastUpdateID++;
                }
                NewMessage(GameMain.Server.ServerSettings.AutoRestart ? "Automatic restart enabled." : "Automatic restart disabled.", Color.White);
            });

            AssignOnExecute("autorestartinterval", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    if (int.TryParse(args[0], out int parsedInt))
                    {
                        if (parsedInt >= 0)
                        {
                            GameMain.Server.ServerSettings.AutoRestart = true;
                            GameMain.Server.ServerSettings.AutoRestartInterval = parsedInt;
                            if (GameMain.Server.ServerSettings.AutoRestartTimer >= GameMain.Server.ServerSettings.AutoRestartInterval) GameMain.Server.ServerSettings.AutoRestartTimer = GameMain.Server.ServerSettings.AutoRestartInterval;
                            NewMessage("Autorestart interval set to " + GameMain.Server.ServerSettings.AutoRestartInterval + " seconds.", Color.White);
                        }
                        else
                        {
                            GameMain.Server.ServerSettings.AutoRestart = false;
                            NewMessage("Autorestart disabled.", Color.White);
                        }
#if CLIENT
                        //TODO: redo again
                        GameMain.NetLobbyScreen.SetAutoRestart(GameMain.Server.AutoRestart, GameMain.Server.AutoRestartTimer);
#endif
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }
                }
            });

            AssignOnExecute("autorestarttimer", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length > 0)
                {
                    if (int.TryParse(args[0], out int parsedInt))
                    {
                        if (parsedInt >= 0)
                        {
                            GameMain.Server.ServerSettings.AutoRestart = true;
                            GameMain.Server.ServerSettings.AutoRestartTimer = parsedInt;
                            if (GameMain.Server.ServerSettings.AutoRestartInterval <= GameMain.Server.ServerSettings.AutoRestartTimer) GameMain.Server.ServerSettings.AutoRestartInterval = GameMain.Server.ServerSettings.AutoRestartTimer;
                            GameMain.NetLobbyScreen.LastUpdateID++;
                            NewMessage("Autorestart timer set to " + GameMain.Server.ServerSettings.AutoRestartTimer + " seconds.", Color.White);
                        }
                        else
                        {
                            GameMain.Server.ServerSettings.AutoRestart = false;
                            NewMessage("Autorestart disabled.", Color.White);
                        }
#if CLIENT
                        GameMain.NetLobbyScreen.SetAutoRestart(GameMain.Server.AutoRestart, GameMain.Server.AutoRestartTimer);
#endif
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }
                }
            });

            AssignOnExecute("startwhenclientsready", (string[] args) =>
            {
                if (GameMain.Server == null) { return; }
                bool enabled = GameMain.Server.ServerSettings.StartWhenClientsReady;
                if (args.Length > 0)
                {
                    bool.TryParse(args[0], out enabled);
                }
                else
                {
                    enabled = !enabled;
                }
                if (enabled != GameMain.Server.ServerSettings.StartWhenClientsReady)
                {
                    GameMain.Server.ServerSettings.StartWhenClientsReady = enabled;
                    GameMain.NetLobbyScreen.LastUpdateID++;
                }
                NewMessage(GameMain.Server.ServerSettings.StartWhenClientsReady ? "Enabled starting the round automatically when clients are ready." : "Disabled starting the round automatically when clients are ready.", Color.White);
            });

            AssignOnExecute("giveperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("giveperm [id]: Grants administrative permissions to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                NewMessage("Valid permissions are:", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(), Color.White);
                }
                ShowQuestionPrompt("Permission to grant to \"" + client.Name + "\"?", (perm) =>
                {
                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        NewMessage(perm + " is not a valid permission!", Color.Red);
                        return;
                    }
                    client.GivePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Granted " + perm + " permissions to " + client.Name + ".", Color.White);
                });
            });

            AssignOnExecute("revokeperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("revokeperm [id]: Revokes administrative permissions to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                NewMessage("Valid permissions are:", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    NewMessage(" - " + permission.ToString(), Color.White);
                }
                ShowQuestionPrompt("Permission to revoke from \"" + client.Name + "\"?", (perm) =>
                {
                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        NewMessage(perm + " is not a valid permission!", Color.Red);
                        return;
                    }
                    client.RemovePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Revoked " + perm + " permissions from " + client.Name + ".", Color.White);
                });
            });

            AssignOnExecute("giverank", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("giverank [id]: Assigns a specific rank(= a set of administrative permissions) to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                NewMessage("Valid ranks are:", Color.White);
                foreach (PermissionPreset permissionPreset in PermissionPreset.List)
                {
                    NewMessage(" - " + permissionPreset.Name, Color.White);
                }

                ShowQuestionPrompt("Rank to grant to \"" + client.Name + "\"?", (rank) =>
                {
                    PermissionPreset preset = PermissionPreset.List.Find(p => p.Name.ToLowerInvariant() == rank.ToLowerInvariant());
                    if (preset == null)
                    {
                        ThrowError("Rank \"" + rank + "\" not found.");
                        return;
                    }

                    client.SetPermissions(preset.Permissions, preset.PermittedCommands);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Assigned the rank \"" + preset.Name + "\" to " + client.Name + ".", Color.White);
                });
            });

            AssignOnExecute("givecommandperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("givecommandperm [id]: Gives the player with the specified client ID the permission to use the specified console commands.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Console command permissions to grant to \"" + client.Name + "\"? You may enter multiple commands separated with a space.", (commandsStr) =>
                {
                    string[] splitCommands = commandsStr.Split(' ');
                    List<Command> grantedCommands = new List<Command>();
                    for (int i = 0; i < splitCommands.Length; i++)
                    {
                        splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                        Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                        if (matchingCommand == null)
                        {
                            ThrowError("Could not find the command \"" + splitCommands[i] + "\"!");
                        }
                        else
                        {
                            grantedCommands.Add(matchingCommand);
                        }
                    }

                    client.GivePermission(ClientPermissions.ConsoleCommands);
                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Union(grantedCommands).Distinct().ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Gave the client \"" + client.Name + "\" the permission to use console commands " + string.Join(", ", grantedCommands.Select(c => c.names[0])) + ".", Color.White);
                });
            });

            AssignOnExecute("revokecommandperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("revokecommandperm [id]: Revokes permission to use the specified console commands from the player with the specified client ID.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Console command permissions to revoke from \"" + client.Name + "\"? You may enter multiple commands separated with a space.", (commandsStr) =>
                {
                    string[] splitCommands = commandsStr.Split(' ');
                    List<Command> revokedCommands = new List<Command>();
                    for (int i = 0; i < splitCommands.Length; i++)
                    {
                        splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                        Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                        if (matchingCommand == null)
                        {
                            ThrowError("Could not find the command \"" + splitCommands[i] + "\"!");
                        }
                        else
                        {
                            revokedCommands.Add(matchingCommand);
                        }
                    }

                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Except(revokedCommands).ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", Color.White);
                });
            });

            AssignOnExecute("showperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("showperm [id]: Shows the current administrative permissions of the client with the specified client ID.", Color.Cyan);
                    return;
                }

                int.TryParse(args[0], out int id);
                var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                if (client == null)
                {
                    ThrowError("Client id \"" + id + "\" not found.");
                    return;
                }

                if (client.Permissions == ClientPermissions.None)
                {
                    NewMessage(client.Name + " has no special permissions.", Color.White);
                    return;
                }

                NewMessage(client.Name + " has the following permissions:", Color.White);
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    if (permission == ClientPermissions.None || !client.HasPermission(permission)) { continue; }
                    NewMessage("   - " + TextManager.Get("ClientPermission." + permission), Color.White);
                }
                if (client.HasPermission(ClientPermissions.ConsoleCommands))
                {
                    if (client.PermittedConsoleCommands.Count == 0)
                    {
                        NewMessage("No permitted console commands:", Color.White);
                    }
                    else
                    {
                        NewMessage("Permitted console commands:", Color.White);
                        foreach (Command permittedCommand in client.PermittedConsoleCommands)
                        {
                            NewMessage("   - " + permittedCommand.names[0], Color.White);
                        }
                    }
                }
            });

            AssignOnExecute("togglekarma", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                GameMain.Server.ServerSettings.KarmaEnabled = !GameMain.Server.ServerSettings.KarmaEnabled;
                NewMessage(GameMain.Server.ServerSettings.KarmaEnabled ? "Karma system enabled." : "Karma system disabled.", Color.LightGreen);
            });

            AssignOnExecute("resetkarma", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                client.Karma = 100.0f;
                NewMessage("Set the karma of the client \"" + args[0] + "\" to 100.", Color.LightGreen);
            });
            AssignOnClientRequestExecute("resetkarma", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                var targetClient = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (targetClient == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                targetClient.Karma = 100.0f;
                GameMain.Server.SendDirectChatMessage("Set the karma of the client \"" + args[0] + "\" to 100.", client);
                NewMessage("Client \"" + client.Name + "\" set the karma of \"" + args[0] + "\" to 100.", Color.LightGreen);
            });

            AssignOnExecute("setkarma", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length < 2) return;
                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                if (!float.TryParse(args[1], out float karmaValue) || karmaValue < 0.0f || karmaValue > 100.0f)
                {
                    ThrowError("\"" + args[1] + "\" is not a valid karma value. You need to enter a number between 0-100.");
                    return;
                }
                client.Karma = karmaValue;
                NewMessage("Set the karma of the client \"" + args[0] + "\" to " + karmaValue + ".", Color.LightGreen);
            });
            AssignOnClientRequestExecute("setkarma", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                if (GameMain.Server == null || args.Length < 2) return;
                var targetClient = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (targetClient == null)
                {
                    GameMain.Server.SendDirectChatMessage("Client \"" + args[0] + "\" not found.", client);
                    return;
                }
                if (!float.TryParse(args[1], out float karmaValue) || karmaValue < 0.0f || karmaValue > 100.0f)
                {
                    GameMain.Server.SendDirectChatMessage("\"" + args[1] + "\" is not a valid karma value. You need to enter a number between 0-100.", client);
                    return;
                }
                targetClient.Karma = karmaValue;
                GameMain.Server.SendDirectChatMessage("Set the karma of the client \"" + args[0] + "\" to " + karmaValue + ".", client);
                NewMessage("Client \"" + client.Name + "\" set the karma of \"" + args[0] + "\" to " + karmaValue + ".", Color.LightGreen);
            });

            AssignOnExecute("showkarma", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                NewMessage("***************", Color.Cyan);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    NewMessage("- " + c.ID.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Karma, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            });
            AssignOnClientRequestExecute("showkarma", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.ID.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Karma, client);
                }
                GameMain.Server.SendConsoleMessage("***************", client);
            });
            AssignOnExecute("togglekarmatestmode|karmatestmode", (string[] args) =>
            {
                if (GameMain.Server?.KarmaManager == null) return;
                GameMain.Server.KarmaManager.TestMode = !GameMain.Server.KarmaManager.TestMode;
                NewMessage(GameMain.Server.KarmaManager.TestMode ? "Karma test mode enabled." : "Karma test mode disabled.", Color.LightGreen);
            });

            AssignOnExecute("banendpoint", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;

                ShowQuestionPrompt("Reason for banning the endpoint \"" + args[0] + "\"?", (reason) =>
                {
                    ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\")", (duration) =>
                    {
                        TimeSpan? banDuration = null;
                        if (!string.IsNullOrWhiteSpace(duration))
                        {
                            if (!TryParseTimeSpan(duration, out TimeSpan parsedBanDuration))
                            {
                                ThrowError("\"" + duration + "\" is not a valid ban duration. Use the format \"[days] d [hours] h\", \"[days] d\" or \"[hours] h\".");
                                return;
                            }
                            banDuration = parsedBanDuration;
                        }

                        var clients = GameMain.Server.ConnectedClients.FindAll(c => c.EndpointMatches(args[0]));
                        if (clients.Count == 0)
                        {
                            GameMain.Server.ServerSettings.BanList.BanPlayer("Unnamed", args[0], reason, banDuration);
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
            });

            commands.Add(new Command("mute", "mute [name]: Prevent the client from speaking through the voice chat.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                client.Muted = true;
                GameMain.Server.SendDirectChatMessage(TextManager.Get("MutedByServer"), client, ChatMessageType.MessageBox);
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));
            commands.Add(new Command("unmute", "unmute [name]: Allow the client to speak through the voice chat.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                client.Muted = false;
                GameMain.Server.SendDirectChatMessage(TextManager.Get("UnmutedByServer"), client, ChatMessageType.MessageBox);
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ConnectedClients.Select(c => c.Name).ToArray()
                };
            }));

            AssignOnExecute("netstats", (string[] args) =>
            {
                //TODO: reimplement
                if (GameMain.Server == null) return;
                GameMain.Server.ShowNetStats = !GameMain.Server.ShowNetStats;
            });

            AssignOnExecute("setclientcharacter", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                
                if (args.Length < 2)
                {
                    ThrowError("Invalid parameters. The command should be formatted as \"setclientcharacter [client] [character]\". If the names consist of multiple words, you should surround them with quotation marks.");
                    return;
                }
                
                var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                }

                var character = FindMatchingCharacter(args.Skip(1).ToArray(), false);
                GameMain.Server.SetClientCharacter(client, character);
            });

            AssignOnExecute("difficulty|leveldifficulty", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length < 1) return;

                if (float.TryParse(args[0], out float difficulty))
                {
                    NewMessage("Set level difficulty setting to " + MathHelper.Clamp(difficulty, 0.0f, 100.0f), Color.White);
                    GameMain.NetLobbyScreen.SetLevelDifficulty(difficulty);
                }
                else
                {
                    NewMessage(args[0] + " is not a valid difficulty setting (enter a value between 0-100)", Color.Red);
                }
            });

            commands.Add(new Command("clientlist", "clientlist: List all the clients connected to the server.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                NewMessage("***************", Color.Cyan);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    NewMessage("- " + c.ID.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Connection.EndPointString, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));
            AssignOnClientRequestExecute("clientlist", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.ID.ToString() + ": " + c.Name + ", " + c.Connection.EndPointString, client);
                }
                GameMain.Server.SendConsoleMessage("***************", client);
            });

            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Enabled cheat commands.", Color.Red);
                if (Steam.SteamManager.USE_STEAM)
                {
                    NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
                    GameMain.Server?.UpdateCheatsEnabled();
                }
                else
                {
                    GameMain.Server?.UpdateCheatsEnabled();
                }
            }));
            AssignOnClientRequestExecute("enablecheats", (client, cursorPos, args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Cheat commands have been enabled by \"" + client.Name + "\".", Color.Red);
                if (Steam.SteamManager.USE_STEAM)
                {
                    NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
                    GameMain.Server?.UpdateCheatsEnabled();
                }
                else
                {
                    GameMain.Server?.UpdateCheatsEnabled();
                }
            });

            commands.Add(new Command("traitorlist", "traitorlist: List all the traitors and their targets.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null || traitorManager.Traitors == null)
                {
                    NewMessage("There are no traitors at the moment.", Color.Cyan);
                    return;
                }
                foreach (Traitor t in traitorManager.Traitors)
                {
                    if (t.CurrentObjective != null)
                    {
                        NewMessage(string.Format("- Traitor {0}'s current goals are:\n{1}", t.Character.Name, t.CurrentObjective.GoalInfos), Color.Cyan);
                    }
                    else
                    {
                        NewMessage(string.Format("- Traitor {0} has no current objective.", t.Character.Name), Color.Cyan);
                    }
                }
                NewMessage("The code words are: " + traitorManager.CodeWords + ", response: " + traitorManager.CodeResponse + ".", Color.Cyan);
            }));
            AssignOnClientRequestExecute("traitorlist", (Client client, Vector2 cursorPos, string[] args) =>
            {
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null || traitorManager.Traitors == null)
                {
                    GameMain.Server.SendTraitorMessage(client,"There are no traitors at the moment.", TraitorMessageType.Console);
                    return;
                }
                foreach (Traitor t in traitorManager.Traitors)
                {
                    if (t.CurrentObjective != null)
                    {
                        var traitorGoals = TextManager.FormatServerMessage(t.CurrentObjective.GoalInfos);
                        var traitorGoalsStart = traitorGoals.LastIndexOf('/') + 1;
                        GameMain.Server.SendTraitorMessage(client, string.Join("/", new[] {
                            traitorGoals.Substring(0, traitorGoalsStart),
                            $"[traitorgoals]={traitorGoals.Substring(traitorGoalsStart)}",
                            $"[traitorname]={t.Character.Name}",
                            "Traitor [traitorname]'s current goals are:\n[traitorgoals]"
                            }.Where(s => !string.IsNullOrEmpty(s))), TraitorMessageType.Console);
                    }
                    else
                    {
                        GameMain.Server.SendTraitorMessage(client, string.Format("- Traitor {0} has no current objective.", t.Character.Name), TraitorMessageType.Console);
                    }
                }
                GameMain.Server.SendTraitorMessage(client, "The code words are: " + traitorManager.CodeWords + ", response: " + traitorManager.CodeResponse + ".", TraitorMessageType.Console);
            });

            commands.Add(new Command("setpassword|setserverpassword|password", "setpassword [password]: Changes the password of the server that's being hosted.", (string[] args) =>
            {
                if (GameMain.Server == null) { return; }
                GameMain.Server.ServerSettings.SetPassword(args.Length > 0 ? args[0] : "");
                NewMessage(GameMain.Server.ServerSettings.HasPassword ? "Changed the server password." : "Removed password protection from the server.");
            }));
            AssignOnClientRequestExecute("setpassword", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (GameMain.Server == null) { return; }
                GameMain.Server.ServerSettings.SetPassword(args.Length > 0 ? args[0] : "");
                NewMessage(client.Name + " " + (GameMain.Server.ServerSettings.HasPassword ? " changed the server password to \"" + args[0] + "\"." : " removed password protection from the server."));
                GameMain.Server.SendConsoleMessage(GameMain.Server.ServerSettings.HasPassword ? "Changed the server password." : "Removed password protection from the server.", client);
            });

            commands.Add(new Command("setmaxplayers|maxplayers", "setmaxplayers [max players]: Sets the maximum player count of the server that's being hosted.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                if (!int.TryParse(args[0], out int maxPlayers))
                {
                    NewMessage(args[0] + " is not a valid player count.");
                }
                else
                {
                    GameMain.Server.ServerSettings.MaxPlayers = maxPlayers;
                    NewMessage("Set the maximum player count to " + maxPlayers + ".");
                }
            }));
            AssignOnClientRequestExecute("setmaxplayers", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                if (!int.TryParse(args[0], out int maxPlayers))
                {
                    GameMain.Server.SendConsoleMessage(args[0] + " is not a valid player count.", client);
                }
                else
                {
                    GameMain.Server.ServerSettings.MaxPlayers = maxPlayers;
                    NewMessage(client.Name + " set the maximum player count to " + maxPlayers + ".");
                    GameMain.Server.SendConsoleMessage("Set the maximum player count to " + maxPlayers + ".", client);
                }
            });

            commands.Add(new Command("restart|reset", "restart/reset: Close and restart the server.", (string[] args) =>
            {
                NewMessage("*****************", Color.Lime);
                NewMessage("RESTARTING SERVER", Color.Lime);
                NewMessage("*****************", Color.Lime);
                GameServer.Log("Console command \"restart\" executed: closing the server...", ServerLog.MessageType.ServerMessage);
                GameMain.Instance.CloseServer();
                GameMain.Instance.StartServer();
            }));

            commands.Add(new Command("exit|quit|close", "exit/quit/close: Exit the application.", (string[] args) =>
            {
                GameMain.ShouldRun = false;
            }));

            commands.Add(new Command("say", "say [message]: Send a global chat message. When issued through the server command line, displays \"HOST\" as the sender.", (string[] args) =>
            {
                string text = string.Join(" ", args);
                text = "HOST: " + text;
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            }));
            AssignOnClientRequestExecute("say",
            (Client client, Vector2 cursorPos, string[] args) =>
            {
                string text = string.Join(" ", args);
                text = client.Name+": " + text;
                if (GameMain.Server.OwnerConnection != null &&
                    client.Connection == GameMain.Server.OwnerConnection)
                {
                    text = "[HOST] " + text;
                }
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            });

            commands.Add(new Command("msg", "msg [message]: Send a chat message with no sender specified.", (string[] args) =>
            {
                string text = string.Join(" ", args);
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            }));
            AssignOnClientRequestExecute("msg",
            (Client client, Vector2 cursorPos, string[] args) =>
            {
                string text = string.Join(" ", args);
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            });

            commands.Add(new Command("servername", "servername [name]: Change the name of the server.", (string[] args) =>
            {
                GameMain.Server.Name = string.Join(" ", args);
                GameMain.NetLobbyScreen.ChangeServerName(string.Join(" ", args));
            }));

            commands.Add(new Command("servermsg", "servermsg [message]: Change the message displayed in the server lobby.", (string[] args) =>
            {
                GameMain.NetLobbyScreen.ChangeServerMessage(string.Join(" ", args));
            }));

            commands.Add(new Command("seed|levelseed", "seed/levelseed: Changes the level seed for the next round.", (string[] args) =>
            {
                GameMain.NetLobbyScreen.LevelSeed = string.Join(" ", args);
            }));

            commands.Add(new Command("randomizeseed", "randomizeseed: Toggles level seed randomization on/off.", (string[] args) =>
            {
                GameMain.Server.ServerSettings.RandomizeSeed = !GameMain.Server.ServerSettings.RandomizeSeed;
                NewMessage((GameMain.Server.ServerSettings.RandomizeSeed ? "Enabled" : "Disabled") + " level seed randomization.", Color.Cyan);
            }));

            commands.Add(new Command("gamemode", "gamemode [name]/[index]: Select the game mode for the next round. The parameter can either be the name or the index number of the game mode (0 = sandbox, 1 = mission, etc).", (string[] args) =>
            {
                int index = -1;
                if (string.Join("", args).Trim().Length > 0)
                {
                    if (int.TryParse(string.Join(" ", args), out index))
                    {
                        if (index > 0 && index < GameMain.NetLobbyScreen.GameModes.Length &&
                            GameMain.NetLobbyScreen.GameModes[index].Identifier == "multiplayercampaign")
                        {
                            MultiPlayerCampaign.StartCampaignSetup();
                        }
                        else
                        {
                            GameMain.NetLobbyScreen.SelectedModeIndex = index;
                            NewMessage("Set gamemode to " + GameMain.NetLobbyScreen.GameModes[GameMain.NetLobbyScreen.SelectedModeIndex].Name, Color.Cyan);
                        }
                    }
                    else
                    {
                        string modeName = string.Join(" ", args);
                        if (modeName.ToLowerInvariant() == "campaign")
                        {
                            MultiPlayerCampaign.StartCampaignSetup();
                        }
                        else
                        {
                            var gameMode = GameModePreset.List.Find(gm => gm.Name.ToLower() == modeName.ToLower());
                            if (gameMode == null)
                            {
                                ThrowError("Game mode \"" + modeName + "\" not found!");
                                return;
                            }
                            GameMain.NetLobbyScreen.SelectedModeIdentifier = gameMode.Identifier;
                            NewMessage("Set gamemode to " + gameMode.Name, Color.Cyan);
                        }
                    }
                }
                else
                {
                    NewMessage("Current gamemode is " + GameMain.NetLobbyScreen.GameModes[GameMain.NetLobbyScreen.SelectedModeIndex].Name, Color.Cyan);
                }
            },
            () =>
            {
                return new string[][]
                {
                    GameModePreset.List.Select(gm => gm.Name).ToArray()
                };
            }));

            commands.Add(new Command("mission", "mission [name]/[index]: Select the mission type for the next round. The parameter can either be the name or the index number of the mission type (0 = first mission type, 1 = second mission type, etc).", (string[] args) =>
            {
                int index = -1;
                if (int.TryParse(string.Join(" ", args), out index))
                {
                    GameMain.NetLobbyScreen.MissionTypeIndex = index;
                }
                else
                {
                    GameMain.NetLobbyScreen.MissionTypeName = string.Join(" ", args);
                }
                NewMessage("Set mission to " + GameMain.NetLobbyScreen.MissionTypeName, Color.Cyan);
            },
            () =>
            {
                return new string[][]
                {
                    Enum.GetNames(typeof(MissionType))
                };
            }));

            commands.Add(new Command("sub|submarine", "submarine [name]: Select the submarine for the next round.", (string[] args) =>
            {
                Submarine sub = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.ToLower() == string.Join(" ", args).ToLower());

                if (sub != null)
                {
                    GameMain.NetLobbyScreen.SelectedSub = sub;
                }
                sub = GameMain.NetLobbyScreen.SelectedSub;
                NewMessage("Selected sub: " + sub.Name + (sub.HasTag(SubmarineTag.Shuttle) ? " (shuttle)" : ""), Color.Cyan);
            },
            () =>
            {
                return new string[][]
                {
                    Submarine.Loaded.Select(s => s.Name).ToArray()
                };
            }));

            commands.Add(new Command("shuttle", "shuttle [name]: Select the specified submarine as the respawn shuttle for the next round.", (string[] args) =>
            {
                Submarine shuttle = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.ToLower() == string.Join(" ", args).ToLower());

                if (shuttle != null)
                {
                    GameMain.NetLobbyScreen.SelectedShuttle = shuttle;
                }
                shuttle = GameMain.NetLobbyScreen.SelectedShuttle;
                NewMessage("Selected shuttle: " + shuttle.Name + (shuttle.HasTag(SubmarineTag.Shuttle) ? "" : " (not shuttle)"), Color.Cyan);
            },
            () =>
            {
                return new string[][]
                {
                    Submarine.Loaded.Select(s => s.Name).ToArray()
                };
            }));


            AssignOnExecute("respawnnow", (string[] args) =>
            {
                if (GameMain.Server?.RespawnManager == null) { return; }
                if (GameMain.Server.RespawnManager.CurrentState != RespawnManager.State.Transporting)
                {
                    GameMain.Server.RespawnManager.ForceRespawn();
                }
            });

            commands.Add(new Command("startgame|startround|start", "start/startgame/startround: Start a new round.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.GameScreen) return;
                if (!GameMain.Server.StartGame()) NewMessage("Failed to start a new round", Color.Yellow);
            }));

            commands.Add(new Command("endgame|endround|end", "end/endgame/endround: End the current round.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.NetLobbyScreen) return;
                GameMain.Server.EndGame();
            }));
            
            commands.Add(new Command("entitydata", "", (string[] args) =>
            {
                if (args.Length == 0) return;
                Entity ent = Entity.FindEntityByID(Convert.ToUInt16(args[0]));
                if (ent != null)
                {
                    NewMessage(ent.ToString(), Color.Lime);
                }
            }));

#if DEBUG
            commands.Add(new Command("printsendertransfers", "", (string[] args) =>
            {
                GameMain.Server.PrintSenderTransters();
            }));

            commands.Add(new Command("eventdata", "", (string[] args) =>
            {
                if (args.Length == 0) return;
                ServerEntityEvent ev = GameMain.Server.EntityEventManager.Events[Convert.ToUInt16(args[0])];
                if (ev != null)
                {
                    NewMessage(ev.StackTrace, Color.Lime);
                }
            }));

            commands.Add(new Command("spamchatmessages", "", (string[] args) =>
            {
                int msgCount = 1000;
                if (args.Length > 0) int.TryParse(args[0], out msgCount);
                int msgLength = 50;
                if (args.Length > 1) int.TryParse(args[1], out msgLength);

                for (int i = 0; i < msgCount; i++)
                {
                    GameMain.Server.SendChatMessage(ToolBox.RandomSeed(msgLength), ChatMessageType.Default);
                }
            }));
#endif

            AssignOnClientRequestExecute(
                "spawn|spawncharacter",
                (Client client, Vector2 cursorPos, string[] args) =>
                {
                    SpawnCharacter(args, cursorPos, out string errorMsg);
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        ThrowError(errorMsg);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "banendpoint|banip",
                (Client client, Vector2 cursorPos, string[] args) =>
                {
                    if (args.Length < 1) return;
                    var clients = GameMain.Server.ConnectedClients.FindAll(c => c.EndpointMatches(args[0]));
                    TimeSpan? duration = null;
                    if (args.Length > 1)
                    {
                        if (double.TryParse(args[1], out double durationSeconds))
                        {
                            if (durationSeconds > 0) duration = TimeSpan.FromSeconds(durationSeconds);
                        }
                        else
                        {
                            GameMain.Server.SendConsoleMessage("\"" + args[1] + "\" is not a valid ban duration.", client);
                            return;
                        }
                    }
                    string reason = "";
                    if (args.Length > 2) reason = string.Join(" ", args.Skip(2));

                    if (clients.Count == 0)
                    {
                        GameMain.Server.ServerSettings.BanList.BanPlayer("Unnamed", args[0], reason, duration);
                    }
                    else
                    {
                        foreach (Client cl in clients)
                        {
                            GameMain.Server.BanClient(cl, reason, false, duration);
                        }
                    }
                }
            );

            commands.Add(new Command("unban", "unban [name]: Unban a specific client.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                string clientName = string.Join(" ", args);
                GameMain.Server.UnbanPlayer(clientName, "");
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ServerSettings.BanList.BannedNames.Where(name => !string.IsNullOrEmpty(name)).ToArray()
                };
            }));

            commands.Add(new Command("unbanip", "unbanip [ip]: Unban a specific IP.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                GameMain.Server.UnbanPlayer("", args[0]);
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ServerSettings.BanList.BannedIPs.Where(ip => !string.IsNullOrEmpty(ip)).ToArray()
                };
            }));

            AssignOnClientRequestExecute(
                "eventmanager",
                (Client client, Vector2 cursorPos, string[] args) =>
                {
                    if (GameMain.GameSession?.EventManager != null)
                    {
                        GameMain.GameSession.EventManager.Enabled = !GameMain.GameSession.EventManager.Enabled;
                        NewMessage(GameMain.GameSession.EventManager.Enabled ? "Event manager on" : "Event manager off", Color.White);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "spawnitem",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    SpawnItem(args, cursorWorldPos, client.Character, out string errorMsg);
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        GameMain.Server.SendConsoleMessage(errorMsg, client);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "disablecrewai",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    HumanAIController.DisableCrewAI = true;
                    NewMessage("Crew AI disabled by \"" + client.Name + "\"", Color.White);
                    GameMain.Server.SendConsoleMessage("Crew AI disabled", client);
                }
            );

            AssignOnClientRequestExecute(
                "enablecrewai",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    HumanAIController.DisableCrewAI = false;
                    NewMessage("Crew AI enabled by \"" + client.Name + "\"", Color.White);
                    GameMain.Server.SendConsoleMessage("Crew AI enabled", client);
                }
            );

            AssignOnClientRequestExecute(
                "botcount",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 1 || GameMain.Server == null) return;
                    int botCount = GameMain.Server.ServerSettings.BotCount;
                    int.TryParse(args[0], out botCount);
                    GameMain.NetLobbyScreen.SetBotCount(botCount);
                    NewMessage("\"" + client.Name + "\" set the number of bots to " + botCount, Color.White);
                    GameMain.Server.SendConsoleMessage("Set the number of bots to " + botCount, client);
                }
            );

            AssignOnClientRequestExecute(
                "botspawnmode",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 1 || GameMain.Server == null) return;
                    if (Enum.TryParse(args[0], true, out BotSpawnMode spawnMode))
                    {
                        GameMain.NetLobbyScreen.SetBotSpawnMode(spawnMode);
                        NewMessage("\"" + client.Name + "\" set bot spawn mode to " + spawnMode, Color.White);
                        GameMain.Server.SendConsoleMessage("Set bot spawn mode to " + spawnMode, client);
                    }
                    else
                    {
                        GameMain.Server.SendConsoleMessage("\"" + args[0] + "\" is not a valid bot spawn mode. (Valid modes are Fill and Normal)", client);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "teleportcharacter|teleport",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character tpCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args, false);
                    if (tpCharacter == null) return;

                    var cam = GameMain.GameScreen.Cam;
                    tpCharacter.AnimController.CurrentHull = null;
                    tpCharacter.Submarine = null;
                    tpCharacter.AnimController.SetPosition(ConvertUnits.ToSimUnits(cursorWorldPos));
                    tpCharacter.AnimController.FindHull(cursorWorldPos, true);
                }
            );

            AssignOnClientRequestExecute(
                "godmode",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (Submarine.MainSub == null) return;

                    Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                    NewMessage((Submarine.MainSub.GodMode ? "Godmode turned on by \"" : "Godmode off by \"") + client.Name + "\"", Color.White);
                    GameMain.Server.SendConsoleMessage(Submarine.MainSub.GodMode ? "Godmode on" : "Godmode off", client);
                }
            );

            AssignOnClientRequestExecute(
                "giveaffliction",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.Find(a => a.Name.ToLowerInvariant() == args[0].ToLowerInvariant());
                    if (afflictionPrefab == null)
                    {
                        GameMain.Server.SendConsoleMessage("Affliction \"" + args[0] + "\" not found.", client);
                        return;
                    }

                    if (!float.TryParse(args[1], out float afflictionStrength))
                    {
                        GameMain.Server.SendConsoleMessage("\"" + args[1] + "\" is not a valid affliction strength.", client);
                        return;
                    }

                    Character targetCharacter = (args.Length <= 2) ? client.Character : FindMatchingCharacter(args.Skip(2).ToArray());
                    if (targetCharacter != null)
                    {
                        targetCharacter.CharacterHealth.ApplyAffliction(targetCharacter.AnimController.MainLimb, afflictionPrefab.Instantiate(afflictionStrength));
                    }
                }
            );

            AssignOnClientRequestExecute(
                "heal",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character healedCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args);
                    if (healedCharacter != null)
                    {
                        healedCharacter.SetAllDamage(0.0f, 0.0f, 0.0f);
                        healedCharacter.Oxygen = 100.0f;
                        healedCharacter.Bloodloss = 0.0f;
                        healedCharacter.SetStun(0.0f, true);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "revive",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character revivedCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args);
                    if (revivedCharacter == null) return;

                    revivedCharacter.Revive();
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
            );

            AssignOnClientRequestExecute(
                "freeze",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (client.Character != null) client.Character.AnimController.Frozen = !client.Character.AnimController.Frozen;
                }
            );

            AssignOnClientRequestExecute(
                "ragdoll",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character ragdolledCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args);
                    if (ragdolledCharacter != null)
                    {
                        ragdolledCharacter.IsForceRagdolled = !ragdolledCharacter.IsForceRagdolled;
                    }
                }
            );

            AssignOnClientRequestExecute(
                "explosion",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Vector2 explosionPos = cursorWorldPos;
                    float range = 500, force = 10, damage = 50, structureDamage = 10, empStrength = 0.0f; ;
                    if (args.Length > 0) float.TryParse(args[0], out range);
                    if (args.Length > 1) float.TryParse(args[1], out force);
                    if (args.Length > 2) float.TryParse(args[2], out damage);
                    if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                    if (args.Length > 4) float.TryParse(args[4], out empStrength);
                    new Explosion(range, force, damage, structureDamage, empStrength).Explode(explosionPos, null);
                }
            );

            AssignOnClientRequestExecute(
                "kill",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character killedCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args);
                    killedCharacter?.SetAllDamage(200.0f, 0.0f, 0.0f);          
                }
            );

            AssignOnClientRequestExecute(
                "control",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 1) return;
                    var character = FindMatchingCharacter(args, ignoreRemotePlayers: true, allowedRemotePlayer: client);
                    if (character != null)
                    {
                        GameMain.Server.SetClientCharacter(client, character);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "difficulty|leveldifficulty",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (GameMain.Server == null || args.Length < 1) return;

                    if (float.TryParse(args[0], out float difficulty))
                    {
                        GameMain.Server.SendConsoleMessage("Set level difficulty setting to " + MathHelper.Clamp(difficulty, 0.0f, 100.0f), client);
                        NewMessage("Client \"" + client.Name + "\" set level difficulty setting to " + MathHelper.Clamp(difficulty, 0.0f, 100.0f), Color.White);
                        GameMain.NetLobbyScreen.SetLevelDifficulty(difficulty);
                    }
                    else
                    {
                        GameMain.Server.SendConsoleMessage(args[0] + " is not a valid difficulty setting (enter a value between 0-100)", client);
                        NewMessage(args[0] + " is not a valid difficulty setting (enter a value between 0-100)", Color.Red);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "giveperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    string perm = string.Join("", args.Skip(1));

                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient);
                        return;
                    }
                    client.GivePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Granted " + perm + " permissions to " + client.Name + ".", senderClient);
                    NewMessage(senderClient.Name + " granted " + perm + " permissions to " + client.Name + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "revokeperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    string perm = string.Join("", args.Skip(1));

                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient);
                        return;
                    }
                    client.RemovePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Revoked " + perm + " permissions from " + client.Name + ".", senderClient);
                    NewMessage(senderClient.Name + " revoked " + perm + " permissions from " + client.Name + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "giverank",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    string rank = string.Join("", args.Skip(1));
                    PermissionPreset preset = PermissionPreset.List.Find(p => p.Name.ToLowerInvariant() == rank.ToLowerInvariant());
                    if (preset == null)
                    {
                        GameMain.Server.SendConsoleMessage("Rank \"" + rank + "\" not found.", senderClient);
                        return;
                    }

                    client.SetPermissions(preset.Permissions, preset.PermittedCommands);
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Assigned the rank \"" + preset.Name + "\" to " + client.Name + ".", senderClient);
                    NewMessage(senderClient.Name + " granted  the rank \"" + preset.Name + "\" to " + client.Name + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "givecommandperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    string[] splitCommands = args.Skip(1).ToArray();
                    List<Command> grantedCommands = new List<Command>();
                    for (int i = 0; i < splitCommands.Length; i++)
                    {
                        splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                        Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                        if (matchingCommand == null)
                        {
                            GameMain.Server.SendConsoleMessage("Could not find the command \"" + splitCommands[i] + "\"!", senderClient);
                        }
                        else
                        {
                            grantedCommands.Add(matchingCommand);
                        }
                    }

                    client.GivePermission(ClientPermissions.ConsoleCommands);
                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Union(grantedCommands).Distinct().ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Gave the client \"" + client.Name + "\" the permission to use the console commands " + string.Join(", ", grantedCommands.Select(c => c.names[0])) + ".", senderClient);
                    NewMessage("Gave the client \"" + client.Name + "\" the permission to use the console commands " + string.Join(", ", grantedCommands.Select(c => c.names[0])) + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "revokecommandperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    string[] splitCommands = args.Skip(1).ToArray();
                    List<Command> revokedCommands = new List<Command>();
                    for (int i = 0; i < splitCommands.Length; i++)
                    {
                        splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                        Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                        if (matchingCommand == null)
                        {
                            GameMain.Server.SendConsoleMessage("Could not find the command \"" + splitCommands[i] + "\"!", senderClient);
                        }
                        else
                        {
                            revokedCommands.Add(matchingCommand);
                        }
                    }

                    client.GivePermission(ClientPermissions.ConsoleCommands);
                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Except(revokedCommands).ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", senderClient);
                    NewMessage(senderClient.Name + " revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "showperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 1)
                    {
                        GameMain.Server.SendConsoleMessage("showperm [id]: Shows the current administrative permissions of the client with the specified client ID.", senderClient);
                        return;
                    }

                    int.TryParse(args[0], out int id);
                    var client = GameMain.Server.ConnectedClients.Find(c => c.ID == id);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client id \"" + id + "\" not found.", senderClient);
                        return;
                    }

                    if (client.Permissions == ClientPermissions.None)
                    {
                        GameMain.Server.SendConsoleMessage(client.Name + " has no special permissions.", senderClient);
                        return;
                    }

                    GameMain.Server.SendConsoleMessage(client.Name + " has the following permissions:", senderClient);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        if (permission == ClientPermissions.None || !client.HasPermission(permission)) { continue; }
                        GameMain.Server.SendConsoleMessage("   - " + TextManager.Get("ClientPermission." + permission), senderClient);
                    }
                    if (client.HasPermission(ClientPermissions.ConsoleCommands))
                    {
                        if (client.PermittedConsoleCommands.Count == 0)
                        {
                            GameMain.Server.SendConsoleMessage("No permitted console commands:", senderClient);
                        }
                        else
                        {
                            GameMain.Server.SendConsoleMessage("Permitted console commands:", senderClient);
                            foreach (Command permittedCommand in client.PermittedConsoleCommands)
                            {
                                GameMain.Server.SendConsoleMessage("   - " + permittedCommand.names[0], senderClient);
                            }
                        }
                    }
                }
            );

            AssignOnClientRequestExecute(
                "setclientcharacter",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2)
                    {
                        GameMain.Server.SendConsoleMessage("Invalid parameters. The command should be formatted as \"setclientcharacter [client] [character]\". If the names consist of multiple words, you should surround them with quotation marks.", senderClient);
                        return;
                    }

                    if (args.Length < 2)
                    {
                        ThrowError("Invalid parameters. The command should be formatted as \"setclientcharacter [client] [character]\". If the names consist of multiple words, you should surround them with quotation marks.");
                        return;
                    }
                    
                    var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client \"" + args[0] + "\" not found.", senderClient);
                    }

                    var character = FindMatchingCharacter(args.Skip(1).ToArray(), false);
                    GameMain.Server.SetClientCharacter(client, character);
                }
            );

            AssignOnClientRequestExecute(
                "campaigndestination|setcampaigndestination",
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
                }
            );

            commands.Add(new Command("tags|taglist", "tags: list all the tags used in the game", (string[] args) =>
            {
                var tagList = MapEntityPrefab.List.SelectMany(p => p.Tags.Select(t => t)).Distinct();
                foreach (var tag in tagList)
                {
                    NewMessage(tag, Color.Yellow);
                }
            }));

#if DEBUG
            commands.Add(new Command("spamevents", "A debug command that creates a ton of entity events.", (string[] args) =>
            {
                /*foreach (Item item in Item.ItemList)
                {
                    foreach (ItemComponent component in item.Components)
                    {
                        if (component is IServerSerializable)
                        {
                            GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(component) });
                        }
                        var itemContainer = item.GetComponent<ItemContainer>();
                        if (itemContainer != null)
                        {
                            GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.InventoryState, 0 });
                        }

                        GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.Status });
                    }
                }
                foreach (Character c in Character.CharacterList)
                {
                    GameMain.Server.CreateEntityEvent(c, new object[] { NetEntityEvent.Type.Status });
                }*/
                foreach (Hull hull in Hull.hullList)
                {
                    GameMain.Server.CreateEntityEvent(hull);
                }
                foreach (Structure wall in Structure.WallList)
                {
                    GameMain.Server.CreateEntityEvent(wall);
                }                
            }));
#endif
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

            if (!MathUtils.IsValid(cursorWorldPos))
            {
                GameMain.Server.SendConsoleMessage("Could not execute command \"" + command + "\" - invalid cursor position.", client);
                NewMessage(client.Name + " attempted to execute the console command \"" + command + "\" with invalid cursor position.", Color.White);
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

        static partial void ShowHelpMessage(Command command)
        {
            NewMessage(command.names[0], Color.Cyan);
            NewMessage(command.help, Color.Gray);
        }
    }
}
