using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;

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

                    if (GameMain.Config.UseSteam)
                    {
                        NewMessage("Enabling cheats will disable Steam achievements during this play session.", Color.Red);
                        GameMain.Server.SendConsoleMessage("Enabling cheats will disable Steam achievements during this play session.", client);
                        return;
                    }

                    return;
                }

                if (OnClientRequestExecute == null)
                {
                    if (onExecute == null) return;
                    onExecute(args);
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

        private static void AddOnClientRequestExecute(string names, Action<Client, Vector2, string[]> onClientRequestExecute)
        {
            commands.First(c => c.names.Intersect(names.Split('|')).Count() > 0).OnClientRequestExecute = onClientRequestExecute;
        }

        private static void InitProjectSpecific()
        {
            commands.Add(new Command("clientlist", "clientlist: List all the clients connected to the server.", (string[] args) =>
            {
                if (GameMain.Server == null) return;
                NewMessage("***************", Color.Cyan);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    NewMessage("- " + c.ID.ToString() + ": " + c.Name + (c.Character != null ? " playing " + c.Character.LogName : "") + ", " + c.Connection.RemoteEndPoint.Address.ToString(), Color.Cyan);
                }
                NewMessage("***************", Color.Cyan);
            }));
            AddOnClientRequestExecute("clientlist", (Client client, Vector2 cursorWorldPos, string[] args) =>
            {
                GameMain.Server.SendConsoleMessage("***************", client);
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    GameMain.Server.SendConsoleMessage("- " + c.ID.ToString() + ": " + c.Name + ", " + c.Connection.RemoteEndPoint.Address.ToString(), client);
                }
                GameMain.Server.SendConsoleMessage("***************", client);
            });

            commands.Add(new Command("enablecheats", "enablecheats: Enables cheat commands and disables Steam achievements during this play session.", (string[] args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Enabled cheat commands.", Color.Red);
                if (GameMain.Config.UseSteam)
                {
                    NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
                    GameMain.Server?.SendChatMessage("Cheat commands have been enabled by the server. You cannot unlock Steam achievements until you restart the game.", ChatMessageType.MessageBox);
                }
                else
                {
                    GameMain.Server?.SendChatMessage("Cheat commands have been enabled by the server.", ChatMessageType.MessageBox);
                }
            }));
            AddOnClientRequestExecute("enablecheats", (client, cursorPos, args) =>
            {
                CheatsEnabled = true;
                SteamAchievementManager.CheatsEnabled = true;
                NewMessage("Cheat commands have been enabled by \"" + client.Name + "\".", Color.Red);
                if (GameMain.Config.UseSteam)
                {
                    NewMessage("Steam achievements have been disabled during this play session.", Color.Red);
                    GameMain.Server.SendChatMessage("Cheat commands have been enabled by the server. You cannot unlock Steam achievements until you restart the game.", ChatMessageType.MessageBox);
                }
                else
                {
                    GameMain.Server.SendChatMessage("Cheat commands have been enabled by the server.", ChatMessageType.MessageBox);
                }
            });

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
            AddOnClientRequestExecute("traitorlist", (Client client, Vector2 cursorPos, string[] args) =>
            {
                TraitorManager traitorManager = GameMain.Server.TraitorManager;
                if (traitorManager == null) return;
                foreach (Traitor t in traitorManager.TraitorList)
                {
                    GameMain.Server.SendConsoleMessage("- Traitor " + t.Character.Name + "'s target is " + t.TargetCharacter.Name + ".", client);
                }
                GameMain.Server.SendConsoleMessage("The code words are: " + traitorManager.codeWords + ", response: " + traitorManager.codeResponse + ".", client);
            });

            commands.Add(new Command("setpassword|setserverpassword", "setpassword [password]: Changes the password of the server that's being hosted.", (string[] args) =>
            {
                if (GameMain.Server == null || args.Length == 0) return;
                GameMain.Server.SetPassword(args[0]);
            }));

            commands.Add(new Command("restart|reset", "restart/reset: Close and restart the server.", (string[] args) =>
            {
                NewMessage("*****************", Color.Lime);
                NewMessage("RESTARTING SERVER", Color.Lime);
                NewMessage("*****************", Color.Lime);
                GameMain.Instance.CloseServer();
                GameMain.Instance.StartServer();
            }));

            commands.Add(new Command("exit|quit|close", "exit/quit/close: Exit the application.", (string[] args) =>
            {
                GameMain.ShouldRun = false;
            }));

            commands.Add(new Command("say", "say [message]: Send a chat message that displays \"HOST\" as the sender.", (string[] args) =>
            {
                string text = string.Join(" ", args);
                text = "HOST: " + text;
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            }));

            commands.Add(new Command("msg", "msg [message]: Send a chat message with no sender specified.", (string[] args) =>
            {
                string text = string.Join(" ", args);
                GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
            }));

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
                GameMain.Server.RandomizeSeed = !GameMain.Server.RandomizeSeed;
                NewMessage((GameMain.Server.RandomizeSeed ? "Enabled" : "Disabled") + " level seed randomization.", Color.Cyan);
            }));

            commands.Add(new Command("gamemode", "gamemode [name]/[index]: Select the game mode for the next round. The parameter can either be the name or the index number of the game mode (0 = sandbox, 1 = mission, etc).", (string[] args) =>
            {
                int index = -1;
                if (int.TryParse(string.Join(" ", args), out index))
                {
                    if (index > 0 && index < GameMain.NetLobbyScreen.GameModes.Length && 
                        GameMain.NetLobbyScreen.GameModes[index].Name == "Campaign")
                    {
                        MultiPlayerCampaign.StartCampaignSetup();
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.SelectedModeIndex = index;
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
                        GameMain.NetLobbyScreen.SelectedModeName = modeName;
                    }
                }
                NewMessage("Set gamemode to " + GameMain.NetLobbyScreen.SelectedModeName, Color.Cyan);
            },
            () =>
            {
                return new string[][]
                {
                    GameModePreset.list.Select(gm => gm.Name).ToArray()
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
                    Enum.GetValues(typeof(MissionType)).Cast<string>().ToArray()
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

            //"dummy commands" that only exist so that the server can give clients permissions to use them
            commands.Add(new Command("control|controlcharacter", "control [character name]: Start controlling the specified character (client-only).", null));
            commands.Add(new Command("los", "Toggle the line of sight effect on/off (client-only).", null));
            commands.Add(new Command("lighting|lights", "Toggle lighting on/off (client-only).", null));
            commands.Add(new Command("debugdraw", "Toggle the debug drawing mode on/off (client-only).", null));
            commands.Add(new Command("togglehud|hud", "Toggle the character HUD (inventories, icons, buttons, etc) on/off (client-only).", null));
            commands.Add(new Command("followsub", "Toggle whether the camera should follow the nearest submarine (client-only).", null));
            commands.Add(new Command("toggleaitargets|aitargets", "Toggle the visibility of AI targets (= targets that enemies can detect and attack/escape from) (client-only).", null));

#if DEBUG
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
        }        
    }
}
