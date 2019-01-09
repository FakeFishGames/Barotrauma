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
        private static Sprite radioIcon;
        
        private GUIFrame guiFrame;

        private GUIListBox chatBox;
        private GUITextBox inputBox;

        private GUIButton toggleButton;

        private GUIButton radioButton;

        private Point screenResolution;

        private bool isSinglePlayer;
        public bool IsSinglePlayer => isSinglePlayer;
               
        private bool toggleOpen = true;
        private float openState;

        private float prevUIScale;

        //individual message texts that pop up when the chatbox is hidden
        const float PopupMessageDuration = 5.0f;
        private float popupMessageTimer;
        private Queue<GUIComponent> popupMessages = new Queue<GUIComponent>();

        public GUITextBox.OnEnterHandler OnEnterMessage
        {
            get { return inputBox.OnEnterPressed; }
            set { inputBox.OnEnterPressed = value; }
        }

        public GUIFrame GUIFrame
        {
            get { return guiFrame; }
        }

        public GUIButton RadioButton
        {
            get { return radioButton; }
        }

        public GUITextBox InputBox
        {
            get { return inputBox; }
        }

        public GUIButton ToggleButton
        {
            get { return toggleButton; }
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
            chatBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.9f), guiFrame.RectTransform), style: "ChatBox");
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
            
            inputBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.1f), guiFrame.RectTransform, Anchor.BottomCenter),
                style: "ChatTextBox")
            {
                Font = GUI.SmallFont,
                MaxTextLength = ChatMessage.MaxLength
            };

            radioButton = new GUIButton(new RectTransform(new Vector2(0.1f, 2.0f), inputBox.RectTransform,
                HUDLayoutSettings.ChatBoxAlignment == Alignment.Right ? Anchor.BottomRight : Anchor.BottomLeft,
                HUDLayoutSettings.ChatBoxAlignment == Alignment.Right ? Pivot.TopRight : Pivot.TopLeft),
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
                    }
                }
                return true;
            };
        }

        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            string command = ChatMessage.GetChatMessageCommand(text, out _);
            if (IsSinglePlayer)
            {
                //radio is the only allowed special message type in single player
                if (command != "r" && command != "radio")
                {
                    command = "";
                }
            }            

            switch (command)
            {
                case "r":
                case "radio":
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Radio];
                    break;
                case "d":
                case "dead":
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Dead];
                    break;
                default:
                    if (Character.Controlled != null && (Character.Controlled.IsDead || Character.Controlled.SpeechImpediment >= 100.0f))
                    {
                        textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Dead];
                    }
                    else if (command != "") //PMing
                    {
                        textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Private];
                    }
                    else
                    {
                        textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];
                    }
                    break;
            }

            return true;
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

            var msgHolder = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.0f), chatBox.Content.RectTransform, Anchor.TopCenter), style: null,
                    color: ((chatBox.Content.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f);

            GUITextBlock senderNameBlock = null;
            if (!string.IsNullOrEmpty(senderName))
            {
                senderNameBlock = new GUITextBlock(new RectTransform(new Vector2(0.98f, 0.0f), msgHolder.RectTransform)
                { AbsoluteOffset = new Point((int)(5 * GUI.Scale), 0) },
                    senderName, textColor: Color.White, font: GUI.SmallFont, textAlignment: Alignment.TopLeft, style: null)
                {
                    CanBeFocused = true
                };
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgHolder.RectTransform)
            { AbsoluteOffset = new Point((int)(10 * GUI.Scale), senderNameBlock == null ? 0 : senderNameBlock.Rect.Height) },
                displayedText, textColor: message.Color, font: GUI.SmallFont, textAlignment: Alignment.TopLeft, style: null, wrap: true,
                color: ((chatBox.Content.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f)
            {
                UserData = message.SenderName,
                CanBeFocused = true
            };

            if (message is OrderChatMessage orderChatMsg && 
                Character.Controlled != null && 
                orderChatMsg.TargetCharacter == Character.Controlled)
            {
                msgHolder.Flash(Color.OrangeRed * 0.6f, flashDuration: 5.0f);
            }
            else
            {
                msgHolder.Flash(Color.Yellow * 0.6f);
            }
            //resize the holder to match the size of the message and add some spacing
            msgHolder.RectTransform.Resize(new Point(msgHolder.Rect.Width, msgHolder.Children.Sum(c => c.Rect.Height) + (int)(10 * GUI.Scale)), resizeChildren: false);

            CoroutineManager.StartCoroutine(UpdateMessageAnimation(msgHolder, 0.5f));

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
        }

        private IEnumerable<object> UpdateMessageAnimation(GUIComponent message, float animDuration)
        {
            float timer = 0.0f;
            while (timer < animDuration)
            {
                timer += CoroutineManager.DeltaTime;
                float wavePhase = timer / animDuration * MathHelper.TwoPi;
                message.RectTransform.ScreenSpaceOffset = 
                    new Point((int)(Math.Sin(wavePhase) * (1.0f - timer / animDuration) * 50.0f), 0);
                yield return CoroutineStatus.Running;
            }
            message.RectTransform.ScreenSpaceOffset = Point.Zero;
            yield return CoroutineStatus.Success;
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
            guiFrame.RectTransform.NonScaledSize -= new Point(toggleButtonWidth, 0);

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
                            (int)MathHelper.SmoothStep(0, -popupMsg.Rect.Width - toggleButton.Rect.Width * 2 - (int)(35 * GUI.Scale), popupMessageTimer * 5.0f), 0);
                    }
                }
            }
            openState = MathHelper.Clamp(openState, 0.0f, 1.0f);
            int hiddenBoxOffset = guiFrame.Rect.Width + toggleButton.Rect.Width;
            if (radioButton != null) hiddenBoxOffset += (int)(radioButton.Rect.Width * 1.5f);
            guiFrame.RectTransform.AbsoluteOffset =
                new Point((int)MathHelper.SmoothStep(hiddenBoxOffset * (HUDLayoutSettings.ChatBoxAlignment == Alignment.Left ? -1 : 1), 0, openState), 0);
        }
    }
}
