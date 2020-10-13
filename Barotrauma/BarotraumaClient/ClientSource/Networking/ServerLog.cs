using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Extensions;
using NLog;

namespace Barotrauma.Networking
{
    public partial class ServerLog
    {
        private static readonly Logger Log = LogManager.GetLogger("IncomingServerLogger");

        public GUIButton LogFrame;
        private GUIListBox listBox;
        private GUIButton reverseButton;

        private readonly bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

        private string msgFilter;

        private bool reverseOrder = false;

        private readonly Queue<LogMessage> lines = new Queue<LogMessage>();
        private readonly int maxLineHistory = 800;

        private bool OnReverseClicked(GUIButton btn, object obj)
        {
            SetMessageReversal(!reverseOrder);

            return false;
        }

        public void CreateLogFrame()
        {
            LogFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null)
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) LogFrame = null; return true; }
            };

            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, LogFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            new GUIButton(new RectTransform(Vector2.One, LogFrame.RectTransform), "", style: null).OnClicked += (btn, userData) =>
            {
                LogFrame = null;
                return true;
            };

            GUIFrame innerFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.5f), LogFrame.RectTransform, Anchor.Center) { MinSize = new Point(700, 500) });
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), innerFrame.RectTransform, Anchor.Center), style: null);

            // left column ----------------

            var tickBoxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1.0f), paddedFrame.RectTransform, Anchor.BottomLeft));
            int y = 30;
            List<GUITickBox> tickBoxes = new List<GUITickBox>();
            foreach (MessageType msgType in Enum.GetValues(typeof(MessageType)))
            {
                var tickBox = new GUITickBox(new RectTransform(new Point(tickBoxContainer.Rect.Width, 30), tickBoxContainer.RectTransform), TextManager.Get("ServerLog." + messageTypeName[msgType]), font: GUI.SmallFont)
                {
                    Selected = true,
                    TextColor = messageColor[msgType],
                    OnSelected = (GUITickBox tb) =>
                    {
                        msgTypeHidden[(int)msgType] = !tb.Selected;
                        FilterMessages();
                        return true;
                    }
                };
                tickBox.TextBlock.SelectedTextColor = tickBox.TextBlock.TextColor;
                tickBox.Selected = !msgTypeHidden[(int)msgType];
                tickBoxes.Add(tickBox);

                y += 20;
            }

            tickBoxes.Last().TextBlock.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(tickBoxes.Select(t => t.TextBlock), defaultScale: 1.0f);
            };

            // right column ----------------

            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), paddedFrame.RectTransform, Anchor.CenterRight), childAnchor: Anchor.TopRight)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            GUILayoutGroup filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform, Anchor.TopRight),
                isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), filterArea.RectTransform), TextManager.Get("ServerLog.Filter"), 
                font: GUI.SubHeadingFont);            
            GUITextBox searchBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUI.SmallFont, createClearButton: true);
            searchBox.OnTextChanged += (textBox, text) =>
            {
                msgFilter = text;
                FilterMessages();
                return true;
            };
            GUI.KeyboardDispatcher.Subscriber = searchBox;
            filterArea.RectTransform.MinSize = new Point(0, filterArea.RectTransform.Children.Max(c => c.MinSize.Y));

            GUILayoutGroup listBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.95f), rightColumn.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.0f
            };

            reverseButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), listBoxLayout.RectTransform), style: "UIToggleButtonVertical");
            reverseButton.Children.ForEach(c => c.SpriteEffects = reverseOrder ? SpriteEffects.FlipVertically : SpriteEffects.None);
            reverseButton.OnClicked = OnReverseClicked;

            listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.95f), listBoxLayout.RectTransform));

            GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), rightColumn.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (button, userData) =>
                {
                    LogFrame = null;
                    return true;
                }
            };

            rightColumn.Recalculate();

            var currLines = lines.ToList();
            foreach (LogMessage line in currLines)
            {
                AddLine(line);
            }
            FilterMessages();

            listBox.UpdateScrollBarSize();

            if (listBox.BarScroll == 0.0f || listBox.BarScroll == 1.0f) { listBox.BarScroll = 1.0f; }
            
            msgFilter = "";
        }

        public void AssignLogFrame(GUIButton inReverseButton, GUIListBox inListBox, GUIComponent tickBoxContainer, GUITextBox searchBox)
        {
            searchBox.OnTextChanged += (textBox, text) =>
            {
                msgFilter = text;
                FilterMessages();
                return true;
            };

            tickBoxContainer.ClearChildren();

            List<GUITickBox> tickBoxes = new List<GUITickBox>();
            foreach (MessageType msgType in Enum.GetValues(typeof(MessageType)))
            {
                var tickBox = new GUITickBox(new RectTransform(new Point(tickBoxContainer.Rect.Width, (int)(25 * GUI.Scale)), tickBoxContainer.RectTransform), TextManager.Get("ServerLog." + messageTypeName[msgType]), font: GUI.SmallFont)
                {
                    Selected = true,
                    TextColor = messageColor[msgType],
                    OnSelected = (GUITickBox tb) =>
                    {
                        msgTypeHidden[(int)msgType] = !tb.Selected;
                        FilterMessages();
                        return true;
                    }
                };
                tickBox.TextBlock.SelectedTextColor = tickBox.TextBlock.TextColor;
                tickBox.Selected = !msgTypeHidden[(int)msgType];
                tickBoxes.Add(tickBox);
            }
            tickBoxes.Last().TextBlock.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(tickBoxes.Select(t => t.TextBlock), defaultScale: 1.0f);
            };

            inListBox.ClearChildren();
            listBox = inListBox;

            reverseButton = inReverseButton;
            reverseButton.Children.ForEach(c => c.SpriteEffects = reverseOrder ? SpriteEffects.FlipVertically : SpriteEffects.None);
            reverseButton.OnClicked = OnReverseClicked;

            var currLines = lines.ToList();
            foreach (LogMessage line in currLines)
            {
                AddLine(line);
            }
            FilterMessages();

            listBox.UpdateScrollBarSize();
        }

        private void AddLine(LogMessage line)
        {
            float prevSize = listBox.BarSize;

            GUIFrame textContainer = null;

            Anchor anchor = Anchor.TopLeft;
            Pivot pivot = Pivot.TopLeft;
            if (line.RichData != null)
            {
                foreach (var data in line.RichData)
                {
                    UInt64 id = 0;
                    if (!UInt64.TryParse(data.Metadata, out id)) { return; }
                    Client client = GameMain.Client.ConnectedClients.Find(c => c.SteamID == id);
                    client ??= GameMain.Client.ConnectedClients.Find(c => c.ID == id);
                    if (client != null && client.Karma < 40.0f)
                    {
                        textContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), listBox.Content.RectTransform),
                                style: null, color: new Color(0xff111155))
                        {
                            CanBeFocused = false
                        };
                        anchor = Anchor.CenterLeft;
                        pivot = Pivot.CenterLeft;
                        break;
                    }
                }
            }

            var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), (textContainer ?? listBox.Content).RectTransform, anchor, pivot),
                line.RichData, line.SanitizedText, wrap: true, font: GUI.SmallFont)
            {
                TextColor = messageColor[line.Type],
                Visible = !msgTypeHidden[(int)line.Type],
                CanBeFocused = false,
                UserData = line
            };

            if (textContainer != null)
            {
                textContainer.RectTransform.NonScaledSize = new Point(textContainer.RectTransform.NonScaledSize.X, textBlock.RectTransform.NonScaledSize.Y + 5);
                textBlock.SetTextPos();
                textBlock.RectTransform.Resize(textContainer.RectTransform.NonScaledSize);
            }

            if (reverseOrder)
            {
                textBlock.RectTransform.SetAsFirstChild();
            }

            if (line.RichData != null)
            {
                foreach (var data in line.RichData)
                {
                    textBlock.ClickableAreas.Add(new GUITextBlock.ClickableArea()
                    {
                        Data = data,
                        OnClick = (component, area) =>
                        {
                            UInt64 id = 0;
                            if (!UInt64.TryParse(area.Data.Metadata, out id)) { return; }
                            Client client = GameMain.Client.ConnectedClients.Find(c => c.SteamID == id);
                            client ??= GameMain.Client.ConnectedClients.Find(c => c.ID == id);
                            if (client == null) { return; }
                            GameMain.NetLobbyScreen.SelectPlayer(client);
                        }
                    });
                }
            }

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

        private void SetMessageReversal(bool reverse)
        {
            if (reverseOrder == reverse) { return; }

            reverseOrder = reverse;
            reverseButton.Children.ForEach(c => c.SpriteEffects = reverseOrder ? SpriteEffects.FlipVertically : SpriteEffects.None);

            listBox.Content.RectTransform.ReverseChildren();
        }

        public bool ClearFilter(GUIComponent button, object obj)
        {
            var searchBox = button.UserData as GUITextBox;
            if (searchBox != null) searchBox.Text = "";

            msgFilter = "";
            FilterMessages();

            return true;
        }

        public void WriteLine(string line, MessageType messageType)
        {
            var newText = new LogMessage(line, messageType);

            Log.Info("{message:l} ({messageType:l})", newText.SanitizedText, messageType.ToString());

            lines.Enqueue(newText);

            while (lines.Count > maxLineHistory)
            {
                lines.Dequeue();
            }

            if (listBox != null)
            {
                AddLine(newText);

                listBox.UpdateScrollBarSize();
            }

            while (listBox != null && listBox.Content.CountChildren > maxLineHistory)
            {
                listBox.RemoveChild(reverseOrder ? listBox.Content.Children.First() : listBox.Content.Children.Last());
            }
        }

        public void SetServerName(string serverName)
        {
            LoggingUtils.SetIncomingServerName(serverName);
        }
    }
}
