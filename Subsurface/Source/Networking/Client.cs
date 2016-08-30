using Barotrauma.Networking.ReliableMessages;
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

        public Character Character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public string version;
        public bool inGame;

        public List<string> ChatMessages = new List<string>();
        public float ChatSpamSpeed;
        public float ChatSpamTimer;
        public int ChatSpamCount;

        private List<Client> kickVoters;

        public bool ReadyToStart;

        private object[] votes;

        public List<JobPrefab> jobPreferences;
        public JobPrefab assignedJob;

        public FileStreamSender FileStreamSender;
        
        public ReliableChannel ReliableChannel;

        public float deleteDisconnectedTimer;

        public ClientPermissions Permissions;
        
        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }

        public Client(NetPeer server, string name, byte ID)
            : this(name, ID)
        {
            ReliableChannel = new ReliableChannel(server);
        }

        public Client(string name, byte ID)
        {
            this.name = name;
            this.ID = ID;

            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0, 3));
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
            this.Permissions |= permission;
        }

        public void RemovePermission(ClientPermissions permission)
        {
            this.Permissions &= ~permission;
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return false; //Permissions.HasFlag(permission);
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

        public void CancelTransfer()
        {
            if (FileStreamSender == null) return;

            FileStreamSender.CancelTransfer();
            FileStreamSender.Dispose();

            FileStreamSender = null;
        }


    }
}
