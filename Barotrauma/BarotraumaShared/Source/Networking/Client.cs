using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class Client
    {
        //NilMod
        public Boolean IsNilModClient = false;
        public Boolean RequiresNilModSync = false;
        public float NilModSyncResendTimer = 3f;

        public string Name;
        public byte ID;

        private float karma = 1.0f;
        public float Karma
        {
            get
            {
                if (GameMain.Server == null) return 1.0f;
                if (!GameMain.Server.KarmaEnabled) return 1.0f;
                return karma;
            }
            set
            {
                if (GameMain.Server == null) return;
                if (!GameMain.Server.KarmaEnabled) return;
                karma = Math.Min(Math.Max(value,0.0f),1.0f);
            }
        }

        public byte TeamID = 0;

        public Character Character;
        public CharacterInfo CharacterInfo;
        public NetConnection Connection { get; set; }
        public bool InGame;
        
        public UInt16 LastRecvGeneralUpdate = 0;
        
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
        
        public double MidRoundSyncTimeOut;

        public bool NeedsMidRoundSync;
        //how many unique events the client missed before joining the server
        public UInt16 UnreceivedEntityEventCount;
        public UInt16 FirstNewEventID;

        private List<Client> kickVoters;

        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public readonly Dictionary<UInt16, float> EntityEventLastSent = new Dictionary<UInt16, float>();

        public readonly Queue<Entity> PendingPositionUpdates = new Queue<Entity>();
        
        public bool ReadyToStart;

        public List<JobPrefab> JobPreferences;
        public JobPrefab AssignedJob;
        
        public float DeleteDisconnectedTimer;

        public ClientPermissions Permissions = ClientPermissions.None;
        public List<DebugConsole.Command> PermittedConsoleCommands
        {
            get;
            private set;
        }

        public bool SpectateOnly;

        public int PreferredTeam = 0;
                
        private object[] votes;

        public void InitClientSync()
        {
            LastSentChatMsgID = 0;
            LastRecvChatMsgID = ChatMessage.LastID;

            LastRecvGeneralUpdate = 0;
            
            LastRecvEntityEventID = 0;

            UnreceivedEntityEventCount = 0;
            NeedsMidRoundSync = false;
        }

        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }
        
        public Client(NetPeer server, string name, byte ID)
            : this(name, ID)
        {
            
        }

        public Client(string name, byte ID)
        {
            this.Name = name;
            this.ID = ID;

            PermittedConsoleCommands = new List<DebugConsole.Command>();
            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            JobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0, Math.Min(JobPrefab.List.Count, 3)));
        }

        public static bool IsValidName(string name)
        {
            if (name.Contains("\n") || name.Contains("\r")) return false;

            return (name.All(c =>
                c != ';' &&
                c != ',' &&
                c != '<' &&
                c != '/'));
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
                if (name[i] < 32)
                {
                    rName += '?';
                }
                else
                {
                    rName += name[i];
                }
            }

            return rName;
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

        public T GetVote<T>(VoteType voteType)
        {
            return (votes[(int)voteType] is T) ? (T)votes[(int)voteType] : default(T);
        }

        public void SetVote(VoteType voteType, object value)
        {
            votes[(int)voteType] = value;
        }

        public void ResetVotes()
        {
            for (int i = 0; i < votes.Length; i++)
            {
                votes[i] = null;
            }
        }

        public void AddKickVote(Client voter)
        {
            if (!kickVoters.Contains(voter)) kickVoters.Add(voter);
        }


        public void RemoveKickVote(Client voter)
        {
            kickVoters.Remove(voter);
        }

        public bool HasKickVoteFromID(int id)
        {
            return kickVoters.Any(k => k.ID == id);
        }


        public static void UpdateKickVotes(List<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                client.kickVoters.RemoveAll(voter => !connectedClients.Contains(voter));
            }
        }
        
    }
}
