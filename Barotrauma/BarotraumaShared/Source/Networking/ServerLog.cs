using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerLog
    {
        private struct LogMessage
        {
            public readonly string Text;
            public readonly MessageType Type;

            public LogMessage(string text, MessageType type)
            {
                Text = "[" + DateTime.Now.ToString() + "] " + text;
                Type = type;
            }
        }

        public enum MessageType
        {
            /* Old Chat Types
            Chat,
            ItemInteraction,
            Inventory,
            Attack,
            Spawning,
            ServerMessage,
            ConsoleUsage,
            Error
            */

            Chat,
            Doorinteraction,
            ItemInteraction,
            Inventory,
            Attack,
            Husk,
            Reactor,
            Set,
            Rewire,
            Spawns,
            Connection,
            ServerMessage,
            ConsoleUsage,
            Error,
            NilMod
        }

        private readonly Color[] messageColor =
        {
            /* Old colours
            Color.LightBlue,            //Chat
            new Color(255, 142, 0),     //ItemInteraction
            new Color(238, 208, 0),     //Inventory
            new Color(204, 74, 78),     //Attack
            new Color(163, 73, 164),    //Spawning
            new Color(157, 225, 160),   //ServerMessage
            new Color(0, 162, 232),     //ConsoleUsage
            Color.Red                   //Error
            */

            Color.LightCoral,       //Chat
            Color.White,            //DoorInteraction
            Color.Orange,           //ItemInteraction
            Color.Yellow,           //Inventory
            Color.Red,              //Attack
            Color.MediumPurple,     //Husk
            Color.MediumSeaGreen,   //Reactor
            Color.ForestGreen,      //Set
            Color.LightPink,        //Rewire
            Color.DarkMagenta,      //Spawns
            Color.DarkCyan,         //Connection
            Color.Cyan,             //ServerMessage
            Color.Aquamarine,       //ConsoleUsage
            Color.Red,              //Error
            Color.Violet            //NilMod
        };

        private readonly string[] messageTypeName =
        {
            /* Old Message Type Names
            "Chat message",
            "Item interaction",
            "Inventory usage",
            "Attack & death",
            "Spawning",
            "Server message",
            "Console usage",
            "Error"
            */

            "Chat message",
            "Door interaction",
            "Item interaction",
            "Inventory usage",
            "Attack & death",
            "Husk Infection",
            "Reactor",
            "Powered/Pump set",
            "Wiring",
            "Spawning",
            "Connection Info",
            "Server message",
            "Console usage",
            "Error",
            "NilMod Extras"
        };

        private int linesPerFile = 800;

        public const string SavePath = "ServerLogs";

        private string serverName;

        private readonly Queue<LogMessage> lines;

        private int unsavedLineCount;

        private string msgFilter;
        private bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

        StreamWriter sw;

        public int LinesPerFile
        {
            get { return linesPerFile; }
            set { linesPerFile = Math.Max(value, 10); }
        }

        public ServerLog(string serverName)
        {
            this.serverName = serverName;

            lines = new Queue<LogMessage>();
        }

        public void WriteLine(string line, MessageType messageType)
        {
            //string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

#if SERVER
            DebugConsole.NewMessage(line, Color.White); //TODO: REMOVE
#endif

            var newText = new LogMessage(line, messageType);
            
            lines.Enqueue(newText);

#if CLIENT
            if (LogFrame != null)
            {
                AddLine(newText);

                listBox.UpdateScrollBarSize();
            }
#endif
            
            unsavedLineCount++;

            if(GameMain.NilMod.LogAppendCurrentRound)
            {
                if (unsavedLineCount >= GameMain.NilMod.LogAppendLineSaveRate)
                {
                    Save();
                    //unsavedLineCount = 0;
                }
            }
            else
            {
                if (unsavedLineCount >= LinesPerFile)
                {
                    Save();
                    unsavedLineCount = 0;
                }
            }
            

            while (lines.Count > LinesPerFile)
            {
                lines.Dequeue();
            }

#if CLIENT
            while (listBox != null && listBox.children.Count > LinesPerFile)
            {
                listBox.RemoveChild(listBox.children[0]);
            }
#endif
        }

        public void Save()
        {
            if (unsavedLineCount > 0)
            {
                if (!Directory.Exists(SavePath))
                {
                    try
                    {
                        Directory.CreateDirectory(SavePath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to create a folder for server logs", e);
                        return;
                    }
                }

                //Append current round file method
                if (GameMain.NilMod.LogAppendCurrentRound)
                {
                    //Get the filename
                    if (GameMain.NilMod.RoundSaveName == "")
                    {
                        GameMain.NilMod.RoundSaveName = serverName + "_" + DateTime.Now.ToShortDateString() + "_" + DateTime.Now.ToShortTimeString() + ".txt";

                        GameMain.NilMod.RoundSaveName = GameMain.NilMod.RoundSaveName.Replace(":", "");
                        GameMain.NilMod.RoundSaveName = GameMain.NilMod.RoundSaveName.Replace("../", "");
                        GameMain.NilMod.RoundSaveName = GameMain.NilMod.RoundSaveName.Replace("/", "");

                        string filePath = Path.Combine(SavePath, GameMain.NilMod.RoundSaveName);

                        //Just write all lines we currently have in our log at the moment
                        try
                        {
                            File.WriteAllLines(filePath, lines.Select(l => l.Text));
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
                        }
                    }
                    //Append our newest lines
                    else
                    {
                        string filePath = Path.Combine(SavePath, GameMain.NilMod.RoundSaveName);

                        try
                        {
                            //This method is apparently faster and performs considerably better
                            //The last number is the buffer size, 65536 is 64kb, default is 4kb, higher may have worse performance
                            sw = new StreamWriter(filePath, true, System.Text.Encoding.UTF8, 65536);

                            for (int i = (lines.Count() - unsavedLineCount); i < lines.Count(); i++)
                            {
                                sw.WriteLine(lines.ElementAt(i).Text);
                            }

                            sw.Close();
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
                        }
                    }
                }
                //Default Saving Methods
                else
                {
                    string fileName = serverName + "_" + DateTime.Now.ToShortDateString() + "_" + DateTime.Now.ToShortTimeString() + ".txt";

                    fileName = fileName.Replace(":", "");
                    fileName = fileName.Replace("../", "");
                    fileName = fileName.Replace("/", "");

                    string filePath = Path.Combine(SavePath, fileName);

                    try
                    {
                        File.WriteAllLines(filePath, lines.Select(l => l.Text));
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
                    }
                }
                unsavedLineCount = 0;
            }
            else
            {
                DebugConsole.NewMessage("NILMOD WARNING - Attempt to save server log with no messages to save", Color.Cyan);
            }
        }

        //NilMod Clear Log at start of next round : > (By saving everything remaining + the prev. round)
        public void ClearLog()
        {
            if (unsavedLineCount >= 0)
            {
                Save();
                unsavedLineCount = 0;
            }

            while (lines.Count > 0)
            {
                lines.Dequeue();
            }

#if CLIENT
            while (listBox != null && listBox.children.Count > 0)
            {
                listBox.RemoveChild(listBox.children[0]);
            }
#endif
            GameMain.NilMod.RoundSaveName = "";
        }
    }
}
