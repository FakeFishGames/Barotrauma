using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Lidgren.Network;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    enum ClientPacketHeader
    {
        REQUEST_AUTH,   //ask the server if a password is needed, if so we'll get nonce for encryption
        REQUEST_INIT,   //ask the server to give you initialization
        UPDATE_LOBBY,   //update state in lobby
        UPDATE_INGAME,  //update state ingame

        FILE_REQUEST,   //request a (submarine) file from the server
        
        RESPONSE_STARTGAME, //tell the server whether you're ready to start
        SERVER_COMMAND      //tell the server to end a round or kick/ban someone (special permissions required)
    }
    enum ClientNetObject
    {
        END_OF_MESSAGE, //self-explanatory
        SYNC_IDS,       //ids of the last changes the client knows about
        CHAT_MESSAGE,   //also self-explanatory
        VOTE,           //you get the idea
        CHARACTER_INPUT,
        ENTITY_STATE
    }

    enum ServerPacketHeader
    {
        AUTH_RESPONSE,      //tell the player if they require a password to log in
        AUTH_FAILURE,       //the server won't authorize player yet, however connection is still alive
        UPDATE_LOBBY,       //update state in lobby (votes and chat messages)
        UPDATE_INGAME,      //update state ingame (character input and chat messages)

        PERMISSIONS,        //tell the client which special permissions they have (if any)

        FILE_TRANSFER,

        QUERY_STARTGAME,    //ask the clients whether they're ready to start
        STARTGAME,          //start a new round
        ENDGAME
    }
    enum ServerNetObject
    {
        END_OF_MESSAGE,
        SYNC_IDS,
        CHAT_MESSAGE,
        VOTE,
        ENTITY_POSITION,
        ENTITY_EVENT,
        ENTITY_EVENT_INITIAL
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound,
        Kick
    }

    abstract partial class NetworkMember
    {
#if DEBUG
        public Dictionary<string, long> messageCount = new Dictionary<string, long>();
#endif

        public NetPeer netPeer
        {
            get;
            protected set;
        }

        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        public int EndVoteCount, EndVoteMax;

        protected bool gameStarted;

        public Dictionary<string, bool> monsterEnabled;

        protected RespawnManager respawnManager;

        public Voting Voting;
        
        [HasDefaultValue(14242, true)]
        public int Port
        {
            get;
            set;
        }

        [HasDefaultValue("", true)]
        public string Name
        {
            get { return name; }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                name = value;
            }
        }

        public bool GameStarted
        {
            get { return gameStarted; }
        }

        public virtual List<Client> ConnectedClients
        {
            get { return null; }
        }

        public NetworkMember()
        {
            InitProjSpecific();
            
            Voting = new Voting();
        }

        public bool CanUseRadio(Character sender)
        {
            if (sender == null) return false;

            var radio = sender.Inventory.Items.FirstOrDefault(i => i != null && i.GetComponent<WifiComponent>() != null);
            if (radio == null || !sender.HasEquippedItem(radio)) return false;
                       
            var radioComponent = radio.GetComponent<WifiComponent>();
            if (radioComponent == null) return false;
            return radioComponent.HasRequiredContainedItems(false);
        }

        public void AddChatMessage(string message, ChatMessageType type, string senderName="", Character senderCharacter = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderCharacter));
        }
        
        public void AddChatMessage(ChatMessage message)
        {
            GameServer.Log(message.TextWithSender, ServerLog.MessageType.Chat);

            string displayedText = message.Text;

            if (message.Sender != null && !message.Sender.IsDead)
            {
                message.Sender.ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)message.Type]);
            }

#if CLIENT
            GameMain.NetLobbyScreen.NewChatMessage(message);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            if (!string.IsNullOrWhiteSpace(message.SenderName))
            {
                displayedText = message.SenderName + ": " + displayedText;
            }
            
            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, chatBox.Rect.Width - 40, 0), displayedText,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, message.Color,
                Alignment.Left, Alignment.TopLeft, "", null, true, GUI.SmallFont);
            msg.UserData = message.SenderName;

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
#endif
        }

        public virtual void KickPlayer(string kickedName, string reason) { }

        public virtual void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null) { }

        public virtual void Update(float deltaTime) 
        {
#if CLIENT
            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                chatMsgBox.Visible = Character.Controlled == null || Character.Controlled.CanSpeak;

                inGameHUD.Update(deltaTime);

                GameMain.GameSession.CrewManager.Update(deltaTime);
                
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
#endif
        }

        public virtual void Disconnect() { }
    }

}
