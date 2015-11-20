using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    enum PacketTypes
    {
        Unknown,

        Login, LoggedIn, LogOut,

        PlayerJoined, PlayerLeft, KickedOut,

        StartGame, EndGame,

        CharacterInfo,

        Chatmessage, UpdateNetLobby,

        NetworkEvent,

        Traitor,

        Vote, VoteStatus,

        ResendRequest, ReliableMessage, LatestMessageID,
       
        SpectateRequest
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound
    }

    class NetworkMember
    {

        protected static Color[] messageColor = { Color.White, Color.Red, Color.LightBlue, Color.LightGreen };

        protected NetPeer netPeer;

        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        protected GUIFrame inGameHUD;
        protected GUIListBox chatBox;
        protected GUITextBox chatMsgBox;

        public int Port;

        protected bool gameStarted;

        protected Character myCharacter;
        protected CharacterInfo characterInfo;

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

        public NetworkMember()
        {
            inGameHUD = new GUIFrame(new Rectangle(0,0,0,0), null, null);
            inGameHUD.CanBeFocused = false;

            int width = 350, height = 100;
            chatBox = new GUIListBox(new Rectangle(
                GameMain.GraphicsWidth - 20 - width,
                GameMain.GraphicsHeight - 40 - 25 - height,
                width, height),
                Color.White * 0.5f, GUI.Style, inGameHUD);

            chatMsgBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, GUI.Style, inGameHUD);
            chatMsgBox.Font = GUI.SmallFont;
            chatMsgBox.OnEnterPressed = EnterChatMessage;


            Voting = new Voting();
        }

        protected NetOutgoingMessage ComposeNetworkEventMessage(bool isImportant, NetConnection excludedConnection = null)
        {
            if (netPeer == null) return null;

            var events = NetworkEvent.Events.FindAll(e => e.IsImportant == isImportant);
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
            }

            if (msgBytes.Count == 0) return null;

            NetOutgoingMessage message = netPeer.CreateMessage();
            message.Write((byte)PacketTypes.NetworkEvent);

            message.Write((float)NetTime.Now);

            message.Write((byte)msgBytes.Count);
            foreach (byte[] msgData in msgBytes)
            {
                if (msgData.Length > 255) DebugConsole.ThrowError("too large networkevent (" + msgData.Length + " bytes)");

                message.Write((byte)msgData.Length);
                message.Write(msgData);
            }

            return message;
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            SendChatMessage(GameMain.NetworkMember.Name + ": " + message);            

            textBox.Deselect();

            return true;
        }
        
        public void AddChatMessage(string message, ChatMessageType messageType)
        {
            GameMain.NetLobbyScreen.NewChatMessage(message, messageColor[(int)messageType]);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), message,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, messageColor[(int)messageType],
                Alignment.Left, null, null, true);
            msg.Font = GUI.SmallFont;

            msg.Padding = new Vector4(20.0f, 0, 0, 0);

            //float prevScroll = chatBox.BarScroll;

            float prevSize = chatBox.BarSize;

            msg.Padding = new Vector4(20, 0, 0, 0);
            chatBox.AddChild(msg);

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;

            GUI.PlayMessageSound();
        }

        public virtual void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server) { }

        public virtual void Update(float deltaTime) 
        {
            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.Update(deltaTime);

                //if (crewFrameOpen) crewFrame.Update(deltaTime);

                if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }

            if (PlayerInput.KeyHit(InputType.Chat))
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

            inGameHUD.Draw(spriteBatch);
        }

        public virtual bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            return false;
        }

        public virtual void Disconnect() { }

        protected byte PlayerCountToByte(int playerCount, int maxPlayers)
        {
            byte byteVal = (byte)playerCount;

            byteVal |= (byte)((maxPlayers - 1) << 4);

            return byteVal;
        }

        public static int ByteToPlayerCount(byte byteVal, out int maxPlayers)
        {
            maxPlayers = (byteVal >> 4)+1;

            int playerCount = byteVal & (byte)((1 << 4) - 1);

            return playerCount;
        }

    }

    enum ChatMessageType
    {
        Default, Admin, Dead, Server
    }
}
