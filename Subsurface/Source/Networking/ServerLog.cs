using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    class ServerLog
    {
        private int linesPerFile = 800;

        public const string SavePath = "ServerLogs";

        private string serverName;

        public GUIFrame LogFrame;

        private GUIListBox listBox;

        private readonly Queue<ColoredText> lines;

        public int LinesPerFile
        {
            get { return linesPerFile; }
            set { linesPerFile = Math.Max(value, 10); }
        }

        public ServerLog(string serverName)
        {
            this.serverName = serverName;

            lines = new Queue<ColoredText>();
        }

        public void WriteLine(string line, Color? color)
        {
            string logLine = "[" + DateTime.Now.ToLongTimeString() + "] " + line;

            var newText = new ColoredText(logLine, color == null ? Color.White : (Color)color);

            lines.Enqueue(newText);

            if (LogFrame != null)
            {
                AddLine(newText);

                listBox.UpdateScrollBarSize();
            }

            if (lines.Count >= LinesPerFile)
            {
                Save();
                lines.Clear();
            }
        }

        public void CreateLogFrame()
        {
            LogFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0,0,400, 400), null, Alignment.Center, GUI.Style, LogFrame);
            innerFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            new GUITextBlock(new Rectangle(-200,0,100,15), "Filter", GUI.Style, Alignment.TopRight, Alignment.TopRight, innerFrame, false, GUI.SmallFont);

            GUITextBox searchBox = new GUITextBox(new Rectangle(-20,0,180,15), Alignment.TopRight, GUI.Style, innerFrame);
            searchBox.Font = GUI.SmallFont;
            searchBox.OnTextChanged = FilterMessages;
            GUIComponent.KeyboardDispatcher.Subscriber = searchBox;

            var clearButton = new GUIButton(new Rectangle(0,0,15,15), "x", Alignment.TopRight, GUI.Style, innerFrame);
            clearButton.OnClicked = ClearFilter;
            clearButton.UserData = searchBox;

            listBox = new GUIListBox(new Rectangle(0,20,0,335), GUI.Style, innerFrame);

            var currLines = lines.ToList();

            foreach (ColoredText line in currLines)
            {
                AddLine(line);
            }

            listBox.UpdateScrollBarSize();

            if (listBox.BarScroll==0.0f || listBox.BarScroll==1.0f) listBox.BarScroll = 1.0f;

            GUIButton closeButton = new GUIButton(new Rectangle(0,0,100, 15), "Close", Alignment.BottomRight, GUI.Style, innerFrame);
            closeButton.OnClicked = (GUIButton button, object userData) =>
            {
                LogFrame = null;
                return true;
            };
        }

        private void AddLine(ColoredText line)
        {
            float prevSize = listBox.BarSize;

            var textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 0), line.Text, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, null, true, GUI.SmallFont);
            textBlock.Rect = new Rectangle(textBlock.Rect.X, textBlock.Rect.Y, textBlock.Rect.Width, Math.Max(13, textBlock.Rect.Height));

            listBox.AddChild(textBlock);

            textBlock.TextColor = line.Color;
            textBlock.CanBeFocused = false;

            if ((prevSize == 1.0f && listBox.BarScroll == 0.0f) || (prevSize < 1.0f && listBox.BarScroll == 1.0f)) listBox.BarScroll = 1.0f;
        }

        private bool FilterMessages(GUITextBox textBox, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                listBox.children.ForEach( c => c.Visible = true);
                return true;
            }

            text = text.ToLower();

            foreach (GUITextBlock textBlock in listBox.children)
            {
                if (textBlock == null) continue;

                textBlock.Visible = textBlock.Text.ToLower().Contains(text);
            }

            listBox.BarScroll = 0.0f;

            return true;
        }

        public bool ClearFilter(GUIComponent button, object obj)
        {
            FilterMessages(null, "");

            var searchBox = button.UserData as GUITextBox;
            if (searchBox != null) searchBox.Text = "";

            return true;
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

            lines.Clear();
        }
    }
}
