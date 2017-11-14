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
            Chat,
            ItemInteraction,
            Inventory,
            Attack,
            Spawning,
            ServerMessage,
            Error
        }

        private readonly Color[] messageColor =
        {
            Color.LightBlue,
            new Color(255, 142, 0),
            new Color(238, 208, 0),
            new Color(204, 74, 78),
            new Color(163, 73, 164),
            new Color(157, 225, 160),
            Color.Red
        };

        private readonly string[] messageTypeName =
        {
            "Chat message",
            "Item interaction",
            "Inventory usage",
            "Attack & death",
            "Spawning",
            "Server message",
            "Error"
        };

        private int linesPerFile = 800;

        public const string SavePath = "ServerLogs";

        private string serverName;

        private readonly Queue<LogMessage> lines;

        private int unsavedLineCount;

        private string msgFilter;
        private bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

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
            while (listBox != null && listBox.children.Count > LinesPerFile)
            {
                listBox.RemoveChild(listBox.children[0]);
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

            string fileName = serverName + "_" + DateTime.Now.ToShortDateString() + "_" + DateTime.Now.ToShortTimeString() + ".txt";

            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                fileName = fileName.Replace(invalidChar.ToString(), "");
            }

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
    }
}
