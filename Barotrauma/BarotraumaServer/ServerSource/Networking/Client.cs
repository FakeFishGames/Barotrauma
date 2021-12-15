using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class Client : IDisposable
    {
        public bool VoiceEnabled = true;

        public UInt16 LastRecvClientListUpdate = 0;

        public UInt16 LastSentServerSettingsUpdate = 0;
        public UInt16 LastRecvServerSettingsUpdate = 0;
        
        public UInt16 LastRecvLobbyUpdate = 0;

        public UInt16 LastSentChatMsgID = 0; //last msg this client said
        public UInt16 LastRecvChatMsgID = 0; //last msg this client knows about

        public UInt16 LastSentEntityEventID = 0;
        public UInt16 LastRecvEntityEventID = 0;

        public UInt16 LastRecvCampaignUpdate = 0;
        public UInt16 LastRecvCampaignSave = 0;

        public Pair<UInt16, float> LastCampaignSaveSendTime;

        public readonly List<ChatMessage> ChatMsgQueue = new List<ChatMessage>();
        public UInt16 LastChatMsgQueueID;

        //latest chat messages sent by this client
        public readonly List<string> LastSentChatMessages = new List<string>();
        public float ChatSpamSpeed;
        public float ChatSpamTimer;
        public int ChatSpamCount;

        public int RoundsSincePlayedAsTraitor;

        public float KickAFKTimer;

        public double MidRoundSyncTimeOut;

        public bool NeedsMidRoundSync;
        //how many unique events the client missed before joining the server
        public UInt16 UnreceivedEntityEventCount;
        public UInt16 FirstNewEventID;
        
        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public readonly Dictionary<UInt16, double> EntityEventLastSent = new Dictionary<UInt16, double>();
        
        //when was a position update for a given entity last sent to the client
        //  key = entity, value = NetTime.Now when sending
        public readonly Dictionary<Entity, float> PositionUpdateLastSent = new Dictionary<Entity, float>();
        public readonly Queue<Entity> PendingPositionUpdates = new Queue<Entity>();

        public bool ReadyToStart;

        public List<Pair<JobPrefab, int>> JobPreferences;
        public Pair<JobPrefab, int> AssignedJob;

        public float DeleteDisconnectedTimer;

        private CharacterInfo characterInfo;
        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set
            {
                if (characterInfo == value) { return; }
                characterInfo?.Remove();
                characterInfo = value;
            }
        }
        public NetworkConnection Connection { get; set; }

        public bool SpectateOnly;
        public bool? WaitForNextRoundRespawn;

        public int KarmaKickCount;

        private float syncedKarma = 100.0f;
        private float karma = 100.0f;
        public float Karma
        {
            get
            {
                if (GameMain.Server == null || !GameMain.Server.ServerSettings.KarmaEnabled) { return 100.0f; }
                if (HasPermission(ClientPermissions.KarmaImmunity)) { return 100.0f; }
                return karma;
            }
            set
            {
                if (GameMain.Server == null || !GameMain.Server.ServerSettings.KarmaEnabled) { return; }
                karma = Math.Min(Math.Max(value, 0.0f), 100.0f);
                if (!MathUtils.NearlyEqual(karma, syncedKarma, 10.0f))
                {
                    syncedKarma = karma;
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
            }
        }

        partial void InitProjSpecific()
        {
            JobPreferences = new List<Pair<JobPrefab, int>>();

            VoipQueue = new VoipQueue(ID, true, true);
            GameMain.Server.VoipServer.RegisterQueue(VoipQueue);

            //initialize to infinity, gets set to a proper value when initializing midround syncing
            MidRoundSyncTimeOut = double.PositiveInfinity;
        }

        partial void DisposeProjSpecific()
        {
            GameMain.Server.VoipServer.UnregisterQueue(VoipQueue);
            VoipQueue.Dispose();
            characterInfo?.Remove();
            characterInfo = null;
        }

        public void InitClientSync()
        {
            LastSentChatMsgID = 0;
            LastRecvChatMsgID = ChatMessage.LastID;

            LastRecvLobbyUpdate = 0;

            LastRecvEntityEventID = 0;

            UnreceivedEntityEventCount = 0;
            NeedsMidRoundSync = false;
        }

        public static bool IsValidName(string name, ServerSettings serverSettings)
        {
            if (string.IsNullOrWhiteSpace(name)) { return false; }
            
            char[] disallowedChars = new char[] { ';', ',', '<', '>', '/', '\\', '[', ']', '"', '?' };
            if (name.Any(c => disallowedChars.Contains(c))) { return false; }

            foreach (char character in name)
            {
                if (!serverSettings.AllowedClientNameChars.Any(charRange => (int)character >= charRange.First && (int)character <= charRange.Second)) { return false; }
            }

            return true;
        }

        public bool EndpointMatches(string endPoint)
        {
            return Connection.EndpointMatches(endPoint);
        }

        public void SetPermissions(ClientPermissions permissions, List<DebugConsole.Command> permittedConsoleCommands)
        {
            this.Permissions = permissions;
            this.PermittedConsoleCommands = new List<DebugConsole.Command>(permittedConsoleCommands);
        }

        public void GivePermission(ClientPermissions permission)
        {
            if (!this.Permissions.HasFlag(permission)) this.Permissions |= permission;
        }

        public void RemovePermission(ClientPermissions permission)
        {
            this.Permissions &= ~permission;
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return this.Permissions.HasFlag(permission);
        }
    }
}
