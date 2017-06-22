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
                    string text = "";
                    for (int i=1;i<commands.Count();i++)
                    {
                        if (!string.IsNullOrEmpty(text)) text += " ";
                        text += commands[i];
                    }
                    if (commands[0].ToLower() == "say") text = "HOST: " + text;
                    GameMain.Server.SendChatMessage(text, ChatMessageType.Server);
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
