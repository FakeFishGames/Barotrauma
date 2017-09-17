using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Barotrauma.Networking
{
    [Flags]
    enum ClientPermissions
    {
        None = 0,
        [Description("End round")]
        EndRound = 1,
        [Description("Kick")]
        Kick = 2,
        [Description("Ban")]
        Ban = 4,
        [Description("Manage campaign")]
        ManageCampaign = 8
    }

    class Client
    {
        public string name;
        public byte ID;

        public byte TeamID = 0;

        public Character Character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public bool inGame;
        public UInt16 lastRecvGeneralUpdate = 0;
        
        public UInt16 lastSentChatMsgID = 0; //last msg this client said
        public UInt16 lastRecvChatMsgID = 0; //last msg this client knows about

        public UInt16 lastSentEntityEventID = 0;
        public UInt16 lastRecvEntityEventID = 0;

        public UInt16 lastRecvCampaignUpdate = 0;

        public List<ChatMessage> chatMsgQueue = new List<ChatMessage>();
        public UInt16 lastChatMsgQueueID;


        //latest chat messages sent by this client
        public List<string> lastSentChatMessages = new List<string>(); 
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
        public Dictionary<UInt16, float> entityEventLastSent;
        
        private Queue<Entity> pendingPositionUpdates = new Queue<Entity>();

        public bool ReadyToStart;

        private object[] votes;

        public List<JobPrefab> jobPreferences;
        public JobPrefab assignedJob;
        
        public float deleteDisconnectedTimer;

        public ClientPermissions Permissions = ClientPermissions.None;

        public Queue<Entity> PendingPositionUpdates
        {
            get { return pendingPositionUpdates; }
        }
        
        public void InitClientSync()
        {
            lastSentChatMsgID = 0;
            lastRecvChatMsgID = ChatMessage.LastID;

            lastRecvGeneralUpdate = 0;
            
            lastRecvEntityEventID = 0;

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
            this.name = name;
            this.ID = ID;

            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0, Math.Min(JobPrefab.List.Count, 3)));

            entityEventLastSent = new Dictionary<UInt16, float>();
        }

        public static bool IsValidName(string name)
        {
            if (name.Contains("\n") || name.Contains("\r\n")) return false;

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

        public void SetPermissions(ClientPermissions permissions)
        {
            this.Permissions = permissions;
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
