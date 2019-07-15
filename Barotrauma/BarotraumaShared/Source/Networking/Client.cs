using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class Client : IDisposable
    {
        public const int MaxNameLength = 20;

        public string Name;
        public byte ID;
        
        public Character.TeamType TeamID;

        private Character character;
        public Character Character
        {
            get { return character; }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
                else
                {
                    if (value!=null)
                    {
                        DebugConsole.NewMessage(value.Name, Microsoft.Xna.Framework.Color.Yellow);
                    }
                }
                character = value;
                if (character != null)
                {
                    HasSpawned = true;
#if CLIENT
                    GameMain.GameSession?.CrewManager?.SetPlayerVoiceIconState(this, muted, mutedLocally);
#endif
                }
            }
        }

        private Vector2 spectate_position;
        public Vector2? SpectatePos
        {
            get
            {
                if (IsCinematicRunning() && Submarine.MainSub != null) return Submarine.MainSub.WorldPosition;
                if (character == null || character.IsDead)
                {
                    return spectate_position;
                }
                else
                {
                    return null;
                }
            }

            set
            {
                spectate_position = value.Value;
            }
        }

        private bool IsCinematicRunning()
        {
#if SERVER
            return GameMain.Server.EndCinematic != null;
#endif
#if CLIENT
            return GameMain.Client.EndCinematic != null;
#endif
        }

        private bool muted;
        public bool Muted
        {
            get { return muted; }
            set
            {
                if (muted == value) { return; }
                muted = value;
#if CLIENT
                GameMain.NetLobbyScreen.SetPlayerVoiceIconState(this, muted, mutedLocally);
                GameMain.GameSession?.CrewManager?.SetPlayerVoiceIconState(this, muted, mutedLocally);
#endif
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
            }
        }

        public VoipQueue VoipQueue
        {
            get;
            private set;
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

        private object[] votes;

        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }
        
        /*public Client(NetPeer server, string name, byte ID)
            : this(name, ID)
        {
            
        }*/

        partial void InitProjSpecific();
        partial void DisposeProjSpecific();
        public Client(string name, byte ID)
        {
            this.Name = name;
            this.ID = ID;

            PermittedConsoleCommands = new List<DebugConsole.Command>();
            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            InitProjSpecific();
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

        public void WritePermissions(NetBuffer msg)
        {
            msg.Write(ID);
            msg.Write((UInt16)Permissions);
            if (HasPermission(ClientPermissions.ConsoleCommands))
            {
                msg.Write((UInt16)PermittedConsoleCommands.Count);
                foreach (DebugConsole.Command command in PermittedConsoleCommands)
                {
                    msg.Write(command.names[0]);
                }
            }
        }
        public static void ReadPermissions(NetBuffer inc, out ClientPermissions permissions, out List<DebugConsole.Command> permittedCommands)
        {
            UInt16 permissionsInt = inc.ReadUInt16();

            permissions = ClientPermissions.None;
            permittedCommands = new List<DebugConsole.Command>();
            try
            {
                permissions = (ClientPermissions)permissionsInt;
            }
            catch (InvalidCastException)
            {
                return;
            }
            if (permissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                UInt16 commandCount = inc.ReadUInt16();
                for (int i = 0; i < commandCount; i++)
                {
                    string commandName = inc.ReadString();
                    var consoleCommand = DebugConsole.Commands.Find(c => c.names.Contains(commandName));
                    if (consoleCommand != null)
                    {
                        permittedCommands.Add(consoleCommand);
                    }
                }
            }
        }

        public void ReadPermissions(NetIncomingMessage inc)
        {
            ClientPermissions permissions = ClientPermissions.None;
            List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
            ReadPermissions(inc, out permissions, out permittedCommands);
            SetPermissions(permissions, permittedCommands);
        }

        public static string SanitizeName(string name)
        {
            name = name.Trim();
            if (name.Length > MaxNameLength)
            {
                name = name.Substring(0, MaxNameLength);
            }
            string rName = "";
            for (int i = 0; i < name.Length; i++)
            {
                rName += name[i] < 32 ? '?' : name[i];
            }
            return rName;
        }

        public void Dispose()
        {
            DisposeProjSpecific();
        }
    }
}
