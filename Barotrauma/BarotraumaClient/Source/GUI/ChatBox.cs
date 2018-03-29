using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class ChatBox
    {
        const float HideDelay = 5.0f;

        private static Sprite radioIcon;

        private Point defaultPos;

        private GUIFrame guiFrame;

        private GUIListBox chatBox;
        private GUITextBox inputBox;

        private GUIButton radioButton;

        private bool isSinglePlayer;

        private float hideTimer;

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
            int height = (int)(440 * GUI.Scale);
            guiFrame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth - 10 - width, 90 + (int)(120 * GUI.Scale - parent.Padding.Y - parent.Rect.Y), width, height), null, parent);
            chatBox = new GUIListBox(new Rectangle(0, 0, 0, guiFrame.Rect.Height - 35), Color.White * 0.5f, "ChatBox", guiFrame);
            chatBox.Padding = Vector4.Zero;

            defaultPos = guiFrame.Rect.Location;

            if (isSinglePlayer)
            {
                radioButton = new GUIButton(
                    new Rectangle(0, (int)radioIcon.size.Y - 40, (int)radioIcon.size.X, (int)radioIcon.size.Y), 
                    "", Alignment.BottomRight, null, guiFrame);
                radioButton.ClampMouseRectToParent = false;
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
                    new Rectangle(0, 0, 0, 25),
                    Color.White * 0.5f, Color.Black, Alignment.BottomCenter, Alignment.Left, "ChatTextBox", guiFrame);
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
                Alignment.Left, Alignment.TopLeft, "ListBoxElement", null, true, GUI.SmallFont);
            msg.UserData = message.SenderName;
            msg.CanBeFocused = false;
            msg.Padding = new Vector4(20.0f, 0, 0, 0);
            msg.Flash(Color.Yellow * 0.5f);

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
            hideTimer = HideDelay;
        }
        
        public void Update(float deltaTime)
        {
            if (inputBox != null && inputBox.Selected) hideTimer = HideDelay;

            bool hovering = 
                PlayerInput.MousePosition.X > Math.Min(chatBox.Rect.X, RadioButton.Rect.X) && 
                PlayerInput.MousePosition.Y > chatBox.Rect.Y && 
                PlayerInput.MousePosition.Y < Math.Max(chatBox.Rect.Bottom, radioButton.Rect.Bottom);

            hideTimer -= deltaTime;
            if ((hideTimer > 0.0f || hovering) && Inventory.draggingItem == null)
            {
                guiFrame.Rect = new Rectangle(Vector2.Lerp(chatBox.Rect.Location.ToVector2(), defaultPos.ToVector2(), deltaTime * 10.0f).ToPoint(), guiFrame.Rect.Size);
            }
            else
            {
                guiFrame.Rect = new Rectangle(Vector2.Lerp(chatBox.Rect.Location.ToVector2(), new Vector2(defaultPos.X + chatBox.Rect.Width - 10, defaultPos.Y), deltaTime * 10.0f).ToPoint(), guiFrame.Rect.Size);
            }
        }
    }
}
