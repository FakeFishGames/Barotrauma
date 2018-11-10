using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerLog
    {
        public GUIButton LogFrame;
        private GUIListBox listBox;

        public void CreateLogFrame()
        {
            LogFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) LogFrame = null; return true; }
            };
            new GUIButton(new RectTransform(Vector2.One, LogFrame.RectTransform), "", style: null).OnClicked += (btn, userData) =>
            {
                LogFrame = null;
                return true;
            };

            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.4f), LogFrame.RectTransform, Anchor.Center) { MinSize = new Point(600, 420) });
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), innerFrame.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, -0.03f) }, style: null);

            new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.05f), paddedFrame.RectTransform, Anchor.TopRight), "Filter", font: GUI.SmallFont);            
            GUITextBox searchBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 0.05f), paddedFrame.RectTransform, Anchor.TopRight), font: GUI.SmallFont);
            searchBox.OnTextChanged += (textBox, text) =>
            {
                msgFilter = text;
                FilterMessages();
                return true;
            };
            GUI.KeyboardDispatcher.Subscriber = searchBox;

            var clearButton = new GUIButton(new RectTransform(new Vector2(0.05f, 0.05f), paddedFrame.RectTransform, Anchor.TopRight), "x")
            {
                OnClicked = ClearFilter,
                UserData = searchBox
            };

            listBox = new GUIListBox(new RectTransform(new Vector2(0.75f, 0.95f), paddedFrame.RectTransform, Anchor.BottomRight));

            var tickBoxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 0.95f), paddedFrame.RectTransform, Anchor.BottomLeft));

            int y = 30;
            foreach (MessageType msgType in Enum.GetValues(typeof(MessageType)))
            {
                var tickBox = new GUITickBox(new RectTransform(new Point(20, 20), tickBoxContainer.RectTransform), messageTypeName[(int)msgType], font: GUI.SmallFont)
                {
                    Selected = true,
                    TextColor = messageColor[(int)msgType]
                };

                tickBox.OnSelected += (GUITickBox tb) =>
                {
                    msgTypeHidden[(int)msgType] = !tb.Selected;
                    FilterMessages();
                    return true;
                };

                tickBox.Selected = !msgTypeHidden[(int)msgType];

                y += 20;
            }

            var currLines = lines.ToList();

            foreach (LogMessage line in currLines)
            {
                AddLine(line);
            }
            FilterMessages();

            listBox.UpdateScrollBarSize();

            if (listBox.BarScroll == 0.0f || listBox.BarScroll == 1.0f) listBox.BarScroll = 1.0f;

            GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), innerFrame.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.02f, 0.03f) }, "Close");
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

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform), 
                line.Text, wrap: true, font: GUI.SmallFont);
            textBlock.TextColor = messageColor[(int)line.Type];
            textBlock.Visible = !msgTypeHidden[(int)line.Type];
            textBlock.CanBeFocused = false;
            textBlock.UserData = line;

            if ((prevSize == 1.0f && listBox.BarScroll == 0.0f) || (prevSize < 1.0f && listBox.BarScroll == 1.0f)) listBox.BarScroll = 1.0f;
        }

        private bool FilterMessages()
        {
            string filter = msgFilter == null ? "" : msgFilter.ToLower();

            foreach (GUIComponent child in listBox.Content.Children)
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
            listBox.UpdateScrollBarSize();
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
