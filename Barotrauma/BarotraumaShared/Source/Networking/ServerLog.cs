using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    public partial class ServerLog
    {
        private struct LogMessage
        {
            public readonly string Text;
            public readonly MessageType Type;

            public LogMessage(string text, MessageType type)
            {
                if (type.HasFlag(MessageType.Chat))
                {
                    Text = $"[{DateTime.Now.ToString()}]\n  {text}";
                }
                else
                {
                    Text = $"[{DateTime.Now.ToString()}]\n  {TextManager.GetServerMessage(text)}";
                }

                Type = type;
            }
        }

        public enum MessageType
        {
            Chat,
            ItemInteraction,
            Inventory,
            Attack,
            Spawning,
            ServerMessage,
            ConsoleUsage,
            Error
        }

        private readonly Color[] messageColor =
        {
            Color.LightBlue,            //Chat
            new Color(255, 142, 0),     //ItemInteraction
            new Color(238, 208, 0),     //Inventory
            new Color(204, 74, 78),     //Attack
            new Color(163, 73, 164),    //Spawning
            new Color(157, 225, 160),   //ServerMessage
            new Color(0, 162, 232),     //ConsoleUsage
            Color.Red                   //Error
        };

        private readonly string[] messageTypeName =
        {
            "ChatMessage",
            "ItemInteraction",
            "InventoryUsage",
            "AttackDeath",
            "Spawning",
            "ServerMessage",
            "ConsoleUsage",
            "Error"
        };

        private int linesPerFile = 800;

        public const string SavePath = "ServerLogs";
        
        private readonly Queue<LogMessage> lines;

        private int unsavedLineCount;

        private bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

        public int LinesPerFile
        {
            get { return linesPerFile; }
            set { linesPerFile = Math.Max(value, 10); }
        }

        public string ServerName;

        public ServerLog(string serverName)
        {
            ServerName = serverName;
            lines = new Queue<LogMessage>();
        }

        public void WriteLine(string line, MessageType messageType)
        {
            //string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

#if SERVER
            DebugConsole.NewMessage(line, messageColor[(int)messageType]); //TODO: REMOVE
#endif

            var newText = new LogMessage(line, messageType);
            
            lines.Enqueue(newText);

#if CLIENT
            if (listBox != null)
            {
                AddLine(newText);

                listBox.UpdateScrollBarSize();
            }
#endif
            
            unsavedLineCount++;

            if (unsavedLineCount >= LinesPerFile)
            {
                Save();
                unsavedLineCount = 0;
            }

            while (lines.Count > LinesPerFile)
            {
                lines.Dequeue();
            }

#if CLIENT
            while (listBox != null && listBox.Content.CountChildren > LinesPerFile)
            {
                listBox.RemoveChild(listBox.Content.Children.First());
            }
#endif
        }

        public void Save()
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

            string fileName = ServerName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm") + ".txt";
            fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

            string filePath = Path.Combine(SavePath, fileName);

            try
            {
                File.WriteAllLines(filePath, lines.Select(l => l.Text));
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
                return;
            }
        }
    }
}
