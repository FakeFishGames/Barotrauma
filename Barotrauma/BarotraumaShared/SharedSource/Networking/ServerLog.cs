using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    public partial class ServerLog
    {
        private struct LogMessage
        {
            public readonly RichString Text;
            public readonly MessageType Type;

            public LogMessage(string text, MessageType type)
            {
                if (type.HasFlag(MessageType.Chat))
                {
                    text = $"[{DateTime.Now}]\n  {text}";
                }
                else
                {
                    text = $"[{DateTime.Now}]\n  {TextManager.GetServerMessage(text)}";
                }
                Text = RichString.Rich(text);
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
            Wiring,
            ServerMessage,
            ConsoleUsage,
            Karma,
            Talent,
            Error,
        }

        private readonly Dictionary<MessageType, Color> messageColor = new Dictionary<MessageType, Color>
        {
            { MessageType.Chat, Color.LightBlue },
            { MessageType.ItemInteraction, new Color(205, 205, 180) },
            { MessageType.Inventory, new Color(255, 234, 85) },
            { MessageType.Attack, new Color(204, 74, 78) },
            { MessageType.Spawning, new Color(163, 73, 164) },
            { MessageType.Wiring, new Color(255, 157, 85) },
            { MessageType.ServerMessage, new Color(157, 225, 160) },
            { MessageType.ConsoleUsage, new Color(0, 162, 232) },
            { MessageType.Karma, new Color(75, 88, 255) },
            { MessageType.Talent, new Color(125, 125, 255) },
            { MessageType.Error, Color.Red },
        };

        private readonly Dictionary<MessageType, string> messageTypeName = new Dictionary<MessageType, string>
        {
            { MessageType.Chat, "ChatMessage" },
            { MessageType.ItemInteraction, "ItemInteraction" },
            { MessageType.Inventory, "InventoryUsage" },
            { MessageType.Attack, "AttackDeath" },
            { MessageType.Spawning, "Spawning" },
            { MessageType.Wiring, "Wiring" },
            { MessageType.ServerMessage, "ServerMessage" },
            { MessageType.ConsoleUsage, "ConsoleUsage" },
            { MessageType.Karma, "Karma" },
            { MessageType.Talent, "Talent" },
            { MessageType.Error, "Error" }
        };

        private int linesPerFile = 800;

        public const string SavePath = "ServerLogs";
        
        private readonly Queue<LogMessage> lines;
        private readonly Queue<LogMessage> unsavedLines;

        private readonly bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

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
            unsavedLines = new Queue<LogMessage>();

            foreach (MessageType messageType in Enum.GetValues(typeof(MessageType)))
            {
                System.Diagnostics.Debug.Assert(messageColor.ContainsKey(messageType));
                System.Diagnostics.Debug.Assert(messageTypeName.ContainsKey(messageType));
            }
        }

        public void WriteLine(string line, MessageType messageType)
        {
            //string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

            var newText = new LogMessage(line, messageType);

#if SERVER
            DebugConsole.NewMessage(newText.Text.SanitizedValue, messageColor[messageType]); //TODO: REMOVE
#endif

            lines.Enqueue(newText);
            unsavedLines.Enqueue(newText);

#if CLIENT
            if (listBox != null)
            {
                AddLine(newText);
                listBox.UpdateScrollBarSize();
            }
#endif
            if (unsavedLines.Count() >= LinesPerFile)
            {
                Save();
                unsavedLines.Clear();
            }

            while (lines.Count > LinesPerFile)
            {
                lines.Dequeue();
            }

#if CLIENT
            while (listBox != null && listBox.Content.CountChildren > LinesPerFile)
            {
                listBox.RemoveChild(reverseOrder ? listBox.Content.Children.First() : listBox.Content.Children.Last());
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

            string fileName = ServerName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH:mm");
            fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

            string filePath = Path.Combine(SavePath, fileName + ".txt");
            int i = 2;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(SavePath, fileName + " (" + i + ").txt");
                i++;
            }

            try
            {
                File.WriteAllLines(filePath, unsavedLines.Select(l => l.Text.SanitizedValue));
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the server log to " + filePath + " failed", e);
                return;
            }
        }
    }
}
