using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class ServerLog
    {
        const int LinesPerFile = 300;

        const string SavePath = "ServerLogs";

        private string serverName;

        public GUIFrame LogFrame;

        private Queue<ColoredText> lines;

        public ServerLog(string serverName)
        {
            this.serverName = serverName;

            lines = new Queue<ColoredText>();
        }

        public void WriteLine(string line, Color? color)
        {
            string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

            lines.Enqueue(new ColoredText(logLine, color == null ? Color.White : (Color)color));

            if (LogFrame != null) CreateLogFrame();

            if (lines.Count>=LinesPerFile)
            {
                Save();
            }
        }

        public void CreateLogFrame()
        {
            LogFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0,0,400, 400), null, Alignment.Center, GUI.Style, LogFrame);
            innerFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            GUIListBox listBox = new GUIListBox(new Rectangle(0,0,0,355), GUI.Style, innerFrame);

            var currLines = lines.ToList();

            foreach (ColoredText line in currLines)
            {
                var textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 0), line.Text, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, listBox, true, GUI.SmallFont);
                //textBlock.Rect = new Rectangle(textBlock.Rect.X, textBlock.Rect.Y, textBlock.Rect.Width, (line.Text.Count(c => c == '\n') + 1) * 15);

                textBlock.TextColor = line.Color;
                textBlock.CanBeFocused = false;
            }

            listBox.BarScroll = 1.0f;

            GUIButton closeButton = new GUIButton(new Rectangle(0,0,100, 15), "Close", Alignment.BottomRight, GUI.Style, innerFrame);
            closeButton.OnClicked = (GUIButton button, object userData) =>
            {
                LogFrame = null;
                return true;
            };
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

            string fileName = serverName+"_"+DateTime.Now.ToShortDateString()+"_"+DateTime.Now.ToShortTimeString()+".txt";

            fileName = fileName.Replace(":", "");

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
