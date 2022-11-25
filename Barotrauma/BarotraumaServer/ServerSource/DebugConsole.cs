using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Barotrauma.Steam;

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
                    GameMain.Server.SendConsoleMessage("You need to enable cheats using the command \"enablecheats\" before you can use the command \"" + names[0] + "\".", client, Color.Red);

#if USE_STEAM
                    NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                    GameMain.Server.SendConsoleMessage("Enabling cheats will disable Steam achievements during this play session.", client, Color.Red);
#endif

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
        }

        private static string input = "";
        private static int memoryIndex = -1;
        private static List<string> commandMemory = new List<string>();

        public static void UpdateCommandLine(int maxTime)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            int consoleWidth = 0;
            int consoleHeight = 0;

            if(!Console.IsOutputRedirected)
            {
                consoleWidth = Console.WindowWidth;
                if (consoleWidth < 5) consoleWidth = 5;
                consoleHeight = Console.WindowHeight;
                if (consoleHeight < 5) consoleHeight = 5;
            }

            //dequeue messages
            if (queuedMessages.Count > 0)
            {

                if (!Console.IsOutputRedirected)
                {
                    Console.CursorLeft = 0;
                }
                while (queuedMessages.TryDequeue(out var msg))
                {
                    Messages.Add(msg);
                    if (GameSettings.CurrentConfig.SaveDebugConsoleLogs || GameSettings.CurrentConfig.VerboseLogging)
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

                    if(!Console.IsOutputRedirected)
                    {
                        int paddingLen = consoleWidth - (msg.Text.Length % consoleWidth) - 1;
                        msgTxt += new string(' ', paddingLen > 0 ? paddingLen : 0);

                        Console.ForegroundColor = XnaToConsoleColor.Convert(msg.Color);
                    }
                    Console.WriteLine(msgTxt);

                    if (sw.ElapsedMilliseconds >= maxTime) { break; }
                }
                if (!Console.IsOutputRedirected)
                {
                    RewriteInputToCommandLine(input);
                }
            }
            if (Messages.Count > MaxMessages)
            {
                Messages.RemoveRange(0, Messages.Count - MaxMessages);
            }

            // No good way to display input when console output is redirected, and can't read from redirected input using KeyAvailable.
            if(!Console.IsOutputRedirected && !Console.IsInputRedirected)
            {
                //read player input
                bool rewriteInput = false;
                while (Console.KeyAvailable)
                {
                    if (sw.ElapsedMilliseconds >= maxTime)
                    {
                        rewriteInput = false;
                        break;
                    }
                    rewriteInput = true;
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
                            ResetAutoComplete();
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
                }
                if (rewriteInput) { RewriteInputToCommandLine(input); }
            }

            sw.Stop();
        }

        private static void WriteAndResetLine(string txt)
        {
            int consoleWidth = Console.BufferWidth;
            int linesWritten = 0;
            while (true)
            {
                if (txt.Length > consoleWidth)
                {
                    linesWritten++;
                    Console.Write(txt.Substring(0, consoleWidth));
                    txt = txt.Substring(consoleWidth);
                }
                else
                {
                    Console.Write(txt);
                    if (txt.Length == consoleWidth)
                    {
                        Console.Write(' '); Console.CursorLeft--;
                        linesWritten++;
                    }
                    break;
                }
            }
            Console.CursorTop -= linesWritten;
        }

        private static void RewriteInputToCommandLine(string input)
        {
            if (Console.WindowWidth == 0 || Console.WindowHeight == 0) { return; }

            int consoleWidth = Math.Max(Console.BufferWidth, 5);
            //int inputLines = Math.Max((int)Math.Ceiling(input.Length / (float)consoleWidth), 1);
            //int cursorLine = Math.Max((int)Math.Ceiling((input.Length + 1) / (float)consoleWidth), 1);

            try
            {
                string tmpInput = input;
                while (tmpInput.Length >= consoleWidth)
                {
                    tmpInput = tmpInput.Substring(consoleWidth);
                }
                string ln = input.Length > 0 ? AutoComplete(input, 0) : "";
                while (ln.Length >= consoleWidth)
                {
                    ln = ln.Substring(consoleWidth);
                }
                ln += new string(' ', consoleWidth - (ln.Length % consoleWidth));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.CursorLeft = 0;
                WriteAndResetLine(ln);
                Console.ForegroundColor = ConsoleColor.White;
                Console.CursorLeft = 0;
                WriteAndResetLine(tmpInput);
                Console.CursorLeft = input.Length % consoleWidth;
            }
            catch (Exception e)
            {
                string errorMsg = "Failed to write input to command line (window width: " + Console.WindowWidth + ", window height: " + Console.WindowHeight + ")\n"
                    + e.Message + "\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("DebugConsole.RewriteInputToCommandLine", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
            }
        }

        public static void Clear()
        {
            while (queuedMessages.TryDequeue(out var msg))
            {
                Messages.Add(msg);
                if (GameSettings.CurrentConfig.SaveDebugConsoleLogs || GameSettings.CurrentConfig.VerboseLogging)
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
        
        private static Client FindClient(string arg)
        {
            Client client = GameMain.Server.ConnectedClients.Find(c => Homoglyphs.Compare(c.Name, arg));
            if (int.TryParse(arg, out int id))
            {
                client ??= GameMain.Server.ConnectedClients.Find(c => c.SessionId == id);
            }
            if (Address.Parse(arg).TryUnwrap(out var address))
            {
                client ??= GameMain.Server.ConnectedClients.Find(c => c.AddressMatches(address));
            }
            if (AccountId.Parse(arg).TryUnwrap(out var argAccountId))
            {
                client ??= GameMain.Server.ConnectedClients.Find(c => c.AccountId.ValueEquals(argAccountId));
            }
            return client;
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
            AssignOnClientRequestExecute("botcount", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                int botCount = GameMain.Server.ServerSettings.BotCount;
                int.TryParse(args[0], out botCount);
                GameMain.NetLobbyScreen.SetBotCount(botCount);
                NewMessage(client.Name + " set the number of bots to " + botCount, Color.White);
                GameMain.Server.SendConsoleMessage("Set the number of bots to " + botCount, client);
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
            AssignOnClientRequestExecute("botspawnmode", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                if (Enum.TryParse(args[0], true, out BotSpawnMode spawnMode))
                {
                    GameMain.NetLobbyScreen.SetBotSpawnMode(spawnMode);
                    NewMessage(client.Name + " set bot spawn mode to " + spawnMode, Color.White);
                    GameMain.Server.SendConsoleMessage("Set bot spawn mode to " + spawnMode, client);
                }
                else
                {
                    GameMain.Server.SendConsoleMessage("\"" + args[0] + "\" is not a valid bot spawn mode. (Valid modes are Fill and Normal)", client, Color.Red);
                }
            });

            AssignOnExecute("killdisconnectedtimer", (string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                float seconds = GameMain.Server.ServerSettings.KillDisconnectedTime;
                if (float.TryParse(args[0], out seconds))
                {
                    seconds = Math.Max(0, seconds);
                    NewMessage("Set kill disconnected timer to " + ToolBox.SecondsToReadableTime(seconds), Color.White);
                }
                else
                {
                    NewMessage("\"" + args[0] + "\" is not a valid duration.", Color.White);
                }
            });
            AssignOnClientRequestExecute("killdisconnectedtimer", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (args.Length < 1 || GameMain.Server == null) return;
                float seconds = GameMain.Server.ServerSettings.KillDisconnectedTime;
                if (float.TryParse(args[0], out seconds))
                {
                    seconds = Math.Max(0, seconds);
                    GameMain.Server.SendConsoleMessage("Set kill disconnected timer to " + ToolBox.SecondsToReadableTime(seconds).Value, client);
                    NewMessage(client.Name + " set kill disconnected timer to " + ToolBox.SecondsToReadableTime(seconds), Color.White);
                }
                else
                {
                    GameMain.Server.SendConsoleMessage("\"" + args[0] + "\" is not a valid duration.", client);
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

            AssignOnExecute("spawn|spawncharacter", (string[] args) =>
            {
                SpawnCharacter(args, Vector2.Zero, out string errorMsg);
                if (!string.IsNullOrWhiteSpace(errorMsg))
                {
                    ThrowError(errorMsg);
                }
            });

            AssignOnExecute("giveperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("giveperm [id/steamid/endpoint/name]: Grants administrative permissions to the player with the specified client.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
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

                    if (permission == ClientPermissions.None)
                    {
                        NewMessage($"No permissions were given to {client.Name}. Did you mean \"revokeperm {client.Name} All\"?");
                        return;
                    }

                    client.GivePermission(permission);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Granted " + perm + " permissions to " + client.Name + ".", Color.White);
                }, args, 1);
            });

            AssignOnExecute("revokeperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("revokeperm [id/steamid/endpoint/name]: Revokes administrative permissions to the player with the specified client.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                if (client.Connection == GameMain.Server.OwnerConnection)
                {
                    NewMessage("Cannot revoke permissions from the server owner!", Color.Red);
                    return;
                }

                if (args.Length < 2)
                {
                    NewMessage("Valid permissions are:", Color.White);
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        NewMessage(" - " + permission.ToString(), Color.White);
                    }
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
                }, args, 1);
            });

            AssignOnExecute("giverank", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("giverank [id/steamid/endpoint/name] [rank]: Assigns a specific rank (= a set of administrative permissions) to the player with the specified client ID.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                if (client.Connection == GameMain.Server.OwnerConnection)
                {
                    NewMessage("Cannot modify the rank of the server owner!", Color.Red);
                    return;
                }

                NewMessage("Valid ranks are:", Color.White);
                foreach (PermissionPreset permissionPreset in PermissionPreset.List)
                {
                    NewMessage(" - " + permissionPreset.Name, Color.White);
                }

                ShowQuestionPrompt("Rank to grant to \"" + client.Name + "\"?", (rank) =>
                {
                    PermissionPreset preset = PermissionPreset.List.Find(p => p.Name.Equals(rank, StringComparison.OrdinalIgnoreCase));
                    if (preset == null)
                    {
                        ThrowError("Rank \"" + rank + "\" not found.");
                        return;
                    }

                    client.SetPermissions(preset.Permissions, preset.PermittedCommands);
                    GameMain.Server.UpdateClientPermissions(client);
                    NewMessage("Assigned the rank \"" + preset.Name + "\" to " + client.Name + ".", Color.White);
                }, args, 1);
            });

            AssignOnExecute("givecommandperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("givecommandperm [id/steamid/endpoint/name]: Gives the specified client the permission to use the specified console commands.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }

                ShowQuestionPrompt("Console command permissions to grant to \"" + client.Name + "\"? You may enter multiple commands separated with a space, or \"all\" to allow using any console command.", (commandsStr) =>
                {
                    string[] splitCommands = commandsStr.Split(' ');
                    bool giveAll = splitCommands.Length > 0 && splitCommands[0].Equals("all", StringComparison.OrdinalIgnoreCase);

                    List<Command> grantedCommands = new List<Command>();
                    if (giveAll)
                    {
                        grantedCommands.AddRange(commands);
                    }
                    else
                    {
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
                    }

                    client.GivePermission(ClientPermissions.ConsoleCommands);
                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Union(grantedCommands).Distinct().ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    if (giveAll)
                    {
                        NewMessage("Gave the client \"" + client.Name + "\" the permission to use all console commands.", Color.White);
                    }
                    else if (grantedCommands.Count > 0)
                    {
                        NewMessage("Gave the client \"" + client.Name + "\" the permission to use console commands " + string.Join(", ", grantedCommands.Select(c => c.names[0])) + ".", Color.White);
                    }

                }, args, 1);
            });

            AssignOnExecute("revokecommandperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("revokecommandperm [id/steamid/endpoint/name]: Revokes permission to use the specified console commands from the specified client.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
                    return;
                }
                if (client.Connection == GameMain.Server.OwnerConnection)
                {
                    NewMessage("Cannot revoke command permissions from the server owner!", Color.Red);
                    return;
                }

                ShowQuestionPrompt("Console command permissions to revoke from \"" + client.Name + "\"? You may enter multiple commands separated with a space.", (commandsStr) =>
                {
                    string[] splitCommands = commandsStr.Split(' ');
                    List<Command> revokedCommands = new List<Command>();
                    bool revokeAll = splitCommands.Length > 0 && splitCommands[0].Equals("all", StringComparison.OrdinalIgnoreCase);
                    if (revokeAll)
                    {
                        revokedCommands.AddRange(commands);
                    }
                    else
                    {
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
                    }                    

                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Except(revokedCommands).ToList());
                    GameMain.Server.UpdateClientPermissions(client);
                    if (revokeAll)
                    {
                        NewMessage("Revoked \"" + client.Name + "\"'s permission to use console commands.", Color.White);
                    }
                    else if (revokedCommands.Any())
                    {
                        NewMessage("Revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", Color.White);
                    }
                }, args, 1);
            });

            AssignOnExecute("showperm", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                if (args.Length < 1)
                {
                    NewMessage("showperm [id/steamid/endpoint/name]: Shows the current administrative permissions of the specified client.", Color.Cyan);
                    return;
                }

                var client = FindClient(args[0]);
                if (client == null)
                {
                    ThrowError("Client \"" + args[0] + "\" not found.");
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
            AssignOnClientRequestExecute("togglekarma", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                if (GameMain.Server == null) return;
                GameMain.Server.ServerSettings.KarmaEnabled = !GameMain.Server.ServerSettings.KarmaEnabled;
                NewMessage((GameMain.Server.ServerSettings.KarmaEnabled ? "Karma system enabled by " : "Karma system disabled by ") + client.Name, Color.LightGreen);
                GameMain.Server.SendConsoleMessage(GameMain.Server.ServerSettings.KarmaEnabled ? "Karma system enabled." : "Karma system disabled.", client);
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
                    NewMessage("- " + c.SessionId.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Karma, Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            });
            AssignOnClientRequestExecute("showkarma", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.SessionId.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Karma, client);
                }
                GameMain.Server.SendConsoleMessage("***************", client);
            });
            AssignOnExecute("togglekarmatestmode|karmatestmode", (string[] args) =>
            {
                if (GameMain.Server?.KarmaManager == null) { return; }
                GameMain.Server.KarmaManager.TestMode = !GameMain.Server.KarmaManager.TestMode;
                NewMessage(GameMain.Server.KarmaManager.TestMode ? "Karma test mode enabled." : "Karma test mode disabled.", Color.LightGreen);
            });
            AssignOnClientRequestExecute("togglekarmatestmode|karmatestmode", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                if (GameMain.Server?.KarmaManager == null) { return; }
                GameMain.Server.KarmaManager.TestMode = !GameMain.Server.KarmaManager.TestMode;
                NewMessage(GameMain.Server.KarmaManager.TestMode ? 
                    $"Karma test mode enabled by {client.Name}." :
                    $"Karma test mode disabled by {client.Name}.", 
                    Color.LightGreen);
                GameMain.Server.SendDirectChatMessage(
                    GameMain.Server.KarmaManager.TestMode ? "Karma test mode enabled." : "Karma test mode disabled.",
                    client);
            });

            AssignOnExecute("banaddress", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;

                if (!(Address.Parse(args[0]).TryUnwrap(out var address))) { return; }
                
                ShowQuestionPrompt("Reason for banning the endpoint \"" + args[0] + "\"? (c to cancel)", (reason) =>
                {
                    if (reason == "c" || reason == "C") { return; }
                    ShowQuestionPrompt("Enter the duration of the ban (leave empty to ban permanently, or use the format \"[days] d [hours] h\") (c to cancel)", (duration) =>
                    {
                        if (duration == "c" || duration == "C") { return; }
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

                        var clients = GameMain.Server.ConnectedClients.Where(c => c.AddressMatches(address)).ToList();
                        if (clients.Count == 0)
                        {
                            GameMain.Server.ServerSettings.BanList.BanPlayer("Unnamed", address, reason, banDuration);
                        }
                        else
                        {
                            foreach (Client cl in clients)
                            {
                                GameMain.Server.BanClient(cl, reason, banDuration);
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
                GameMain.Server.SendDirectChatMessage(TextManager.Get("MutedByServer").Value, client, ChatMessageType.MessageBox);
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
                GameMain.Server.SendDirectChatMessage(TextManager.Get("UnmutedByServer").Value, client, ChatMessageType.MessageBox);
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
                client.SpectateOnly = false;
            });

            AssignOnExecute("starttraitormissionimmediately", (string[] args) =>
            {
                GameMain.Server?.TraitorManager?.SkipStartDelay();
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
                    NewMessage(
                        $"- {c.SessionId}: {c.Name}{(c.Character != null ? " playing " + c.Character.LogName : "")}, {c.Connection.Endpoint.StringRepresentation}, {c.Connection.AccountInfo.AccountId}, ping {c.Ping} ms", Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));
            AssignOnClientRequestExecute("clientlist", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client, Color.Cyan);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.SessionId.ToString() + ": " + c.Name + ", " + c.Connection.Endpoint.StringRepresentation + $", ping {c.Ping} ms", client, Color.Cyan);
                }
                GameMain.Server.SendConsoleMessage("***************", client, Color.Cyan);
            });

            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Enabled cheat commands.", Color.Red);
#if USE_STEAM
                NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
#endif
                GameMain.Server?.UpdateCheatsEnabled();
            }));
            AssignOnClientRequestExecute("enablecheats", (client, cursorPos, args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Cheat commands have been enabled by \"" + client.Name + "\".", Color.Red);
#if USE_STEAM
                NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
#endif
                GameMain.Server?.UpdateCheatsEnabled();
            });

            commands.Add(new Command("traitorlist", "traitorlist: List all the traitors and their targets.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null || traitorManager.Traitors == null || !traitorManager.Traitors.Any())
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
                //NewMessage("The code words are: " + traitorManager.CodeWords + ", response: " + traitorManager.CodeResponse + ".", Color.Cyan);
            }));
            AssignOnClientRequestExecute("traitorlist", (Client client, Vector2 cursorPos, string[] args) =>
            {
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null || traitorManager.Traitors == null || !traitorManager.Traitors.Any())
                {
                    GameMain.Server.SendTraitorMessage(client, "There are no traitors at the moment.", Identifier.Empty, TraitorMessageType.Console);
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
                            }.Where(s => !string.IsNullOrEmpty(s))), t.Mission.Identifier, TraitorMessageType.Console);
                    }
                    else
                    {
                        GameMain.Server.SendTraitorMessage(client, string.Format("- Traitor {0} has no current objective.", t.Character.Name), Identifier.Empty, TraitorMessageType.Console);
                    }
                }
                //GameMain.Server.SendTraitorMessage(client, "The code words are: " + traitorManager.CodeWords + ", response: " + traitorManager.CodeResponse + ".", TraitorMessageType.Console);
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
                    if (maxPlayers > NetConfig.MaxPlayers)
                    {
                        NewMessage($"Setting the maximum amount of players to {maxPlayers} failed due to exceeding the limit of {NetConfig.MaxPlayers} players per server. Using the maximum of {NetConfig.MaxPlayers} instead.");
                        maxPlayers = NetConfig.MaxPlayers;
                    }

                    GameMain.Server.ServerSettings.MaxPlayers = maxPlayers;
                    NewMessage("Set the maximum player count to " + maxPlayers + ".");
                }
            }));
            AssignOnClientRequestExecute("setmaxplayers", (Client client, Vector2 cursorPos, string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                if (!int.TryParse(args[0], out int maxPlayers))
                {
                    GameMain.Server.SendConsoleMessage(args[0] + " is not a valid player count.", client, Color.Red);
                }
                else
                {
                    if (maxPlayers > NetConfig.MaxPlayers)
                    {
                        GameMain.Server.SendConsoleMessage($"Setting the maximum amount of players to {maxPlayers} failed due to exceeding the limit of {NetConfig.MaxPlayers} players per server. Using the maximum of {NetConfig.MaxPlayers} instead.", client, Color.Red);
                        maxPlayers = NetConfig.MaxPlayers;
                    }

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
                Program.TryStartChildServerRelay(GameMain.Instance.CommandLineArgs);
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
                GameMain.Server.ServerName = string.Join(" ", args);
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
                            GameMain.NetLobbyScreen.GameModes[index] == GameModePreset.MultiPlayerCampaign)
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
                        if (modeName.Equals("campaign", StringComparison.OrdinalIgnoreCase))
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
                    GameModePreset.List.Select(gm => gm.Name.Value).ToArray()
                };
            }));

            commands.Add(new Command("mission", "mission [name]: Select the mission type for the next round.", (string[] args) =>
            {
                GameMain.NetLobbyScreen.MissionTypeName = string.Join(" ", args);
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
                SubmarineInfo sub = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.Equals(string.Join(" ", args), StringComparison.OrdinalIgnoreCase));

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
                    SubmarineInfo.SavedSubmarines.Select(s => s.Name).ToArray()
                };
            }));

            commands.Add(new Command("shuttle", "shuttle [name]: Select the specified submarine as the respawn shuttle for the next round.", (string[] args) =>
            {
                SubmarineInfo shuttle = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.ToLower() == string.Join(" ", args).ToLower());

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
                    SubmarineInfo.SavedSubmarines.Select(s => s.Name).ToArray()
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
                if (Screen.Selected == GameMain.GameScreen) { return; }
                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign mpCampaign && 
                    GameMain.NetLobbyScreen.SelectedMode == GameModePreset.MultiPlayerCampaign)
                {
                    MultiPlayerCampaign.LoadCampaign(GameMain.GameSession.SavePath);
                }
                else
                {
                    if (GameMain.NetLobbyScreen.SelectedMode == GameModePreset.MultiPlayerCampaign)
                    {
                        MultiPlayerCampaign.StartCampaignSetup();
                        return;
                    }
                    if (!GameMain.Server.StartGame()) { NewMessage("Failed to start a new round", Color.Yellow); }
                }
            }));

            commands.Add(new Command("endgame|endround|end", "end/endgame/endround: End the current round.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.NetLobbyScreen) { return; }
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


            AssignOnExecute("resetcharacternetstate", (string[] args) =>
            {
                if (GameMain.Server == null) { return; }

                if (args.Length < 1)
                {
                    ThrowError("Invalid parameters. The command should be formatted as \"resetcharacternetstate [character]\". If the names consist of multiple words, you should surround them with quotation marks.");
                    return;
                }

                var character = FindMatchingCharacter(args.Skip(1).ToArray(), false);
                character?.ResetNetState();
            });

            commands.Add(new Command("eventdata", "", (string[] args) =>
            {
                if (args.Length == 0) { return; }

                string indentStr(string s)
                    => string.Join('\n', s.Split('\n').Select(sub => $"    {sub}"));
                
                string eventDataRip(object data)
                {
                    if (data is null) { return "[NULL]"; }
                    var type = data.GetType();
                    
                    string retVal = $"{type.FullName} ";

                    if (type.IsPrimitive
                        || type.IsEnum
                        || type.IsClass)
                    {
                        retVal += data.ToString();
                        return retVal;
                    }

                    retVal += "{\n";
                    var fields = data.GetType().GetFields();
                    foreach (var field in fields)
                    {
                        retVal += indentStr($"{field.Name}: {eventDataRip(field.GetValue(data))}")+"\n";
                    }

                    retVal += "}";
                    retVal = retVal.Replace("{\n}", "{ }");
                    return retVal;
                }

                string eventDebugStr(ServerEntityEvent ev)
                {
                    ushort eventId = ev.ID;
                    
                    string entityData = "";
                    if (ev.Entity is { ID: var entityId, Removed: var removed, IdFreed: var idFreed })
                    {
                        entityData = $"Entity ID: {entityId}\n" +
                                     $"Entity type {ev.Entity.GetType().Name}\n" +
                                     $"Entity removed: {removed}\n" +
                                     $"Entity ID freed: {idFreed}\n" +
                                     $"Event data: {eventDataRip(ev.Data)}\n";
                    }

                    return $"EventData {eventId}\n{indentStr(entityData)}";
                }
                
                IReadOnlyList<ServerEntityEvent> events = GameMain.Server.EntityEventManager.Events;
                ushort? eventId = null;
                if (args[0].Equals("latest", StringComparison.OrdinalIgnoreCase))
                {
                    eventId = events.Max(e => e.ID);
                }
                else if (UInt16.TryParse(args[0], NumberStyles.Any, CultureInfo.InvariantCulture, out ushort id))
                {
                    eventId = id;
                }
                IEnumerable<ServerEntityEvent> matchedEvents = GameMain.Server.EntityEventManager.Events.Where(ev
                    => eventId.HasValue
                        ? ev.ID == eventId
                        : eventDebugStr(ev).Contains(args[0], StringComparison.OrdinalIgnoreCase));
                foreach (var ev in matchedEvents)
                {
                    NewMessage(eventDebugStr(ev), Color.Lime);
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
                "banaddress|banip",
                (Client client, Vector2 cursorPos, string[] args) =>
                {
                    if (args.Length < 1) { return; }
                    if (!(Address.Parse(args[0]).TryUnwrap(out var address))) { return; }
                    var clients = GameMain.Server.ConnectedClients.Where(c => c.AddressMatches(address)).ToList();
                    TimeSpan? duration = null;
                    if (args.Length > 1)
                    {
                        if (double.TryParse(args[1], out double durationSeconds))
                        {
                            if (durationSeconds > 0) duration = TimeSpan.FromSeconds(durationSeconds);
                        }
                        else
                        {
                            GameMain.Server.SendConsoleMessage("\"" + args[1] + "\" is not a valid ban duration.", client, Color.Red);
                            return;
                        }
                    }
                    string reason = "";
                    if (args.Length > 2) reason = string.Join(" ", args.Skip(2));

                    if (clients.Count == 0)
                    {
                        GameMain.Server.ServerSettings.BanList.BanPlayer("Unnamed", address, reason, duration);
                    }
                    else
                    {
                        foreach (Client cl in clients)
                        {
                            GameMain.Server.BanClient(cl, reason, duration);
                        }
                    }
                }
            );

            commands.Add(new Command("unban", "unban [name]: Unban a specific client.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                string clientName = string.Join(" ", args);
                GameMain.Server.UnbanPlayer(clientName);
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ServerSettings.BanList.BannedNames.Where(name => !string.IsNullOrEmpty(name)).ToArray()
                };
            }));

            commands.Add(new Command("unbanaddress", "unbanaddress [endpoint]: Unban a specific endpoint.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                if (Endpoint.Parse(args[0]).TryUnwrap(out var endpoint))
                {
                    GameMain.Server.UnbanPlayer(endpoint);
                }
            },
            () =>
            {
                if (GameMain.Server == null) return null;
                return new string[][]
                {
                    GameMain.Server.ServerSettings.BanList.BannedAddresses.Select(ep => ep.ToString()).ToArray()
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
                        GameMain.Server.SendConsoleMessage("\"" + args[0] + "\" is not a valid bot spawn mode. (Valid modes are Fill and Normal)", client, Color.Red);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "teleportcharacter|teleport",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character tpCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args, false);
                    if (tpCharacter != null)
                    {
                        tpCharacter.TeleportTo(cursorWorldPos);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "teleportsub",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (Submarine.MainSub == null || Level.Loaded == null) { return; }
                    if (args.Length == 0 || args[0].Equals("cursor", StringComparison.OrdinalIgnoreCase))
                    {
                        Submarine.MainSub.SetPosition(cursorWorldPos);
                    }
                    else if (args[0].Equals("start", StringComparison.OrdinalIgnoreCase))
                    {
                        Submarine.MainSub.SetPosition(Level.Loaded.StartPosition - Vector2.UnitY * Submarine.MainSub.Borders.Height);
                    }
                    else
                    {
                        Submarine.MainSub.SetPosition(Level.Loaded.EndPosition - Vector2.UnitY * Submarine.MainSub.Borders.Height);
                    }
                }
            );

            AssignOnClientRequestExecute("togglecampaignteleport",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (!(GameMain.GameSession?.Campaign is MultiPlayerCampaign mpCampaign))
                    {
                        GameMain.Server.SendConsoleMessage("No campaign active.", client, Color.Red);
                        return;
                    }
                    mpCampaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
                    GameMain.GameSession.Map.AllowDebugTeleport = !GameMain.GameSession.Map.AllowDebugTeleport;
                    NewMessage(client.Name + (GameMain.GameSession.Map.AllowDebugTeleport ? " enabled" : " disabled") + " teleportation on the campaign map.", Color.White);
                    GameMain.Server.SendConsoleMessage((GameMain.GameSession.Map.AllowDebugTeleport ? "Enabled" : "Disabled") + " teleportation on the campaign map.", client);
                }
            );

            AssignOnClientRequestExecute(
                "godmode",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character targetCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args, false);

                    if (targetCharacter == null) { return; }

                    targetCharacter.GodMode = !targetCharacter.GodMode;

                    NewMessage(targetCharacter.Name + (targetCharacter.GodMode ? "'s godmode turned on by \"" : "'s godmode turned off by \"") + client.Name + "\"", Color.White);
                    GameMain.Server.SendConsoleMessage(targetCharacter.Name + (targetCharacter.GodMode ? "'s godmode on" : "'s godmode off"), client);
                }
            );

            AssignOnClientRequestExecute(
                "godmode_mainsub",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (Submarine.MainSub == null) return;

                    Submarine.MainSub.GodMode = !Submarine.MainSub.GodMode;
                    NewMessage((Submarine.MainSub.GodMode ? "Mainsub godmode turned on by \"" : "Mainsub godmode turned off by \"") + client.Name + "\"", Color.White);
                    GameMain.Server.SendConsoleMessage(Submarine.MainSub.GodMode ? "Mainsub godmode on" : "Mainsub godmode off", client);
                }
            );

            AssignOnClientRequestExecute(
                "giveaffliction",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;
                    string affliction = args[0];
                    AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(a => a.Identifier == affliction);
                    if (afflictionPrefab == null)
                    {
                        afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(a => a.Name.Equals(affliction, StringComparison.OrdinalIgnoreCase));
                    }
                    if (afflictionPrefab == null)
                    {
                        GameMain.Server.SendConsoleMessage("Affliction \"" + affliction + "\" not found.", client, Color.Red);
                        return;
                    }
                    if (!float.TryParse(args[1], out float afflictionStrength))
                    {
                        GameMain.Server.SendConsoleMessage("\"" + args[1] + "\" is not a valid affliction strength.", client, Color.Red);
                        return;
                    }
                    bool relativeStrength = false;
                    if (args.Length > 4)
                    {
                        bool.TryParse(args[4], out relativeStrength);
                    }
                    Character targetCharacter = (args.Length <= 2) ? client.Character : FindMatchingCharacter(args.Skip(2).ToArray());
                    if (targetCharacter != null)
                    {
                        Limb targetLimb = targetCharacter.AnimController.MainLimb;
                        if (args.Length > 3)
                        {
                            targetLimb = targetCharacter.AnimController.Limbs.FirstOrDefault(l => l.type.ToString().Equals(args[3], StringComparison.OrdinalIgnoreCase));
                        }
                        if (relativeStrength)
                        {
                            afflictionStrength *= targetCharacter.MaxVitality / afflictionPrefab.MaxStrength;
                        }
                        targetCharacter.CharacterHealth.ApplyAffliction(targetLimb ?? targetCharacter.AnimController.MainLimb, afflictionPrefab.Instantiate(afflictionStrength));
                    }
                }
            );

            AssignOnClientRequestExecute(
                "heal",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    bool healAll = args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
                    Character healedCharacter = (args.Length == 0) ? Character.Controlled : FindMatchingCharacter(healAll ? args.Take(args.Length - 1).ToArray() : args);
                    if (healedCharacter != null)
                    {
                        healedCharacter.SetAllDamage(0.0f, 0.0f, 0.0f);
                        healedCharacter.Oxygen = 100.0f;
                        healedCharacter.Bloodloss = 0.0f;
                        healedCharacter.SetStun(0.0f, true);
                        if (healAll)
                        {
                            healedCharacter.CharacterHealth.RemoveAllAfflictions();
                        }
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
                            if (c.Character != revivedCharacter) { continue; }

                            //clients stop controlling the character when it dies, force control back
                            GameMain.Server.SetClientCharacter(c, revivedCharacter);
                            break;
                        }
                    }
                }
            );

            AssignOnClientRequestExecute(
                "givetalent",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length == 0) { return; }
                    Character targetCharacter = (args.Length >= 2) ? FindMatchingCharacter(args.Skip(1).ToArray(), false) : client.Character;

                    if (targetCharacter == null) { return; }

                    TalentPrefab talentPrefab = TalentPrefab.TalentPrefabs.Find(c =>
                        c.Identifier == args[0] ||
                        c.DisplayName.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                    if (talentPrefab == null)
                    {
                        GameMain.Server.SendConsoleMessage("Couldn't find the talent \"" + args[0] + "\".", client, Color.Red);
                        return;
                    }
                    targetCharacter.GiveTalent(talentPrefab);
                    NewMessage($"Talent \"{talentPrefab.DisplayName}\" given to \"{targetCharacter.Name}\" by \"{client.Name}\".");
                    GameMain.Server.SendConsoleMessage($"Gave talent \"{talentPrefab.DisplayName}\" to \"{targetCharacter.Name}\".", client);
                }
            );

            AssignOnClientRequestExecute(
                "unlocktalents",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    var targetCharacter = args.Length >= 2 ? FindMatchingCharacter(args.Skip(1).ToArray()) : Character.Controlled;
                    if (targetCharacter == null) { return; }

                    List<TalentTree> talentTrees = new List<TalentTree>();
                    if (args.Length == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        talentTrees.AddRange(TalentTree.JobTalentTrees);
                    }
                    else
                    {
                        var job = JobPrefab.Prefabs.Find(jp => jp.Name != null && jp.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
                        if (job == null)
                        {
                            GameMain.Server.SendConsoleMessage($"Failed to find the job \"{args[0]}\".", client, Color.Red);
                            return;
                        }
                        if (!TalentTree.JobTalentTrees.TryGet(job.Identifier, out TalentTree talentTree))
                        {
                            GameMain.Server.SendConsoleMessage($"No talents configured for the job \"{args[0]}\".", client, Color.Red);
                            return;
                        }
                        talentTrees.Add(talentTree);
                    }

                    foreach (var talentTree in talentTrees)
                    {          
                        foreach (var talentId in talentTree.AllTalentIdentifiers)
                        {
                            if (TalentPrefab.TalentPrefabs.TryGet(talentId, out TalentPrefab talentPrefab))
                            {
                                targetCharacter.GiveTalent(talentPrefab);
                                NewMessage($"Talent \"{talentPrefab.DisplayName}\" given to \"{targetCharacter.Name}\" by \"{client.Name}\".");
                                GameMain.Server.SendConsoleMessage($"Gave talent \"{talentPrefab.DisplayName}\" to \"{targetCharacter.Name}\".", client);
                                NewMessage($"Unlocked talent \"{talentPrefab.DisplayName}\".");
                            }
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
                    float range = 500, force = 10, damage = 50, structureDamage = 20, itemDamage = 100, empStrength = 0.0f, ballastFloraStrength = 50f;
                    if (args.Length > 0) float.TryParse(args[0], out range);
                    if (args.Length > 1) float.TryParse(args[1], out force);
                    if (args.Length > 2) float.TryParse(args[2], out damage);
                    if (args.Length > 3) float.TryParse(args[3], out structureDamage);
                    if (args.Length > 4) float.TryParse(args[4], out itemDamage);
                    if (args.Length > 5) float.TryParse(args[5], out empStrength);
                    if (args.Length > 6) float.TryParse(args[6], out ballastFloraStrength);
                    new Explosion(range, force, damage, structureDamage, itemDamage, empStrength, ballastFloraStrength).Explode(explosionPos, null);
                }
            );

            AssignOnClientRequestExecute(
                "kill",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    Character killedCharacter = (args.Length == 0) ? client.Character : FindMatchingCharacter(args);
                    if (killedCharacter == null)
                    {
                        GameMain.Server.SendConsoleMessage("Could not find the specified character.", client, Color.Red);
                    }
                    killedCharacter?.SetAllDamage(200.0f, 0.0f, 0.0f);
                }
            );

            AssignOnClientRequestExecute(
                "control",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 1) { return; }
                    var character = FindMatchingCharacter(args, ignoreRemotePlayers: true, allowedRemotePlayer: client);
                    if (character != null)
                    {
                        GameMain.Server.SetClientCharacter(client, character);
                        client.SpectateOnly = false;
                    }
                    else
                    {
                        GameMain.Server.SendConsoleMessage("Could not find the specified character.", client, Color.Red);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "freecam",
                (Client client, Vector2 cursorWorldPos, string[] args) =>
                {
                    GameMain.Server.SetClientCharacter(client, null);
                    client.SpectateOnly = true;
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
                        GameMain.Server.SendConsoleMessage(args[0] + " is not a valid difficulty setting (enter a value between 0-100)", client, Color.Red);
                        NewMessage(args[0] + " is not a valid difficulty setting (enter a value between 0-100)", Color.Red);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "giveperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        ThrowError("Client \"" + args[0] + "\" not found.");
                        return;
                    }

                    string perm = string.Join("", args.Skip(1));

                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient, Color.Red);
                        return;
                    }

                    if (permission == ClientPermissions.None)
                    {
                        GameMain.Server.SendConsoleMessage($"No permissions were given to {client.Name}. Did you mean \"revokeperm {client.Name} All\"?", senderClient);
                        NewMessage($"No permissions were given to {client.Name}. Did you mean \"revokeperm {client.Name} All\"?");
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

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        ThrowError("Client \"" + args[0] + "\" not found.");
                        return;
                    }
                    if (client.Connection == GameMain.Server.OwnerConnection)
                    {
                        GameMain.Server.SendConsoleMessage("Cannot revoke permissions from the server owner!", senderClient, Color.Red);
                        return;
                    }

                    string perm = string.Join("", args.Skip(1));

                    ClientPermissions permission = ClientPermissions.None;
                    if (!Enum.TryParse(perm, true, out permission))
                    {
                        GameMain.Server.SendConsoleMessage(perm + " is not a valid permission!", senderClient, Color.Red);
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

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        ThrowError("Client \"" + args[0] + "\" not found.");
                        return;
                    }
                    if (client.Connection == GameMain.Server.OwnerConnection)
                    {
                        GameMain.Server.SendConsoleMessage("Cannot modify the rank of the server owner!", senderClient, Color.Red);
                        return;
                    }

                    string rank = string.Join("", args.Skip(1));
                    PermissionPreset preset = PermissionPreset.List.Find(p => p.Name.Equals(rank, StringComparison.OrdinalIgnoreCase));
                    if (preset == null)
                    {
                        GameMain.Server.SendConsoleMessage("Rank \"" + rank + "\" not found.", senderClient, Color.Red);
                        return;
                    }

                    client.SetPermissions(preset.Permissions, preset.PermittedCommands);
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage($"Assigned the rank \"{preset.Name}\" to {client.Name}.", senderClient);
                    NewMessage(senderClient.Name + " granted  the rank \"" + preset.Name + "\" to " + client.Name + ".", Color.White);
                }
            );

            AssignOnClientRequestExecute(
                "givecommandperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) return;

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client \"" + args[0] + "\" not found.", senderClient, Color.Red);
                        return;
                    }
                    if (client.Connection == GameMain.Server.OwnerConnection)
                    {
                        GameMain.Server.SendConsoleMessage("Cannot modify the command permissions of the server owner!", senderClient, Color.Red);
                        return;
                    }

                    List<Command> grantedCommands = new List<Command>();
                    string[] splitCommands = args.Skip(1).ToArray();
                    bool giveAll = splitCommands.Length > 0 && splitCommands[0].Equals("all", StringComparison.OrdinalIgnoreCase);
                    if (giveAll)
                    {
                        grantedCommands.AddRange(commands);
                    }
                    else
                    {
                        for (int i = 0; i < splitCommands.Length; i++)
                        {
                            splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                            Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                            if (matchingCommand == null)
                            {
                                GameMain.Server.SendConsoleMessage("Could not find the command \"" + splitCommands[i] + "\"!", senderClient, Color.Red);
                            }
                            else
                            {
                                grantedCommands.Add(matchingCommand);
                            }
                        }
                    }

                    client.GivePermission(ClientPermissions.ConsoleCommands);
                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Union(grantedCommands).Distinct().ToList());

                    GameMain.Server.UpdateClientPermissions(client);
                    if (giveAll)
                    {
                        GameMain.Server.SendConsoleMessage("Gave the client \"" + client.Name + "\" the permission to use all console commands.", senderClient);
                    }
                    else if (grantedCommands.Count > 0)
                    {
                        GameMain.Server.SendConsoleMessage("Gave the client \"" + client.Name + "\" the permission to use console commands " + string.Join(", ", grantedCommands.Select(c => c.names[0])) + ".", senderClient);
                    }                
                }
            );

            AssignOnClientRequestExecute(
                "revokecommandperm",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length < 2) { return; }

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        ThrowError("Client \"" + args[0] + "\" not found.");
                        return;
                    }
                    if (client.Connection == GameMain.Server.OwnerConnection)
                    {
                        GameMain.Server.SendConsoleMessage("Cannot revoke command permissions from the server owner!", senderClient, Color.Red);
                        return;
                    }
                    List<Command> revokedCommands = new List<Command>();
                    string[] splitCommands = args.Skip(1).ToArray();
                    bool revokeAll = splitCommands.Length > 0 && splitCommands[0].Equals("all", StringComparison.OrdinalIgnoreCase);
                    if (revokeAll)
                    {
                        revokedCommands.AddRange(commands);
                    }
                    else
                    {
                        for (int i = 0; i < splitCommands.Length; i++)
                        {
                            splitCommands[i] = splitCommands[i].Trim().ToLowerInvariant();
                            Command matchingCommand = commands.Find(c => c.names.Contains(splitCommands[i]));
                            if (matchingCommand == null)
                            {
                                GameMain.Server.SendConsoleMessage("Could not find the command \"" + splitCommands[i] + "\"!", senderClient, Color.Red);
                            }
                            else
                            {
                                revokedCommands.Add(matchingCommand);
                            }
                        }
                    }

                    client.SetPermissions(client.Permissions, client.PermittedConsoleCommands.Except(revokedCommands).ToList());
                    if (client.PermittedConsoleCommands.Count == 0)
                    {
                        client.RemovePermission(ClientPermissions.ConsoleCommands);
                    }
                    GameMain.Server.UpdateClientPermissions(client);
                    GameMain.Server.SendConsoleMessage("Revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", senderClient);
                    if (revokeAll)
                    {
                        GameMain.Server.SendConsoleMessage("Revoked \"" + client.Name + "\"'s permission to use console commands.", senderClient);
                    }
                    else if (revokedCommands.Count > 0)
                    {
                        GameMain.Server.SendConsoleMessage("Revoked \"" + client.Name + "\"'s permission to use the console commands " + string.Join(", ", revokedCommands.Select(c => c.names[0])) + ".", senderClient);
                    }
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

                    var client = FindClient(args[0]);
                    if (client == null)
                    {
                        ThrowError("Client \"" + args[0] + "\" not found.");
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
                        GameMain.Server.SendConsoleMessage($"   - {TextManager.Get("ClientPermission." + permission)}", senderClient);
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
                        GameMain.Server.SendConsoleMessage("Invalid parameters. The command should be formatted as \"setclientcharacter [client] [character]\". If the names consist of multiple words, you should surround them with quotation marks.", senderClient, Color.Red);
                        ThrowError("Invalid parameters. The command should be formatted as \"setclientcharacter [client] [character]\". If the names consist of multiple words, you should surround them with quotation marks.");
                        return;
                    }

                    var client = GameMain.Server.ConnectedClients.Find(c => c.Name == args[0]);
                    if (client == null)
                    {
                        GameMain.Server.SendConsoleMessage("Client \"" + args[0] + "\" not found.", senderClient, Color.Red);
                        return;
                    }

                    var character = FindMatchingCharacter(args.Skip(1).ToArray(), false);
                    GameMain.Server.SetClientCharacter(client, character);
                    client.SpectateOnly = false;
                }
            );

            AssignOnClientRequestExecute(
                "money",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (args.Length == 0) { return; }
                    if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign))
                    {
                        GameMain.Server.SendConsoleMessage("No campaign active!", senderClient, Color.Red);
                        return;
                    }

                    Character targetCharacter = null;

                    if (args.Length >= 2)
                    {
                        targetCharacter = FindMatchingCharacter(args.Skip(1).ToArray());
                    }

                    if (int.TryParse(args[0], out int money))
                    {
                        Wallet wallet = targetCharacter is null ? campaign.Bank : targetCharacter.Wallet;
                        wallet.Give(money);
                        GameAnalyticsManager.AddMoneyGainedEvent(money, GameAnalyticsManager.MoneySource.Cheat, "console");
                    }
                    else
                    {
                        GameMain.Server.SendConsoleMessage($"\"{args[0]}\" is not a valid numeric value.", senderClient, Color.Red);
                    }
                }
            );

            AssignOnClientRequestExecute(
                "showmoney",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign))
                    {
                        GameMain.Server.SendConsoleMessage("No campaign active!", senderClient, Color.Red);
                        return;
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.Append($"Bank: {campaign.Bank.Balance}");
                    foreach (Client client in GameMain.Server.ConnectedClients)
                    {
                        if (client.Character is null) { continue; }
                        sb.Append(Environment.NewLine);
                        sb.Append($"{client.Name}: {client.Character.Wallet.Balance}");
                    }
                    GameMain.Server.SendConsoleMessage(sb.ToString(), senderClient);
                }
            );

            AssignOnClientRequestExecute(
                "campaigndestination|setcampaigndestination",
                (Client senderClient, Vector2 cursorWorldPos, string[] args) =>
                {
                    if (!(GameMain.GameSession?.GameMode is CampaignMode campaign))
                    {
                        GameMain.Server.SendConsoleMessage("No campaign active!", senderClient, Color.Red);
                        return;
                    }

                    int destinationIndex = -1;
                    if (args.Length < 1 || !int.TryParse(args[0], out destinationIndex)) return;
                    if (destinationIndex < 0 || destinationIndex >= campaign.Map.CurrentLocation.Connections.Count)
                    {
                        GameMain.Server.SendConsoleMessage("Index out of bounds!", senderClient, Color.Red);
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
                    NewMessage(tag.Value, Color.Yellow);
                }
            }));

            commands.Add(new Command("sendchatmessage", "Sends a chat message with specified type and color.", (string[] args) =>
            {
                if (args.Length < 2) { return; }

                ChatMessageType chatMessageType = ChatMessageType.Default;
                Color? chatMessageColor = null;

                if (args.Length >= 3 && int.TryParse(args[2], out int result))
                {
                    chatMessageType = (ChatMessageType)result;
                }

                if (args.Length >= 7 &&
                    int.TryParse(args[3], out int r) &&
                    int.TryParse(args[4], out int g) &&
                    int.TryParse(args[5], out int b) &&
                    int.TryParse(args[6], out int a))
                {
                    chatMessageColor = new Color(r, g, b, a);
                }

                foreach (var client in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendDirectChatMessage(ChatMessage.Create(args[0], args[1], chatMessageType, null, null, textColor: chatMessageColor), client);
                }
            }));

            AssignOnClientRequestExecute(
                "setskill",
                (senderClient, cursorWorldPos, args) =>
                {
                    if (args.Length < 2)
                    {
                        GameMain.Server.SendConsoleMessage($"Missing arguments. Expected at least 2 but got {args.Length} (skill, level, name)", senderClient, Color.Red);
                        return;
                    }

                    Identifier skillIdentifier = args[0].ToIdentifier();
                    string levelString = args[1];
                    Character character = args.Length >= 3 ? FindMatchingCharacter(args.Skip(2).ToArray(), false) : senderClient.Character;

                    if (character?.Info?.Job == null)
                    {
                        GameMain.Server.SendConsoleMessage("Character is not valid.", senderClient, Color.Red);
                        return;
                    }

                    bool isMax = levelString.Equals("max", StringComparison.OrdinalIgnoreCase);

                    if (float.TryParse(levelString, NumberStyles.Number, CultureInfo.InvariantCulture, out float level) || isMax)
                    {
                        if (isMax) { level = 100; }
                        if (skillIdentifier == "all")
                        {
                            foreach (Skill skill in character.Info.Job.GetSkills())
                            {
                                character.Info.SetSkillLevel(skill.Identifier, level);
                            }
                            GameMain.Server.SendConsoleMessage($"Set all {character.Name}'s skills to {level}", senderClient);
                        }
                        else
                        {
                            character.Info.SetSkillLevel(skillIdentifier, level);
                            GameMain.Server.SendConsoleMessage($"Set {character.Name}'s {skillIdentifier} level to {level}", senderClient);
                        }

                        GameMain.NetworkMember.CreateEntityEvent(character, new Character.UpdateSkillsEventData());                
                    }
                    else
                    {
                        GameMain.Server.SendConsoleMessage($"{levelString} is not a valid level. Expected number or \"max\".", senderClient, Color.Red);
                    }                  
                }
            );

            commands.Add(new Command("readycheck", "Commence a ready check.", (string[] args) =>
            {
                if (Screen.Selected == GameMain.GameScreen && GameMain.NetworkMember != null)
                {
                    CrewManager crewManager = GameMain.GameSession?.CrewManager;
                    if (crewManager != null && crewManager.ActiveReadyCheck == null)
                    {
                        ReadyCheck.StartReadyCheck("");
                        NewMessage("Attempted to commence a ready check.", Color.Green);
                        return;
                    }
                    NewMessage("A ready check is already running.", Color.Red);
                    return;
                }
                NewMessage("Ready checks cannot be commenced in the lobby.", Color.Red);
            }));

            AssignOnClientRequestExecute(
                "readycheck",
                (senderClient, cursorWorldPos, args) =>
                {
                    if (Screen.Selected == GameMain.GameScreen && GameMain.NetworkMember != null && !(GameMain.GameSession?.GameMode?.IsSinglePlayer ?? true))
                    {
                        CrewManager crewManager = GameMain.GameSession?.CrewManager;
                        if (crewManager != null && crewManager.ActiveReadyCheck == null)
                        {
                            ReadyCheck.StartReadyCheck(senderClient.Name, senderClient);
                            GameMain.Server.SendConsoleMessage("Attempted to commence a ready check.", senderClient);
                            return;
                        }
                        GameMain.Server.SendConsoleMessage("A ready check is already running.", senderClient);
                        return;
                    }
                    GameMain.Server.SendConsoleMessage("Ready checks cannot be commenced in the lobby.", senderClient);
                }
            );

