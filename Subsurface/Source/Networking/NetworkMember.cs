using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Lidgren.Network;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    enum PacketTypes : byte
    {
        Unknown,

        Login, LoggedIn, LogOut,

        PlayerJoined, PlayerLeft, KickedOut,

        RequestNetLobbyUpdate,

        StartGame, EndGame, CanStartGame,

        NewItem, RemoveItem,

        NewCharacter,

        CharacterInfo,

        Chatmessage, UpdateNetLobby,

        NetworkEvent,

        Traitor,

        Vote, VoteStatus,

        ResendRequest, ReliableMessage, LatestMessageID,

        RequestFile, FileStream,
       
        SpectateRequest,

        Respawn
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound,
        Kick
    }

    abstract class NetworkMember
    {
#if DEBUG
        public Dictionary<string, long> messageCount = new Dictionary<string, long>();
#endif

        protected NetPeer netPeer;

        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        protected GUIFrame inGameHUD;
        protected GUIListBox chatBox;
        protected GUITextBox chatMsgBox;        

        public int EndVoteCount, EndVoteMax;
        //private GUITextBlock endVoteText;

        public int Port;

        protected bool gameStarted;

        protected Character myCharacter;
        protected CharacterInfo characterInfo;


        protected RespawnManager respawnManager;

        public Voting Voting;

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set { characterInfo = value; }
        }

        public string Name
        {
            get { return name; }
            set
            {
                if (string.IsNullOrEmpty(name)) return;
                name = value;
            }
        }

        public bool GameStarted
        {
            get { return gameStarted; }
        }

        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }


        public virtual List<Client> ConnectedClients
        {
            get { return null; }
        }

        public NetworkMember()
        {
            inGameHUD = new GUIFrame(new Rectangle(0,0,0,0), null, null);
            inGameHUD.CanBeFocused = false;

            int width = (int)MathHelper.Clamp(GameMain.GraphicsWidth * 0.35f, 350, 500);
            int height = (int)MathHelper.Clamp(GameMain.GraphicsHeight * 0.15f, 100, 200);
            chatBox = new GUIListBox(new Rectangle(
                GameMain.GraphicsWidth - 20 - width,
                GameMain.GraphicsHeight - 40 - 25 - height,
                width, height),
                Color.White * 0.5f, GUI.Style, inGameHUD);
            chatBox.Padding = Vector4.Zero;

            chatMsgBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, GUI.Style, inGameHUD);
            chatMsgBox.Font = GUI.SmallFont;
            chatMsgBox.Padding = Vector4.Zero;
            chatMsgBox.OnEnterPressed = EnterChatMessage;
            chatMsgBox.OnTextChanged = TypingChatMessage;

            Voting = new Voting();
        }

        protected NetOutgoingMessage ComposeNetworkEventMessage(NetworkEventDeliveryMethod deliveryMethod, NetConnection excludedConnection = null)
        {
            if (netPeer == null) return null;

            var events = NetworkEvent.Events.FindAll(e => e.DeliveryMethod == deliveryMethod);
            if (events.Count == 0) return null;

            List<byte[]> msgBytes = new List<byte[]>();

            foreach (NetworkEvent networkEvent in events)
            {
                if (excludedConnection != null && networkEvent.SenderConnection == excludedConnection) continue;

                NetBuffer tempMessage = new NetBuffer();// server.CreateMessage();
                if (!networkEvent.FillData(tempMessage)) continue;
                tempMessage.WritePadBits();

                tempMessage.Position = 0;
                msgBytes.Add(tempMessage.ReadBytes(tempMessage.LengthBytes));

#if DEBUG
                string msgType = networkEvent.Type.ToString();
                if (networkEvent.Type == NetworkEventType.EntityUpdate)
                {
                    msgType += " (" + Entity.FindEntityByID(networkEvent.ID) + ")";
                }

                long sentBytes = 0;
                if (!messageCount.TryGetValue(msgType, out sentBytes))
                {
                    messageCount.Add(msgType, tempMessage.LengthBytes);
                }
                else
                {
                    messageCount[msgType] += tempMessage.LengthBytes;
                }
#endif
            }

            if (msgBytes.Count == 0) return null;

            NetOutgoingMessage message = netPeer.CreateMessage();
            message.Write((byte)PacketTypes.NetworkEvent);

            message.Write((float)NetTime.Now);

            message.Write((byte)msgBytes.Count);
            foreach (byte[] msgData in msgBytes)
            {
                if (msgData.Length > 255) DebugConsole.ThrowError("Too large networkevent (" + msgData.Length + " bytes)");

                message.Write((byte)msgData.Length);
                message.Write(msgData);
            }


            return message;
        }

        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            string tempStr;
            string command = ChatMessage.GetChatMessageCommand(text, out tempStr);
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
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];
                    break;
            }

            return true;
        }

        public bool CanUseRadio(Character sender)
        {
            if (sender == null) return false;

            var radio = sender.Inventory.Items.FirstOrDefault(i => i != null && i.GetComponent<WifiComponent>() != null);
            if (radio == null || !sender.HasEquippedItem(radio)) return false;
                       
            var radioComponent = radio.GetComponent<WifiComponent>();
            return radioComponent.HasRequiredContainedItems(false);
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

            if (string.IsNullOrWhiteSpace(message)) return false;

            SendChatMessage(message);

            textBox.Deselect();

            return true;
        }

        public void AddChatMessage(string message, ChatMessageType type, string senderName="", Character senderCharacter = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderCharacter));
        }
        
        public void AddChatMessage(ChatMessage message)
        {
            if (message.Type == ChatMessageType.Radio && 
                Character.Controlled != null &&
                message.Sender != null && message.Sender != myCharacter)
            {
                var radio = message.Sender.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                if (radio == null) return;

                message.Sender.ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)ChatMessageType.Radio]);

                var radioComponent = radio.GetComponent<WifiComponent>();
                radioComponent.Transmit(message.TextWithSender);
                return;
            }

            string displayedText = message.Text;

            if (message.Sender != null)
            {
                if (message.Type == ChatMessageType.Default && Character.Controlled != null)
                {
                    displayedText = message.ApplyDistanceEffect(Character.Controlled);
                    if (string.IsNullOrWhiteSpace(displayedText)) return;
                }

                message.Sender.ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)ChatMessageType.Default]);
            }

            GameMain.NetLobbyScreen.NewChatMessage(message);

            GameServer.Log(message.Text, message.Color);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                displayedText = message.SenderName + ": " + displayedText;
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), displayedText,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, message.Color,
                Alignment.Left, null, null, true);
            msg.Font = GUI.SmallFont;

            msg.Padding = new Vector4(20.0f, 0, 0, 0);

            float prevSize = chatBox.BarSize;

            msg.Padding = new Vector4(20, 0, 0, 0);
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

        public virtual void SendChatMessage(string message, ChatMessageType? type = null) { }

        public virtual void Update(float deltaTime) 
        {
            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                chatMsgBox.Visible = Character.Controlled == null || 
                    (!Character.Controlled.IsUnconscious && Character.Controlled.Stun >= 0.0f);

                inGameHUD.Update(deltaTime);

                GameMain.GameSession.CrewManager.Update(deltaTime);

                //if (crewFrameOpen) crewFrame.Update(deltaTime);

                if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }

            if (PlayerInput.KeyHit(InputType.Chat) && chatMsgBox.Visible)
            {
                if (chatMsgBox.Selected)
                {
                    chatMsgBox.Text = "";
                    chatMsgBox.Deselect();
                }
                else
                {
                    chatMsgBox.Select();
                }
            }
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (!gameStarted || Screen.Selected != GameMain.GameScreen) return;

            GameMain.GameSession.CrewManager.Draw(spriteBatch);

            inGameHUD.Draw(spriteBatch);

            if (EndVoteCount > 0)
            {
                if (GameMain.NetworkMember.myCharacter == null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 180.0f, 40),
                        "Votes to end the round (y/n): " + EndVoteCount + "/" + (EndVoteMax - EndVoteCount), Color.White, null, 0, GUI.SmallFont);
                }
                else
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 140.0f, 40),
                        "Votes (y/n): " + EndVoteCount + "/" + (EndVoteMax - EndVoteCount), Color.White, null, 0, GUI.SmallFont);
                }
            }
        }

        public virtual bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            return false;
        }

        public virtual void Disconnect() { }
    }

}
