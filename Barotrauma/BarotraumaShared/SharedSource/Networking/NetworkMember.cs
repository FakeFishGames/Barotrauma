using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    public enum ClientPacketHeader
    {
        UPDATE_LOBBY,   //update state in lobby
        UPDATE_INGAME,  //update state ingame

        SERVER_SETTINGS, //change server settings
        
        CAMPAIGN_SETUP_INFO,

        FILE_REQUEST,   //request a (submarine) file from the server

        VOICE,
        
        PING_RESPONSE,

        RESPONSE_STARTGAME, //tell the server whether you're ready to start
        SERVER_COMMAND,     //tell the server to end a round or kick/ban someone (special permissions required)

        EVENTMANAGER_RESPONSE,

        REQUEST_STARTGAMEFINALIZE, //tell the server you're ready to finalize round initialization

        UPDATE_CHARACTERINFO,

        ERROR,           //tell the server that an error occurred
        CREW,            //hiring UI
        MEDICAL,         //medical clinic
        TRANSFER_MONEY,      // wallet transfers
        REWARD_DISTRIBUTION, // wallet reward distribution
        READY_CHECK,
        READY_TO_SPAWN
    }

    enum ClientNetSegment
    {
        SyncIds,       //ids of the last changes the client knows about
        ChatMessage,   //also self-explanatory
        Vote,          //you get the idea
        CharacterInput,
        EntityState,
        SpectatingPos
    }

    enum ClientNetError
    {
        MISSING_EVENT, //client was expecting a previous event
        MISSING_ENTITY //client can't find an entity of a certain ID
    }

    public enum ServerPacketHeader
    {
        AUTH_RESPONSE,      //tell the player if they require a password to log in
        AUTH_FAILURE,       //the server won't authorize player yet, however connection is still alive
        UPDATE_LOBBY,       //update state in lobby (votes and chat messages)
        UPDATE_INGAME,      //update state ingame (character input and chat messages)

        PERMISSIONS,        //tell the client which special permissions they have (if any)
        ACHIEVEMENT,        //give the client a steam achievement
        CHEATS_ENABLED,     //tell the clients whether cheats are on or off

        CAMPAIGN_SETUP_INFO,

        FILE_TRANSFER,

        VOICE,

        PING_REQUEST,       //ping the client
        CLIENT_PINGS,       //tell the client the pings of all other clients

        QUERY_STARTGAME,    //ask the clients whether they're ready to start
        STARTGAME,          //start a new round
        STARTGAMEFINALIZE,  //finalize round initialization
        ENDGAME,

        TRAITOR_MESSAGE,
        MISSION,
        EVENTACTION,
        CREW,               //anything related to managing bots in multiplayer
        MEDICAL,            //medical clinic
        MONEY,
        READY_CHECK         //start, end and update a ready check
    }
    enum ServerNetSegment
    {
        SyncIds,
        ChatMessage,
        Vote,
        ClientList,
        EntityPosition,
        EntityEvent,
        EntityEventInitial
    }

    enum TraitorMessageType
    {
        Server,
        ServerMessageBox,
        Objective,
        Console
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound,
        Kick,
        StartRound,
        PurchaseAndSwitchSub,
        PurchaseSub,
        SwitchSub,
        TransferMoney
    }

    public enum ReadyCheckState
    {
        Start,
        Update,
        End
    }

    enum DisconnectReason
    {
        //do not attempt reconnecting with these reasons
        Unknown,
        Disconnected,
        Banned,
        Kicked,
        ServerShutdown,
        ServerCrashed,
        ServerFull,
        AuthenticationRequired,
        SteamAuthenticationFailed,
        SessionTaken,
        TooManyFailedLogins,
        InvalidName,
        NameTaken,
        InvalidVersion,
        SteamP2PError,
        
        //attempt reconnecting with these reasons
        Timeout,
        ExcessiveDesyncOldEvent,
        ExcessiveDesyncRemovedEvent,
        SyncTimeout,
        SteamP2PTimeOut
    }

    abstract partial class NetworkMember
    {
        public UInt16 LastClientListUpdateID
        {
            get;
            set;
        }

        public abstract bool IsServer { get; }

        public abstract bool IsClient { get; }

        public abstract void CreateEntityEvent(INetSerializable entity, NetEntityEvent.IData extraData = null);

        public abstract Voting Voting { get; }

        protected DateTime updateTimer;

        public bool ShowNetStats;
        
        public float SimulatedRandomLatency, SimulatedMinimumLatency;
        public float SimulatedLoss;
        public float SimulatedDuplicatesChance;

        public KarmaManager KarmaManager
        {
            get;
            private set;
        } = new KarmaManager();

        public bool GameStarted { get; protected set; }

        public abstract IReadOnlyList<Client> ConnectedClients { get; }

        public RespawnManager RespawnManager { get; protected set; }

        public ServerSettings ServerSettings { get; protected set; }
        
        public TimeSpan UpdateInterval => new TimeSpan(0, 0, 0, 0, MathHelper.Clamp(1000 / ServerSettings.TickRate, 1, 500));


        public bool CanUseRadio(Character sender)
        {
            if (sender == null) { return false; }

            var radio = sender.Inventory.AllItems.FirstOrDefault(i => i.GetComponent<WifiComponent>() != null);
            if (radio == null || !sender.HasEquippedItem(radio)) { return false; }

            var radioComponent = radio.GetComponent<WifiComponent>();
            if (radioComponent == null) { return false; }
            return radioComponent.HasRequiredContainedItems(sender, addMessage: false);
        }

        public void AddChatMessage(string message, ChatMessageType type, string senderName = "", Client senderClient = null, Character senderCharacter = null, PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None, Color? textColor = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderCharacter, senderClient, changeType: changeType, textColor: textColor));
        }

        public virtual void AddChatMessage(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Text)) { return; }

            if (message.Sender != null && !message.Sender.IsDead)
            {
                message.Sender.ShowSpeechBubble(2.0f, message.Color);
            }
        }

        public static string ClientLogName(Client client, string name = null)
        {
            if (client == null) { return name; }
            string retVal = "‖";
            if (client.Karma < 40.0f)
            {
                retVal += "color:#ff9900;";
            }
            retVal += "metadata:" + (client.AccountId.TryUnwrap(out var accountId) ? accountId.ToString() : client.SessionId.ToString())
                                  + "‖" + (name ?? client.Name).Replace("‖", "") + "‖end‖";
            return retVal;
        }

        public abstract void KickPlayer(string kickedName, string reason);

        public abstract void BanPlayer(string kickedName, string reason, TimeSpan? duration = null);

        public abstract void UnbanPlayer(string playerName);
        
        public abstract void UnbanPlayer(Endpoint endpoint);

        /// <summary>
        /// Check if the two version are compatible (= if they can play together in multiplayer).
        /// </summary>
        public static bool IsCompatible(Version myVersion, Version remoteVersion)
        {
            //major.minor.build.revision
            //revision number is ignored, other values have to match
            return
                myVersion.Major == remoteVersion.Major &&
                myVersion.Minor == remoteVersion.Minor &&
                myVersion.Build == remoteVersion.Build;
        }
    }
}
