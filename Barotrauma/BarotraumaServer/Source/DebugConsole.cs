using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private static void InitProjectSpecific()
        {
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

            commands.Add(new Command("gamemode", "gamemode [name]/[index]: Select the game mode for the next round. The parameter can either be the name or the index number of the game mode (0 = sandbox, 1 = mission, etc).", (string[] args) =>
            {
                int index = -1;
                if (int.TryParse(string.Join(" ", args), out index))
                {
                    if (index > 0 && index < GameMain.NetLobbyScreen.GameModes.Length && 
                        GameMain.NetLobbyScreen.GameModes[index].Name == "Campaign")
                    {
                        MultiplayerCampaign.StartCampaignSetup();
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
                        MultiplayerCampaign.StartCampaignSetup();
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.SelectedModeName = modeName;
                    }
                }
                NewMessage("Set gamemode to " + GameMain.NetLobbyScreen.SelectedModeName, Color.Cyan);
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

            commands.Add(new Command("autorestart", "autorestart: Toggle automatic round restarting on/off.", (string[] args) =>
            {
                if (GameMain.Server == null) return;

                GameMain.Server.AutoRestart = !GameMain.Server.AutoRestart;
                NewMessage(GameMain.Server.AutoRestart ? "Automatic restart enabled." : "Automatic restart disabled.", Color.White);
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
