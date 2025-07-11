﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    public enum ClientPacketHeader
    {
        UPDATE_LOBBY,   //update state in lobby
        UPDATE_INGAME,  //update state ingame

        SERVER_SETTINGS, //change server settings
        SERVER_SETTINGS_PERKS, //change disembark perks (has different permissions from the rest of server settings)

        CAMPAIGN_SETUP_INFO,

        FILE_REQUEST,   //request a (submarine) file from the server

        VOICE,

        PING_RESPONSE,

        RESPONSE_CANCEL_STARTGAME, //tell the server you do not wish to start with the given warnings active

        RESPONSE_STARTGAME, //tell the server whether you're ready to start
        SERVER_COMMAND,     //tell the server to end a round or kick/ban someone (special permissions required)

        ENDROUND_SELF, //the client wants to end the round for themselves only and return to the lobby

        EVENTMANAGER_RESPONSE,

        REQUEST_STARTGAMEFINALIZE, //tell the server you're ready to finalize round initialization

        UPDATE_CHARACTERINFO,

        ERROR,           //tell the server that an error occurred
        CREW,            //hiring UI
        MEDICAL,         //medical clinic
        TRANSFER_MONEY,              // wallet transfers
        REWARD_DISTRIBUTION,         // wallet reward distribution
        RESET_REWARD_DISTRIBUTION,
        CIRCUITBOX,
        READY_CHECK,
        READY_TO_SPAWN,
        TAKEOVERBOT,
        TOGGLE_RESERVE_BENCH,

        REQUEST_BACKUP_INDICES // client wants a list of available backups for a save file
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
        ACHIEVEMENT_STAT,   //increment stat for an achievement
        CHEATS_ENABLED,     //tell the clients whether cheats are on or off

        CAMPAIGN_SETUP_INFO,

        FILE_TRANSFER,

        VOICE,
        VOICE_AMPLITUDE_DEBUG,

        PING_REQUEST,       //ping the client
        CLIENT_PINGS,       //tell the client the pings of all other clients

        QUERY_STARTGAME,    //ask the clients whether they're ready to start
        WARN_STARTGAME,     //round is about to start with invalid (perk) settings, warn the clients before starting
        CANCEL_STARTGAME,   //someone requested the round start to be cancelled due to invalid settings, tell the other clients
        STARTGAME,          //start a new round
        STARTGAMEFINALIZE,  //finalize round initialization
        ENDGAME,

        MISSION,
        EVENTACTION,
        TRAITOR_MESSAGE,
        CREW,               //anything related to managing bots in multiplayer
        MEDICAL,            //medical clinic
        CIRCUITBOX,
        MONEY,
        READY_CHECK,        //start, end and update a ready check
        UNLOCKRECIPE,       //unlocking a fabrication recipe

        SEND_BACKUP_INDICES // the server sends a list of available backups for a save file
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

    [NetworkSerialize]
    readonly record struct EntityPositionHeader(
        bool IsItem,
        UInt32 PrefabUintIdentifier,
        UInt16 EntityId) : INetSerializableStruct
    {
        public static EntityPositionHeader FromEntity(Entity entity)
            => new (
                IsItem: entity is Item,
                PrefabUintIdentifier: entity is MapEntity me ? me.Prefab.UintIdentifier : 0,
                EntityId: entity.ID);
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
        TransferMoney,
        Traitor,
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
        AuthenticationFailed,
        SessionTaken,
        TooManyFailedLogins,
        InvalidName,
        NameTaken,
        InvalidVersion,
        SteamP2PError,
        MalformedData,
        
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

        public void AddChatMessage(string message, ChatMessageType type, string senderName = "", Client senderClient = null, Entity senderEntity = null, PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None, Color? textColor = null)
        {
            AddChatMessage(ChatMessage.Create(senderName, message, type, senderEntity, senderClient, changeType: changeType, textColor: textColor));
        }

        public abstract void AddChatMessage(ChatMessage message);

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
