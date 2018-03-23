using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    class ChatBox
    {
        private static Sprite radioIcon;

        private GUIListBox chatBox;
        private GUITextBox inputBox;

        private GUIButton radioButton;

        private bool isSinglePlayer;

        public GUITextBox.OnEnterHandler OnEnterMessage
        {
            get
            {
                if (isSinglePlayer)
                {
                    DebugConsole.ThrowError("Cannot access chat input box in single player!\n" + Environment.StackTrace);
                    return null;
                }
                return inputBox.OnEnterPressed;
            }
            set
            {
                if (isSinglePlayer)
                {
                    DebugConsole.ThrowError("Cannot access chat input box in single player!\n" + Environment.StackTrace);
                    return;
                }
                inputBox.OnEnterPressed = value;
            }
        }

        public GUITextBox.OnTextChangedHandler OnTextChanged
        {
            get
            {
                if (isSinglePlayer)
                {
                    DebugConsole.ThrowError("Cannot access chat input box in single player!\n" + Environment.StackTrace);
                    return null;
                }
                return inputBox.OnTextChanged;
            }
            set
            {
                if (isSinglePlayer)
                {
                    DebugConsole.ThrowError("Cannot access chat input box in single player!\n" + Environment.StackTrace);
                    return;
                }
                inputBox.OnTextChanged = value;
            }
        }

        public GUIButton RadioButton
        {
            get { return radioButton; }
        }

        public GUITextBox InputBox
        {
            get { return inputBox; }
        }

        public ChatBox(GUIComponent parent, bool isSinglePlayer)
        {
            this.isSinglePlayer = isSinglePlayer;
            if (radioIcon == null)
            {
                radioIcon = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(527, 952, 38, 52), null);
                radioIcon.Origin = radioIcon.size / 2;
            }

            int width = (int)(330 * GUI.Scale);
            int height = (int)(400 * GUI.Scale);
            chatBox = new GUIListBox(
                new Rectangle(GameMain.GraphicsWidth - 10 - width, 60 + (int)(90 * GUI.Scale - parent.Padding.Y - parent.Rect.Y), width, height),
                Color.White * 0.5f, "ChatBox", parent);
            chatBox.Padding = Vector4.Zero;

            if (isSinglePlayer)
            {
                radioButton = new GUIButton(
                    new Rectangle(chatBox.Rect.Center.X - (int)(radioIcon.size.Y / 2), chatBox.Rect.Bottom - (int)parent.Padding.Y - parent.Rect.Y, (int)radioIcon.size.X, (int)radioIcon.size.Y), 
                    "", Alignment.TopLeft, null, parent);
                new GUIImage(Rectangle.Empty, radioIcon, Alignment.Center, radioButton);
                radioButton.OnClicked = (GUIButton btn, object userData) =>
                {
                    GameMain.GameSession.CrewManager.CrewCommander.ToggleGUIFrame();
                    return true;
                };
            }
            else
            {
                inputBox = new GUITextBox(
                    new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 10, chatBox.Rect.Width, 25),
                    Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, "ChatTextBox", parent);
                inputBox.children[0].Padding = new Vector4(30, 0, 10, 0);
                inputBox.Font = GUI.SmallFont;
                inputBox.MaxTextLength = ChatMessage.MaxLength;
                inputBox.Padding = Vector4.Zero;

                radioButton = new GUIButton(new Rectangle(-15, 0, (int)radioIcon.size.X, (int)radioIcon.size.Y), "", Alignment.CenterLeft, null, inputBox);
                radioButton.ClampMouseRectToParent = false;
                new GUIImage(Rectangle.Empty, radioIcon, Alignment.Center, radioButton);
                radioButton.OnClicked = (GUIButton btn, object userData) =>
                {
                    if (inputBox.Selected)
                    {
                        inputBox.Text = "";
                        inputBox.Deselect();
                    }
                    else
                    {
                        inputBox.Select();
                        var radioItem = Character.Controlled?.Inventory?.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
                        if (radioItem != null && Character.Controlled.HasEquippedItem(radioItem) && radioItem.GetComponent<WifiComponent>().CanTransmit())
                        {
                            inputBox.Text = "r; ";
                            inputBox.OnTextChanged?.Invoke(inputBox, inputBox.Text);
                        }
                    }
                    return true;
                };
            }

        }

        public void AddMessage(ChatMessage message)
        {
            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[0]);
            }

            string displayedText = message.Text;
            string senderName = "";
            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                senderName = (message.Type == ChatMessageType.Private ? "[PM] " : "") + message.SenderName;
            }

            GUITextBlock senderText = null;
            if (!string.IsNullOrEmpty(senderName))
            {
                senderText = new GUITextBlock(new Rectangle(0, 0, chatBox.Rect.Width - 15, 0), senderName,
                    ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, Color.White,
                    Alignment.Left, Alignment.TopLeft, "", null, true, GUI.SmallFont);
                senderText.CanBeFocused = false;
                senderText.Padding = new Vector4(10, 0, 0, 0);
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, chatBox.Rect.Width - 15, 0), displayedText,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, message.Color,
                Alignment.Left, Alignment.TopLeft, "", null, true, GUI.SmallFont);
            msg.UserData = message.SenderName;
            msg.CanBeFocused = false;
            msg.Padding = new Vector4(20.0f, 0, 0, 0);

            float prevSize = chatBox.BarSize;

            msg.Padding = new Vector4(20, 0, 0, 0);
            msg.Rect = new Rectangle(msg.Rect.X, msg.Rect.Y, msg.Rect.Width, (int)GUI.SmallFont.MeasureString(msg.WrappedText).Y + 5);
            if (senderText != null) chatBox.AddChild(senderText);
            chatBox.AddChild(msg);
            
            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;

            GUISoundType soundType = GUISoundType.Message;
            if (message.Type == ChatMessageType.Radio)
            {
                soundType = GUISoundType.RadioMessage;
            }
            else if (message.Type == ChatMessageType.Dead)
            {
                soundType = GUISoundType.DeadMessage;
            }

            GUI.PlayUISound(soundType);
        }
    }
}