#if DEBUG
            commands.Add(new Command("spamevents", "A debug command that creates a ton of entity events.", (string[] args) =>
            {
                foreach (Item item in Item.ItemList)
                {
                    item.TryCreateServerEventSpam();
                    item.CreateStatusEvent();
                }
                foreach (Structure wall in Structure.WallList)
                {
                    GameMain.Server.CreateEntityEvent(wall);
                }
            }));
            commands.Add(new Command("stallfiletransfers", "stallfiletransfers [seconds]: A debug command that stalls each file transfer packet by the specified duration.", (string[] args) =>
            {
                float seconds = 0.0f;
                if (args.Length > 0)
                {
                    float.TryParse(args[0], out seconds);
                }
                GameMain.Server.FileSender.StallPacketsTime = seconds;
                NewMessage("Set file transfer stall time to " + seconds);
            }));
#endif
        }

        public static void ExecuteClientCommand(Client client, Vector2 cursorWorldPos, string command)
        {
            if (GameMain.Server == null) return;
            if (string.IsNullOrWhiteSpace(command)) return;
            if (!client.HasPermission(ClientPermissions.ConsoleCommands) && client.Connection != GameMain.Server.OwnerConnection)
            {
                GameMain.Server.SendConsoleMessage("You are not permitted to use console commands!", client, Color.Red);
                GameServer.Log(GameServer.ClientLogName(client) + " attempted to execute the console command \"" + command + "\" without a permission to use console commands.", ServerLog.MessageType.ConsoleUsage);
                return;
            }

            string[] splitCommand = ToolBox.SplitCommand(command);
            Command matchingCommand = commands.Find(c => c.names.Contains(splitCommand[0].ToLowerInvariant()));
            if (matchingCommand != null && !client.PermittedConsoleCommands.Contains(matchingCommand) && client.Connection != GameMain.Server.OwnerConnection)
            {
                GameMain.Server.SendConsoleMessage("You are not permitted to use the command\"" + matchingCommand.names[0] + "\"!", client, Color.Red);
                GameServer.Log(GameServer.ClientLogName(client) + " attempted to execute the console command \"" + command + "\" without a permission to use the command.", ServerLog.MessageType.ConsoleUsage);
                return;
            }
            else if (matchingCommand == null)
            {
                GameMain.Server.SendConsoleMessage("Command \"" + splitCommand[0] + "\" not found.", client, Color.Red);
                return;
            }

            if (!MathUtils.IsValid(cursorWorldPos))
            {
                GameMain.Server.SendConsoleMessage("Could not execute command \"" + command + "\" - invalid cursor position.", client, Color.Red);
                NewMessage(GameServer.ClientLogName(client) + " attempted to execute the console command \"" + command + "\" with invalid cursor position.", Color.White);
                return;
            }

            try
            {
                matchingCommand.ServerExecuteOnClientRequest(client, cursorWorldPos, splitCommand.Skip(1).ToArray());
                GameServer.Log("Console command \"" + command + "\" executed by " + GameServer.ClientLogName(client) + ".", ServerLog.MessageType.ConsoleUsage);
            }
            catch (Exception e)
            {
                ThrowError("Executing the command \"" + matchingCommand.names[0] + "\" by request from \"" + GameServer.ClientLogName(client) + "\" failed.", e);
            }
        }

        static partial void ShowHelpMessage(Command command)
        {
            NewMessage(command.names[0], Color.Cyan);
            NewMessage(command.help, Color.Gray);
        }
    }
}
