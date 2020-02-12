using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class ChatBox
    {
        public const string RadioChatString = "r; ";

        private GUIListBox chatBox;
        private Point screenResolution;

        public readonly ChatManager ChatManager = new ChatManager();

        public bool IsSinglePlayer { get; private set; }

        private bool _toggleOpen = true;
        public bool ToggleOpen
        {
            get { return _toggleOpen; }
            set
            {
                _toggleOpen = GameMain.Config.ChatOpen = value;
                if (value) hideableElements.Visible = true;
            }
        }
        private float openState;

        public bool CloseAfterMessageSent;

        private float prevUIScale;

        //individual message texts that pop up when the chatbox is hidden
        const float PopupMessageDuration = 5.0f;
        private float popupMessageTimer;
        private Queue<GUIComponent> popupMessages = new Queue<GUIComponent>();

        public GUITextBox.OnEnterHandler OnEnterMessage
        {
            get { return InputBox.OnEnterPressed; }
            set { InputBox.OnEnterPressed = value; }
        }

        public GUIFrame GUIFrame { get; private set; }

        public GUITextBox InputBox { get; private set; }

        public GUIButton ToggleButton;

        private GUIButton showNewMessagesButton;

        private GUIFrame hideableElements;

        public const int ToggleButtonWidthRaw = 30;
        private int popupMessageOffset;

        public ChatBox(GUIComponent parent, bool isSinglePlayer)
        {
            this.IsSinglePlayer = isSinglePlayer;

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            int toggleButtonWidth = (int)(ToggleButtonWidthRaw * GUI.Scale);
            GUIFrame = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ChatBoxArea, parent.RectTransform), style: null);

            hideableElements = new GUIFrame(new RectTransform(Vector2.One, GUIFrame.RectTransform), style: null);

            var chatBoxHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.875f), hideableElements.RectTransform), style: "ChatBox");
            chatBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.95f), chatBoxHolder.RectTransform, Anchor.CenterRight), style: null);

            InputBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.125f), hideableElements.RectTransform, Anchor.BottomLeft),
                style: "ChatTextBox")
            {
                OverflowClip = true,
                Font = GUI.SmallFont,
                MaxTextLength = ChatMessage.MaxLength
            };

            ChatManager.RegisterKeys(InputBox, ChatManager);
            
            InputBox.OnDeselected += (gui, Keys) =>
            {
                ChatManager.Clear();
                //gui.Text = "";
            };

            var chatSendButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.7f), InputBox.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonToggleRight");
            chatSendButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                InputBox.OnEnterPressed(InputBox, InputBox.Text);
                return true;
            };
            chatSendButton.RectTransform.AbsoluteOffset = new Point((int)(InputBox.Rect.Height * 0.15f), 0);
            InputBox.TextBlock.RectTransform.MaxSize 
                = new Point((int)(InputBox.Rect.Width - chatSendButton.Rect.Width * 1.25f - InputBox.TextBlock.Padding.Z), int.MaxValue);

            showNewMessagesButton = new GUIButton(new RectTransform(new Vector2(1f, 0.075f), GUIFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.125f) }, TextManager.Get("chat.shownewmessages"));
            showNewMessagesButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                chatBox.ScrollBar.BarScrollValue = 1f;
                showNewMessagesButton.Visible = false;
                return true;
            };

            showNewMessagesButton.Visible = false;
            ToggleOpen = GameMain.Config.ChatOpen;
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
            while (chatBox.Content.CountChildren > 60)
            {
                chatBox.RemoveChild(chatBox.Content.Children.First());
            }

            float prevSize = chatBox.BarSize;

            string displayedText = message.TranslatedText;
            string senderName = "";
            Color senderColor = Color.White;
            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                senderName = (message.Type == ChatMessageType.Private ? "[PM] " : "") + message.SenderName;
            }
            if (message.Sender?.Info?.Job != null)
            {
                senderColor = Color.Lerp(message.Sender.Info.Job.Prefab.UIColor, Color.White, 0.25f);
            }

            var msgHolder = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.0f), chatBox.Content.RectTransform, Anchor.TopCenter), style: null,
                    color: ((chatBox.Content.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f);

            GUITextBlock senderNameBlock = new GUITextBlock(new RectTransform(new Vector2(0.98f, 0.0f), msgHolder.RectTransform) { AbsoluteOffset = new Point((int)(5 * GUI.Scale), 0) },
                ChatMessage.GetTimeStamp(), textColor: Color.LightGray, font: GUI.SmallFont, textAlignment: Alignment.TopLeft, style: null)
            {
                CanBeFocused = true
            };
            if (!string.IsNullOrEmpty(senderName))
            {
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), senderNameBlock.RectTransform) { AbsoluteOffset = new Point((int)(senderNameBlock.TextSize.X), 0) },
                    senderName, textColor: senderColor, font: GUI.SmallFont, textAlignment: Alignment.TopLeft, style: null)
                {
                    CanBeFocused = true
                };
            }

            var msgText =new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgHolder.RectTransform)
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
            msgHolder.RectTransform.SizeChanged += Recalculate;
            Recalculate();
            void Recalculate()
            {
                msgHolder.RectTransform.SizeChanged -= Recalculate;
                //resize the holder to match the size of the message and add some spacing
                msgText.RectTransform.MaxSize = new Point(msgHolder.Rect.Width - msgText.RectTransform.AbsoluteOffset.X, int.MaxValue);
                senderNameBlock.RectTransform.MaxSize = new Point(msgHolder.Rect.Width - senderNameBlock.RectTransform.AbsoluteOffset.X, int.MaxValue);
                msgHolder.Children.ForEach(c => (c as GUITextBlock)?.CalculateHeightFromText());
                msgHolder.RectTransform.Resize(new Point(msgHolder.Rect.Width, msgHolder.Children.Sum(c => c.Rect.Height) + (int)(10 * GUI.Scale)), resizeChildren: false);
                msgHolder.RectTransform.SizeChanged += Recalculate;
                chatBox.RecalculateChildren();
                chatBox.UpdateScrollBarSize();
            }

            CoroutineManager.StartCoroutine(UpdateMessageAnimation(msgHolder, 0.5f));

            chatBox.UpdateScrollBarSize();
                       
            if (chatBox.ScrollBar.Visible && chatBox.ScrollBar.BarScroll < 1f)
            {
                showNewMessagesButton.Visible = true;
            }

            if (!ToggleOpen)
            {
                var popupMsg = new GUIFrame(new RectTransform(Vector2.One, GUIFrame.RectTransform), style: "GUIToolTip")
                {
                    Visible = false,
                    CanBeFocused = false
                };
                var senderText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), popupMsg.RectTransform, Anchor.TopRight),
                    senderName, textColor: senderColor, font: GUI.SmallFont, textAlignment: Alignment.TopRight)
                {
                    CanBeFocused = false
                };
                var msgPopupText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), popupMsg.RectTransform, Anchor.TopRight)
                    { AbsoluteOffset = new Point(0, senderText.Rect.Height) },
                    displayedText, textColor: message.Color, font: GUI.SmallFont, textAlignment: Alignment.TopRight, style: null, wrap: true)
                {
                    CanBeFocused = false
                };
                int textWidth = (int)Math.Max(
                    msgPopupText.Font.MeasureString(msgPopupText.WrappedText).X,
                    senderText.Font.MeasureString(senderText.WrappedText).X);
                popupMsg.RectTransform.Resize(new Point(textWidth + 20, msgPopupText.Rect.Bottom - senderText.Rect.Y), resizeChildren: false);
                popupMessages.Enqueue(popupMsg);
            }

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;

            GUISoundType soundType = GUISoundType.ChatMessage;
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

        public void SetVisibility(bool visible)
        {
            GUIFrame.Parent.Visible = visible;
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
            GUIFrame.RectTransform.AbsoluteOffset = Point.Zero;
            GUIFrame.RectTransform.RelativeOffset = new Vector2(
                HUDLayoutSettings.ChatBoxArea.X / (float)GameMain.GraphicsWidth,
                HUDLayoutSettings.ChatBoxArea.Y / (float)GameMain.GraphicsHeight);
            GUIFrame.RectTransform.NonScaledSize = HUDLayoutSettings.ChatBoxArea.Size;

            int toggleButtonWidth = (int)(ToggleButtonWidthRaw * GUI.Scale);
            GUIFrame.RectTransform.NonScaledSize -= new Point(toggleButtonWidth, 0);
            GUIFrame.RectTransform.AbsoluteOffset += new Point(toggleButtonWidth, 0);

            popupMessageOffset = GameMain.GameSession.CrewManager.ReportButtonFrame.Rect.Width + GUIFrame.Rect.Width + (int)(20 * GUI.Scale);
        }

        public void Update(float deltaTime)
        {
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale)
            {
                SetUILayout();
                screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                prevUIScale = GUI.Scale;
            }

            //hide chatbox when accessing the inventory of another character to prevent overlaps
            if (Character.Controlled?.SelectedCharacter?.Inventory != null &&
                Character.Controlled.SelectedCharacter.CanInventoryBeAccessed)
            {
                SetVisibility(false);
            }
            else
            {
                SetVisibility(true);
            }

            if (showNewMessagesButton.Visible && chatBox.ScrollBar.BarScroll == 1f)
            {
                showNewMessagesButton.Visible = false;
            }

            if (ToggleButton != null)
            {
                ToggleButton.RectTransform.AbsoluteOffset = new Point(GUIFrame.Rect.Right, GUIFrame.Rect.Y + HUDLayoutSettings.ChatBoxArea.Height - ToggleButton.Rect.Height);
            }

            if (ToggleOpen)
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
                            new Point((int)MathHelper.SmoothStep(popupMessageOffset, 10, (popupMessageTimer - PopupMessageDuration) * 5.0f), 0);
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
                            (int)MathHelper.SmoothStep(0, popupMessageOffset, popupMessageTimer * 5.0f), 0);
                    }
                }
            }
            openState = MathHelper.Clamp(openState, 0.0f, 1.0f);
            int hiddenBoxOffset = -(GUIFrame.Rect.Width);
            GUIFrame.RectTransform.AbsoluteOffset =
                new Point((int)MathHelper.SmoothStep(hiddenBoxOffset, 0, openState), 0);
            hideableElements.Visible = openState > 0.0f;
        }
    }
}
