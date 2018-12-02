using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    partial class Client
    {
        public ulong SteamID;

        public UInt16 LastRecvClientListUpdate = 0;

        public UInt16 LastRecvLobbyUpdate = 0;

        public UInt16 LastSentChatMsgID = 0; //last msg this client said
        public UInt16 LastRecvChatMsgID = 0; //last msg this client knows about

        public UInt16 LastSentEntityEventID = 0;
        public UInt16 LastRecvEntityEventID = 0;

        public UInt16 LastRecvCampaignUpdate = 0;
        public UInt16 LastRecvCampaignSave = 0;

        public readonly List<ChatMessage> ChatMsgQueue = new List<ChatMessage>();
        public UInt16 LastChatMsgQueueID;

        //latest chat messages sent by this client
        public readonly List<string> LastSentChatMessages = new List<string>();
        public float ChatSpamSpeed;
        public float ChatSpamTimer;
        public int ChatSpamCount;

        public float KickAFKTimer;

        public double MidRoundSyncTimeOut;

        public bool NeedsMidRoundSync;
        //how many unique events the client missed before joining the server
        public UInt16 UnreceivedEntityEventCount;
        public UInt16 FirstNewEventID;


        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public readonly Dictionary<UInt16, float> EntityEventLastSent = new Dictionary<UInt16, float>();

        public readonly Queue<Entity> PendingPositionUpdates = new Queue<Entity>();

        public bool ReadyToStart;

        public List<JobPrefab> JobPreferences;
        public JobPrefab AssignedJob;

        public float DeleteDisconnectedTimer;

        public CharacterInfo CharacterInfo;
        public NetConnection Connection { get; set; }

        public bool SpectateOnly;
        
        private float karma = 1.0f;
        public float Karma
        {
            get
            {
                if (GameMain.Server == null) return 1.0f;
                if (!GameMain.Server.ServerSettings.KarmaEnabled) return 1.0f;
                return karma;
            }
            set
            {
                if (GameMain.Server == null) return;
                if (!GameMain.Server.ServerSettings.KarmaEnabled) return;
                karma = Math.Min(Math.Max(value, 0.0f), 1.0f);
            }
        }

        partial void InitProjSpecific()
        {
            JobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0, Math.Min(JobPrefab.List.Count, 3)));
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

        public static bool IsValidName(string name, GameServer server)
        {
            char[] disallowedChars = new char[] { ';', ',', '<', '>', '/', '\\', '[', ']', '"', '?' };
            if (name.Any(c => disallowedChars.Contains(c))) return false;

            foreach (char character in name)
            {
                if (!server.ServerSettings.AllowedClientNameChars.Any(charRange => (int)character >= charRange.First && (int)character <= charRange.Second)) return false;
            }

            return true;
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
            if (this.Permissions.HasFlag(permission)) this.Permissions &= ~permission;
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return this.Permissions.HasFlag(permission);
        }

        public static string SanitizeName(string name)
        {
            name = name.Trim();
            if (name.Length > 20)
            {
                name = name.Substring(0, 20);
            }
            string rName = "";
            for (int i = 0; i < name.Length; i++)
            {
                rName += name[i] < 32 ? '?' : name[i];
            }
            return rName;
        }
    }
}
