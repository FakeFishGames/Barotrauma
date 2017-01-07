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
        Ban = 4
    }

    class Client
    {
        public string name;
        public byte ID;

        public byte TeamID = 0;

        public Character Character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public string version;
        public bool inGame;
        public UInt32 lastRecvGeneralUpdate = 0;

        public bool hasLobbyData = false;
        public UInt32 lastSentChatMsgID = 0; //last msg this client said
        public UInt32 lastRecvChatMsgID = 0; //last msg this client knows about

        public UInt32 lastSentEntityEventID = 0;
        public UInt32 lastRecvEntityEventID = 0;

        public UInt32 lastRecvEntitySpawnID = 0;

        public List<ChatMessage> chatMsgQueue = new List<ChatMessage>();
        public float ChatSpamSpeed;
        public float ChatSpamTimer;
        public int ChatSpamCount;

        private List<Client> kickVoters;

        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public Dictionary<UInt32, float> entityEventLastSent;

        public bool ReadyToStart;

        private object[] votes;

        public List<JobPrefab> jobPreferences;
        public JobPrefab assignedJob;
        
        public float deleteDisconnectedTimer;

        public ClientPermissions Permissions = ClientPermissions.None;
        
        public void InitClientSync()
        {
            lastSentChatMsgID = 0;
            lastRecvChatMsgID = ChatMessage.LastID;

            lastRecvEntitySpawnID = 0;
            lastRecvEntityEventID = 0;
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

            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0, 3));

            entityEventLastSent = new Dictionary<UInt32, float>();
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
            for (int i=0;i<name.Length;i++)
            {
                if (name[i] < 32 || name[i] > 126)
                {
                    //TODO: allow safe unicode characters, this is just to prevent players from taking names that look similar but aren't the same
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
