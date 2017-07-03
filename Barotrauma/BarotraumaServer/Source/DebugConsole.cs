using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        public static List<string> QueuedCommands = new List<string>();

        public static void Update()
        {
            lock (QueuedCommands)
            {
                while (QueuedCommands.Count>0)
                {
                    ExecuteCommand(QueuedCommands[0], GameMain.Instance);
                    QueuedCommands.RemoveAt(0);
                }
            }
        }

        private static bool ExecProjSpecific(string[] commands)
        {
            switch (commands[0].ToLower())
            {
                case "help":

                    NewMessage("start: start a new round", Color.Cyan);
                    NewMessage("end: end the current round", Color.Cyan);
                    NewMessage("restart: restart the server", Color.Cyan);
                    NewMessage("quit: exit the game", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("say [chat message]: send a chat message", Color.Cyan);
                    NewMessage("clientlist: list the names and IPs of the connected clients", Color.Cyan);
                    NewMessage("kick [name]: kick a player out from the server", Color.Cyan);
                    NewMessage("ban [name]: kick and ban the player from the server", Color.Cyan);
                    NewMessage("banip [IP address]: ban the IP address from the server", Color.Cyan);
                    NewMessage("debugdraw: toggles the \"debug draw mode\"", Color.Cyan);
                    NewMessage("netstats: toggles the visibility of the network statistics panel", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("servername [name]: change the name of the server", Color.Cyan);
                    NewMessage("servermsg [message]: change the message in the server lobby", Color.Cyan);
                    NewMessage("seed [seed]: changes the level seed for the next round", Color.Cyan);
                    NewMessage("gamemode [name]: select the specified game mode for the next round", Color.Cyan);
                    NewMessage("gamemode [index]: select the specified game mode (0 = sandbox, 1 = mission, etc)", Color.Cyan);
                    NewMessage("submarine [name]: select the specified game mode for the next round", Color.Cyan);
                    NewMessage("shuttle [name]: select the specified submarine as the respawn shuttle for the next round", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("spawn [creaturename] [near/inside/outside]: spawn a creature at a random spawnpoint (use the second parameter to only select spawnpoints near/inside/outside the submarine)", Color.Cyan);
                    
                    NewMessage(" ", Color.Cyan);
                    
                    NewMessage("heal [character name]: restore the specified character to full health", Color.Cyan);
                    NewMessage("revive [character name]: bring the specified character back from the dead", Color.Cyan);
                    NewMessage("killmonsters: immediately kills all AI-controlled enemies in the level", Color.Cyan);

                    NewMessage(" ", Color.Cyan);

                    NewMessage("fixwalls: fixes all the walls", Color.Cyan);
                    NewMessage("fixitems: fixes every item/device in the sub", Color.Cyan);
                    NewMessage("oxygen: replenishes the oxygen in every room to 100%", Color.Cyan);
                    NewMessage("power [amount]: immediately sets the temperature of the reactor to the specified value", Color.Cyan);
                    
                    break;
                case "restart":
                case "reset":
                    NewMessage("*****************", Color.Lime);
                    NewMessage("RESTARTING SERVER", Color.Lime);
                    NewMessage("*****************", Color.Lime);
                    GameMain.Instance.CloseServer();
                    GameMain.Instance.StartServer();
                    break;
                case "exit":
                case "close":
                case "quit":
                    GameMain.ShouldRun = false;
                    break;
                case "say":
                case "msg":
                    string text = string.Join(" ", commands.Skip(1));
                    if (commands[0].ToLower() == "say") text = "HOST: " + text;
                    GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
                    break;
                case "servername":
                    GameMain.Server.Name = string.Join(" ", commands.Skip(1));
                    GameMain.NetLobbyScreen.ChangeServerName(string.Join(" ", commands.Skip(1)));
                    break;
                case "servermsg":
                    GameMain.NetLobbyScreen.ChangeServerMessage(string.Join(" ", commands.Skip(1)));
                    break;
                case "seed":
                    GameMain.NetLobbyScreen.LevelSeed = string.Join(" ", commands.Skip(1));
                    break;
                case "gamemode":
                    {
                        int index = -1;
                        if (int.TryParse(string.Join(" ", commands.Skip(1)), out index))
                        {
                            GameMain.NetLobbyScreen.SelectedModeIndex = index;
                        }
                        else
                        {
                            GameMain.NetLobbyScreen.SelectedModeName = string.Join(" ", commands.Skip(1));
                        }
                        NewMessage("Set gamemode to " + GameMain.NetLobbyScreen.SelectedModeName, Color.Cyan);
                    }
                    break;
                case "mission":
                    {
                        int index = -1;
                        if (int.TryParse(string.Join(" ", commands.Skip(1)), out index))
                        {
                            GameMain.NetLobbyScreen.MissionTypeIndex = index;
                        }
                        else
                        {
                            GameMain.NetLobbyScreen.MissionTypeName = string.Join(" ", commands.Skip(1));
                        }
                        NewMessage("Set mission to " + GameMain.NetLobbyScreen.MissionTypeName, Color.Cyan);
                    }
                    break;
                case "sub":
                case "submarine":
                    {
                        Submarine sub = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.ToLower() == string.Join(" ", commands.Skip(1)).ToLower());

                        if (sub != null)
                        {
                            GameMain.NetLobbyScreen.SelectedSub = sub;
                        }
                        sub = GameMain.NetLobbyScreen.SelectedSub;
                        NewMessage("Selected sub: " + sub.Name + (sub.HasTag(SubmarineTag.Shuttle) ? " (shuttle)" : ""), Color.Cyan);
                    }
                    break;
                case "shuttle":
                    {
                        Submarine shuttle = GameMain.NetLobbyScreen.GetSubList().Find(s => s.Name.ToLower() == string.Join(" ", commands.Skip(1)).ToLower());

                        if (shuttle != null)
                        {
                            GameMain.NetLobbyScreen.SelectedShuttle = shuttle;
                        }
                        shuttle = GameMain.NetLobbyScreen.SelectedShuttle;
                        NewMessage("Selected shuttle: " + shuttle.Name + (shuttle.HasTag(SubmarineTag.Shuttle) ? "" : " (not shuttle)"), Color.Cyan);
                    }
                    break;
                case "startgame":
                case "startround":
                case "start":
                    if (Screen.Selected == GameMain.GameScreen) break;
                    if (!GameMain.Server.StartGame()) NewMessage("Failed to start a new round", Color.Yellow);
                    break;
                case "endgame":
                case "endround":
                case "end":
                    if (Screen.Selected == GameMain.NetLobbyScreen) break;
                    GameMain.Server.EndGame();
                    break;
                case "entitydata":
                    Entity ent = Entity.FindEntityByID(Convert.ToUInt16(commands[1]));
                    if (ent != null)
                    {
                        NewMessage(ent.ToString(), Color.Lime);
                    }
                    break;
#if DEBUG
                case "eventdata":
                    ServerEntityEvent ev = GameMain.Server.EntityEventManager.Events[Convert.ToUInt16(commands[1])];
                    if (ev != null)
                    {
                        NewMessage(ev.StackTrace, Color.Lime);
                    }
                    break;
#endif
                default:
                    return false;
            }
            return true; //command found
        }
    }
}
