using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ChatBox
    {
        const float HideDelay = 5.0f;

        private static Sprite radioIcon;
        
        private GUIFrame guiFrame;

        private GUIListBox chatBox;
        private GUITextBox inputBox;

        private GUIButton toggleButton;

        private GUIButton radioButton;

        private Point screenResolution;

        private bool isSinglePlayer;
               
        private bool toggleOpen;
        private float openState;

        private float prevUIScale;

        //individual message texts that pop up when the chatbox is hidden
        const float PopupMessageDuration = 5.0f;
        private float popupMessageTimer;
        private Queue<GUIComponent> popupMessages = new Queue<GUIComponent>();
        
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

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            int toggleButtonWidth = (int)(30 * GUI.Scale);
            guiFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ChatBoxArea, parent.RectTransform), style: null);
            chatBox = new GUIListBox(new RectTransform(new Vector2(1.0f, isSinglePlayer ? 1.0f : 0.9f), guiFrame.RectTransform), style: "ChatBox");
            toggleButton = new GUIButton(new RectTransform(new Point(toggleButtonWidth, HUDLayoutSettings.ChatBoxArea.Height), parent.RectTransform),
                style: "UIToggleButton");
            
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
            
            if (isSinglePlayer)
            {
                radioButton = new GUIButton(new RectTransform(new Vector2(0.15f, 0.2f), guiFrame.RectTransform, Anchor.BottomRight, Pivot.BottomLeft),
                    style: null);
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

                radioButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.2f), inputBox.RectTransform, Anchor.CenterRight, Pivot.Center),
                    style: null);
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
            
            if (!string.IsNullOrEmpty(senderName))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), chatBox.Content.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.0f) },
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
            msg.Flash(Color.Yellow);
            //some spacing at the bottom of the msg
            msg.RectTransform.NonScaledSize += new Point(0, 5);
                        
            chatBox.UpdateScrollBarSize();

            if (!toggleOpen)
            {
                var popupMsg = new GUIFrame(new RectTransform(Vector2.One, guiFrame.RectTransform), style: "GUIToolTip")
                {
                    Visible = false
                };
                var senderText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), popupMsg.RectTransform, Anchor.TopRight),
                    senderName, textColor: Color.White, font: GUI.SmallFont, textAlignment: Alignment.TopRight)
                {
                    CanBeFocused = false
                };
                var msgText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), popupMsg.RectTransform, Anchor.TopRight)
                    { AbsoluteOffset = new Point(0, senderText.Rect.Height) },
                    displayedText, textColor: message.Color, font: GUI.SmallFont, textAlignment: Alignment.TopRight, style: null, wrap: true)
                {
                    CanBeFocused = false
                };
                int textWidth = (int)Math.Max(
                    msgText.Font.MeasureString(msgText.WrappedText).X, 
                    senderText.Font.MeasureString(senderText.WrappedText).X);
                popupMsg.RectTransform.Resize(new Point(textWidth + 20, msgText.Rect.Bottom - senderText.Rect.Y), resizeChildren: false);
                popupMessages.Enqueue(popupMsg);
            }

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
            //hideTimer = HideDelay;
        }

        private void SetUILayout()
        {
            guiFrame.RectTransform.AbsoluteOffset = Point.Zero;
            guiFrame.RectTransform.RelativeOffset = new Vector2(
                HUDLayoutSettings.ChatBoxArea.X / (float)GameMain.GraphicsWidth,
                HUDLayoutSettings.ChatBoxArea.Y / (float)GameMain.GraphicsHeight);
            guiFrame.RectTransform.NonScaledSize = HUDLayoutSettings.ChatBoxArea.Size;

            int toggleButtonWidth = (int)(30 * GUI.Scale);
            //make room for the toggle button
            if (HUDLayoutSettings.ChatBoxAlignment == Alignment.Left)
            {
                guiFrame.RectTransform.AbsoluteOffset += new Point(toggleButtonWidth, 0);
            }
            guiFrame.RectTransform.NonScaledSize -= new Point(toggleButtonWidth);

            toggleButton.RectTransform.NonScaledSize = new Point(toggleButtonWidth, HUDLayoutSettings.ChatBoxArea.Height);
            toggleButton.RectTransform.AbsoluteOffset = HUDLayoutSettings.ChatBoxAlignment == Alignment.Left ?
                new Point(HUDLayoutSettings.ChatBoxArea.X, HUDLayoutSettings.ChatBoxArea.Y) :
                new Point(HUDLayoutSettings.ChatBoxArea.Right - toggleButtonWidth, HUDLayoutSettings.ChatBoxArea.Y);
        }
                
        public void Update(float deltaTime)
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale)
            {
                SetUILayout();
                screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                prevUIScale = GUI.Scale;
            }
            
            if (toggleOpen || (inputBox != null && inputBox.Selected))
            {
                openState += deltaTime * 5.0f;
                //delete all popup messages when the chatbox is open
                while (popupMessages.Count > 0)
                {
                    var popupMsg = popupMessages.Dequeue();
                    popupMsg.Parent.RemoveChild(popupMsg);
                }
            }
            else
            {
                openState -= deltaTime * 5.0f;

                //make the first popup message visible
                var popupMsg = popupMessages.Count > 0 ? popupMessages.Peek() : null;
                if (popupMsg != null)
                {
                    popupMsg.Visible = true;
                    //popup messages appear and disappear faster when there's more pending messages
                    popupMessageTimer += deltaTime * popupMessages.Count * popupMessages.Count;
                    if (popupMessageTimer > PopupMessageDuration)
                    {
                        //move the message out of the screen and delete it
                        popupMsg.RectTransform.ScreenSpaceOffset =
                            new Point((int)MathHelper.SmoothStep(-popupMsg.Rect.Width - toggleButton.Rect.Width * 2, 10, (popupMessageTimer - PopupMessageDuration) * 5.0f), 0);
                        if (popupMessageTimer > PopupMessageDuration + 1.0f)
                        {
                            popupMessageTimer = 0.0f;
                            popupMsg.Parent.RemoveChild(popupMsg);
                            popupMessages.Dequeue();
                        }
                    }
                    else
                    {
                        //move the message on the screen
                        popupMsg.RectTransform.ScreenSpaceOffset = new Point(
                            (int)MathHelper.SmoothStep(0, -popupMsg.Rect.Width - toggleButton.Rect.Width * 2, popupMessageTimer * 5.0f), 0);
                    }
                }
            }
            openState = MathHelper.Clamp(openState, 0.0f, 1.0f);
            int hiddenBoxOffset = guiFrame.Rect.Width + toggleButton.Rect.Width;
            guiFrame.RectTransform.AbsoluteOffset =
                new Point((int)MathHelper.SmoothStep(hiddenBoxOffset * (HUDLayoutSettings.ChatBoxAlignment == Alignment.Left ? -1 : 1), 0, openState), 0);
        }
    }
}
