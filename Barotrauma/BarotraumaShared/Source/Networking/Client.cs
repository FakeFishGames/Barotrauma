using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class Client
    {
        public string Name;
        public byte ID;
        
        public byte TeamID = 0;

        private Character character;
        public Character Character
        {
            get { return character; }
            set
            {
                character = value;
                if (character != null) HasSpawned = true;
            }
        }
        public bool InGame;        
        public bool HasSpawned; //has the client spawned as a character during the current round
        
        private List<Client> kickVoters;

        public HashSet<string> GivenAchievements = new HashSet<string>();

        public ClientPermissions Permissions = ClientPermissions.None;
        public List<DebugConsole.Command> PermittedConsoleCommands
        {
            get;
            private set;
        }

        public bool SpectateOnly;
                
        private object[] votes;

        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }
        
        public Client(NetPeer server, string name, byte ID)
            : this(name, ID)
        {
            
        }

        partial void InitProjSpecific();
        public Client(string name, byte ID)
        {
            this.Name = name;
            this.ID = ID;

            PermittedConsoleCommands = new List<DebugConsole.Command>();
            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            InitProjSpecific();
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
        
        public bool HasKickVoteFrom(Client voter)
        {
            return kickVoters.Contains(voter);
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
