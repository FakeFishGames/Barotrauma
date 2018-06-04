using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    class ChatBox
    {
        const float HideDelay = 5.0f;

        private static Sprite radioIcon;//, toggleArrow;

        private Point defaultPos;

        private GUIFrame guiFrame;

        private GUIListBox chatBox;
        private GUITextBox inputBox;

        private GUIButton toggleButton;

        private GUIButton radioButton;

        private bool isSinglePlayer;

        private float hideTimer;

        private bool toggleOpen;

        public float HideTimer
        {
            get { return hideTimer; }
            set { hideTimer = MathHelper.Clamp(value, 0.0f, HideDelay); }
        }

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
                        
            guiFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ChatBoxArea, parent.RectTransform), style: null);
            chatBox = new GUIListBox(new RectTransform(new Vector2(1.0f, isSinglePlayer ? 1.0f : 0.9f), guiFrame.RectTransform), style: "ChatBox");

            toggleButton = new GUIButton(new RectTransform(new Point(25, 70), guiFrame.RectTransform, Anchor.TopRight, Pivot.TopLeft) { AbsoluteOffset = new Point(10, 0) },
                style: "GUIButtonHorizontalArrow")
            {
                ClampMouseRectToParent = false
            };
            toggleButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                toggleOpen = !toggleOpen;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = toggleOpen == (HUDLayoutSettings.ChatBoxAlignment == Alignment.Right) ?
                      SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                return true;
            };
            
            defaultPos = guiFrame.Rect.Location;
            
            if (isSinglePlayer)
            {
                radioButton = new GUIButton(new RectTransform(radioIcon.size.ToPoint(), guiFrame.RectTransform, Anchor.BottomRight, Pivot.Center),
                    style: null)
                {
                    ClampMouseRectToParent = false
                };
                new GUIImage(new RectTransform(Vector2.One, radioButton.RectTransform), radioIcon, scaleToFit: true)
                {
                    Color = Color.White * 0.8f
                };
                radioButton.OnClicked = (GUIButton btn, object userData) =>
                {
                    GameMain.GameSession.CrewManager.ToggleCrewAreaOpen = !GameMain.GameSession.CrewManager.ToggleCrewAreaOpen;
                    return true;
                };
            }
            else
            {
                inputBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), guiFrame.RectTransform, Anchor.BottomCenter),
                    style: "ChatTextBox")
                {
                    Font = GUI.SmallFont,
                    MaxTextLength = ChatMessage.MaxLength
                };

                radioButton = new GUIButton(new RectTransform(radioIcon.size.ToPoint(), inputBox.RectTransform, Anchor.CenterRight, Pivot.Center),
                    style: null)
                {
                    ClampMouseRectToParent = false
                };
                new GUIImage(new RectTransform(Vector2.One, radioButton.RectTransform), radioIcon, scaleToFit: true);
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
            while (chatBox.Content.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.Content.Children.First());
            }
            
            float prevSize = chatBox.BarSize;

            string displayedText = message.Text;
            string senderName = "";
            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                senderName = (message.Type == ChatMessageType.Private ? "[PM] " : "") + message.SenderName;
            }

            GUITextBlock senderText = null;
            if (!string.IsNullOrEmpty(senderName))
            {
                senderText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), chatBox.Content.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.0f) },
                    senderName, textColor: Color.White, font: GUI.SmallFont, style: null,
                    color: ((chatBox.Content.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f)
                {
                    CanBeFocused = false
                };
            }

            GUITextBlock msg = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), chatBox.Content.RectTransform) { RelativeOffset = new Vector2(0.08f, 0.0f) },
                displayedText, textColor: message.Color, font: GUI.SmallFont, style: null, wrap: true,
                color: ((chatBox.Content.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f)
            {
                UserData = message.SenderName,
                CanBeFocused = false
            };
            msg.Flash(Color.Yellow * 0.5f);
            //some spacing at the bottom of the msg
            msg.RectTransform.NonScaledSize += new Point(0, 5);
                        
            chatBox.UpdateScrollBarSize();

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

        private float openState;
        
        public void Update(float deltaTime)
        {
            if (inputBox != null && inputBox.Selected) hideTimer = HideDelay;

            bool hovering =
                (PlayerInput.MousePosition.X > Math.Min(Math.Min(chatBox.Rect.X, toggleButton.Rect.X), radioButton.Rect.X) || HUDLayoutSettings.ChatBoxAlignment == Alignment.Left) &&
                (PlayerInput.MousePosition.X < Math.Max(Math.Max(chatBox.Rect.Right, radioButton.Rect.Right), toggleButton.Rect.Right) || HUDLayoutSettings.ChatBoxAlignment == Alignment.Right) &&
                PlayerInput.MousePosition.Y > chatBox.Rect.Y &&
                PlayerInput.MousePosition.Y < Math.Max(chatBox.Rect.Bottom, radioButton.Rect.Bottom);

            hideTimer -= deltaTime;

            if ((hideTimer > 0.0f || hovering || toggleOpen) && Inventory.draggingItem == null)
            {
                openState += deltaTime * 5.0f;
            }
            else
            {
                openState -= deltaTime * 5.0f;
            }
            openState = MathHelper.Clamp(openState, 0.0f, 1.0f);
            guiFrame.RectTransform.AbsoluteOffset = new Point((int)MathHelper.SmoothStep(-guiFrame.Rect.Width, 0, openState), 0);
        }
    }
}
