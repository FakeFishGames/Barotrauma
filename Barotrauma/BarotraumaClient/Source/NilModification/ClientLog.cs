using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    class ClientLog
    {
        enum LogDataTypes
        {
            String = 0,
            Boolean = 1,
            Short = 2,
            Integar = 3,
            Float = 4
        }

        class logMessageType
        {
            public string DefaultMessage;
            public List<Enum> DataTypes;

            public logMessageType(string message, List<Enum> datatypes, MessageType messageType)
            {
                DefaultMessage = message;
                DataTypes = datatypes;
            }

            public string GetLogText(string[] data)
            {
                string logmessage = DefaultMessage;

                //Generically add in the args
                for (int i = 0; i < data.Length - 1; i++)
                {
                    logmessage.Replace("ARG" + i.ToString(), data[i]);
                }

                return logmessage;
            }
        }

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
            Error,
            NilMod
        }

        private readonly Color[] messageColor =
        {
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
            Color.Red,              //Error
            Color.Violet            //NilMod
        };

        private readonly string[] messageTypeName =
        {
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
            "Error",
            "NilMod Extras"
        };

        private readonly Queue<LogMessage> lines;

        UInt16 CurrentReceivedMessage = 0;

        private string msgFilter;
        private bool[] msgTypeHidden = new bool[Enum.GetValues(typeof(MessageType)).Length];

        int MaxLines = 800;

        public GUIFrame LogFrame;

        private GUIListBox listBox;

        public ClientLog()
        {
            lines = new Queue<LogMessage>();
        }

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
                tickBox.Selected = !msgTypeHidden[(int)msgType];
                tickBox.TextColor = messageColor[(int)msgType];

                tickBox.OnSelected += (GUITickBox tb) =>
                {
                    if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) | PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.LeftControl))
                    {
                        foreach (MessageType checkedMsgType in Enum.GetValues(typeof(MessageType)))
                        {
                            msgTypeHidden[(int)checkedMsgType] = true;
                        }

                        foreach (GUIComponent chkBox in innerFrame.children)
                        {
                            if (chkBox is GUITickBox)
                            {
                                GUITickBox chkBox2 = (GUITickBox)chkBox;
                                chkBox2.Selected = false;
                            }
                        }
                        tickBox.Selected = true;
                        msgTypeHidden[(int)msgType] = false;
                    }
                    else
                    {
                        msgTypeHidden[(int)msgType] = !tb.Selected;
                    }
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

            FilterMessages();
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

            while (lines.Count > MaxLines)
            {
                lines.Dequeue();
            }

#if CLIENT
            while (listBox != null && listBox.children.Count > MaxLines)
            {
                listBox.RemoveChild(listBox.children[0]);
            }
#endif
        }

        //NilMod Clear Log at start of next round : > (By saving everything remaining + the prev. round)
        public void ClearLog()
        {
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

            //Nilmod ClientLog GUI Code
            foreach (GUIComponent chkBox in button.Parent.children)
            {
                if (chkBox is GUITickBox)
                {
                    GUITickBox chkBox2 = (GUITickBox)chkBox;
                    chkBox2.Selected = true;
                }
            }

            foreach (MessageType checkedMsgType in Enum.GetValues(typeof(MessageType)))
            {
                msgTypeHidden[(int)checkedMsgType] = false;
            }

            msgFilter = "";
            FilterMessages();

            return true;
        }
    }
}
