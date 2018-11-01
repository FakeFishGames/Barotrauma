using Barotrauma.Items.Components;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    enum ClientPacketHeader
    {
        REQUEST_AUTH,   //ask the server if a password is needed, if so we'll get nonce for encryption
        REQUEST_STEAMAUTH, //the same as REQUEST_AUTH, but in addition we want to authenticate the player's Steam ID
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
        ACHIEVEMENT,        //give the client a steam achievement

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
        ENTITY_EVENT_INITIAL,
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound,
        Kick,
        StartRound
    }

    enum DisconnectReason
    {
        Unknown,
        Banned,
        Kicked,
        ServerShutdown,
        ServerFull,
        AuthenticationRequired,
        SteamAuthenticationRequired,
        SteamAuthenticationFailed,
        SessionTaken,
        TooManyFailedLogins,
        NoName,
        InvalidName,
        NameTaken,
        InvalidVersion,
        MissingContentPackage,
        IncompatibleContentPackage,
        NotOnWhitelist,
    }

    abstract partial class NetworkMember
    {
#if DEBUG
        public Dictionary<string, long> messageCount = new Dictionary<string, long>();
#endif

        public NetPeer NetPeer
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
        
        public int Port
        {
            get;
            set;
        }
        
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

        public RespawnManager RespawnManager
        {
            get { return respawnManager; }
        }

        public ServerLog ServerLog
        {
            get;
            protected set;
        }

        public NetPeerConfiguration NetPeerConfiguration
        {
            get;
            protected set;
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

        public void AddChatMessage(string message, ChatMessageType type, string senderName = "", Character senderCharacter = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderCharacter));
        }

        public void AddChatMessage(ChatMessage message)
        {
            GameServer.Log(message.TextWithSender, ServerLog.MessageType.Chat);
            
            if (message.Sender != null && !message.Sender.IsDead)
            {
                message.Sender.ShowSpeechBubble(2.0f, ChatMessage.MessageColor[(int)message.Type]);
            }

#if CLIENT
            GameMain.NetLobbyScreen.NewChatMessage(message);
            chatBox.AddMessage(message);
#endif
        }

        public virtual void KickPlayer(string kickedName, string reason) { }

        public virtual void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null) { }

        public virtual void Update(float deltaTime) 
        {
#if CLIENT
            UpdateHUD(deltaTime);            
#endif
        }

        public virtual void Disconnect() { }
    }

}
