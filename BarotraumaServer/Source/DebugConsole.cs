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
                case "restart":
                case "reset":
                    DebugConsole.NewMessage("*****************", Color.Lime);
                    DebugConsole.NewMessage("RESTARTING SERVER", Color.Lime);
                    DebugConsole.NewMessage("*****************", Color.Lime);
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
                        DebugConsole.NewMessage("Set gamemode to " + GameMain.NetLobbyScreen.SelectedModeName, Color.Cyan);
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
                        DebugConsole.NewMessage("Set mission to " + GameMain.NetLobbyScreen.MissionTypeName, Color.Cyan);
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
                        DebugConsole.NewMessage("Selected sub: " + sub.Name + (sub.HasTag(SubmarineTag.Shuttle) ? " (shuttle)" : ""), Color.Cyan);
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
                        DebugConsole.NewMessage("Selected shuttle: " + shuttle.Name + (shuttle.HasTag(SubmarineTag.Shuttle) ? "" : " (not shuttle)"), Color.Cyan);
                    }
                    break;
                case "startgame":
                case "startround":
                case "start":
                    if (Screen.Selected == GameMain.GameScreen) break;
                    if (!GameMain.Server.StartGame()) NewMessage("Failed to start server",Color.Yellow);
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
                case "eventdata":
                    ServerEntityEvent ev = GameMain.Server.EntityEventManager.Events[Convert.ToUInt16(commands[1])];
                    if (ev != null)
                    {
                        NewMessage(ev.StackTrace, Color.Lime);
                    }
                    break;
                default:
                    return false;
                    break;
            }
            return true; //command found
        }
    }
}
