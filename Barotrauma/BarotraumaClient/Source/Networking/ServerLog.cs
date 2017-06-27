using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerLog
    {
        public GUIFrame LogFrame;

        private GUIListBox listBox;

        public void CreateLogFrame()
        {
            LogFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, 600, 420), null, Alignment.Center, "", LogFrame);
            innerFrame.Padding = new Vector4(10.0f, 20.0f, 10.0f, 20.0f);

            new GUITextBlock(new Rectangle(-200, 0, 100, 15), "Filter", "", Alignment.TopRight, Alignment.CenterRight, innerFrame, false, GUI.SmallFont);

            GUITextBox searchBox = new GUITextBox(new Rectangle(-20, 0, 180, 15), Alignment.TopRight, "", innerFrame);
            searchBox.Font = GUI.SmallFont;
            searchBox.OnTextChanged = (textBox, text) =>
            {
                msgFilter = text;
                FilterMessages();
                return true;
            };
            GUIComponent.KeyboardDispatcher.Subscriber = searchBox;

            var clearButton = new GUIButton(new Rectangle(0, 0, 15, 15), "x", Alignment.TopRight, "", innerFrame);
            clearButton.OnClicked = ClearFilter;
            clearButton.UserData = searchBox;

            listBox = new GUIListBox(new Rectangle(0, 30, 450, 340), "", Alignment.TopRight, innerFrame);

            int y = 30;
            foreach (MessageType msgType in Enum.GetValues(typeof(MessageType)))
            {
                var tickBox = new GUITickBox(new Rectangle(0, y, 20, 20), messageTypeName[(int)msgType], Alignment.TopLeft, GUI.SmallFont, innerFrame);
                tickBox.Selected = true;
                tickBox.TextColor = messageColor[(int)msgType];

                tickBox.OnSelected += (GUITickBox tb) =>
                {
                    msgTypeHidden[(int)msgType] = !tb.Selected;
                    FilterMessages();
                    return true;
                };

                y += 20;
            }

            var currLines = lines.ToList();

            foreach (LogMessage line in currLines)
            {
                AddLine(line);
            }

            listBox.UpdateScrollBarSize();

            if (listBox.BarScroll == 0.0f || listBox.BarScroll == 1.0f) listBox.BarScroll = 1.0f;

            GUIButton closeButton = new GUIButton(new Rectangle(-100, 10, 100, 15), "Close", Alignment.BottomRight, "", innerFrame);
            closeButton.OnClicked = (button, userData) =>
            {
                LogFrame = null;
                return true;
            };

            msgFilter = "";
        }

        private void AddLine(LogMessage line)
        {
            float prevSize = listBox.BarSize;

            var textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 0), line.Text, "", Alignment.TopLeft, Alignment.TopLeft, listBox, true, GUI.SmallFont);
            textBlock.Rect = new Rectangle(textBlock.Rect.X, textBlock.Rect.Y, textBlock.Rect.Width, Math.Max(13, textBlock.Rect.Height));
            textBlock.TextColor = messageColor[(int)line.Type];
            textBlock.CanBeFocused = false;
            textBlock.UserData = line;

            if ((prevSize == 1.0f && listBox.BarScroll == 0.0f) || (prevSize < 1.0f && listBox.BarScroll == 1.0f)) listBox.BarScroll = 1.0f;
        }

        private bool FilterMessages()
        {
            string filter = msgFilter == null ? "" : msgFilter.ToLower();

            foreach (GUIComponent child in listBox.children)
            {
                var textBlock = child as GUITextBlock;
                if (textBlock == null) continue;

                child.Visible = true;

                if (msgTypeHidden[(int)((LogMessage)child.UserData).Type])
                {
                    child.Visible = false;
                    continue;
                }

                textBlock.Visible = string.IsNullOrEmpty(filter) || textBlock.Text.ToLower().Contains(filter);
            }

            listBox.BarScroll = 0.0f;

            return true;
        }

        public bool ClearFilter(GUIComponent button, object obj)
        {
            var searchBox = button.UserData as GUITextBox;
            if (searchBox != null) searchBox.Text = "";

            msgFilter = "";
            FilterMessages();

            return true;
        }

    }
}
