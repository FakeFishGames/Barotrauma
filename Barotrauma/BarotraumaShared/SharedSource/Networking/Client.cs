using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    [NetworkSerialize]
    struct TempClient : INetSerializableStruct
    {
        public string Name;
        public Identifier PreferredJob;
        public CharacterTeamType PreferredTeam;
        public UInt16 NameId;
        public AccountInfo AccountInfo;
        public byte SessionId;
        public UInt16 CharacterId;
        public float Karma;
        public bool Muted;
        public bool InGame;
        public bool HasPermissions;
        public bool IsOwner;
        public bool IsDownloading;
    }
    
    partial class Client : IDisposable
    {
        public const int MaxNameLength = 32;

        public string Name; public UInt16 NameId;
        
        /// <summary>
        /// An ID for this client for the current session.
        /// THIS IS NOT A PERSISTENT VALUE. DO NOT STORE THIS LONG-TERM.
        /// IT CANNOT BE USED TO IDENTIFY PLAYERS ACROSS SESSIONS.
        /// </summary>
        public readonly byte SessionId;

        public AccountInfo AccountInfo;
        
        /// <summary>
        /// The ID of the account used to authenticate this session.
        /// This value can be used as a persistent value to identify
        /// players in the banlist and campaign saves.
        /// </summary>
        public Option<AccountId> AccountId => AccountInfo.AccountId;

        public LanguageIdentifier Language;

        public UInt16 Ping;

        public Identifier PreferredJob;

        public CharacterTeamType TeamID;

        public CharacterTeamType PreferredTeam;

        private Character character;
        public Character Character
        {
            get
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient && (character?.ID ?? 0) != CharacterID)
                {
                    Character = Entity.FindEntityByID(CharacterID) as Character;
                }
                return character;
            }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.LastClientListUpdateID++;
                    if (value != null)
                    {
                        CharacterID = value.ID;
                    }
                }
                else
                {
                    if (value != null)
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

                    if (character == GameMain.Client.Character && GameMain.Client.SpawnAsTraitor)
                    {
                        character.IsTraitor = true;
                        character.TraitorCurrentObjective = GameMain.Client.TraitorFirstObjective;
                    }
#endif
                }
            }
        }

        public UInt16 CharacterID;

        private Vector2 spectatePos;
        public Vector2? SpectatePos
        {
            get
            {
                if (character == null || character.IsDead)
                {
                    return spectatePos;
                }
                else
                {
                    return null;
                }
            }

            set
            {
                spectatePos = value.Value;
            }
        }

        public bool Spectating
        {
            get
            {
                return inGame && character == null;
            }
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

        public bool HasPermissions => Permissions != ClientPermissions.None;

        public VoipQueue VoipQueue
        {
            get;
            private set;
        }

        private bool inGame;
        public bool InGame
        {
            get
            {
                return inGame;
            }
            set
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.LastClientListUpdateID++;
                }
                inGame = value;
            }
        }
        public bool HasSpawned; //has the client spawned as a character during the current round
        
        public HashSet<Identifier> GivenAchievements = new HashSet<Identifier>();

        public ClientPermissions Permissions = ClientPermissions.None;
        public readonly HashSet<DebugConsole.Command> PermittedConsoleCommands = new HashSet<DebugConsole.Command>();

        private readonly object[] votes;

        partial void InitProjSpecific();
        partial void DisposeProjSpecific();
        public Client(string name, byte sessionId)
        {
            this.Name = name;
            this.SessionId = sessionId;

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];

            InitProjSpecific();
        }

        public T GetVote<T>(VoteType voteType)
        {
            return (votes[(int)voteType] is T t) ? t : default;
        }

        public void SetVote(VoteType voteType, object value)
        {
            votes[(int)voteType] = value;
        }
                       
        public bool SessionOrAccountIdMatches(string userId)
            => (AccountId.IsSome() && Networking.AccountId.Parse(userId) == AccountId)
               || (byte.TryParse(userId, out byte sessionId) && SessionId == sessionId);

        public void WritePermissions(IWriteMessage msg)
        {
            msg.WriteByte(SessionId);
            msg.WriteRangedInteger((int)Permissions, 0, (int)ClientPermissions.All);
            if (HasPermission(ClientPermissions.ConsoleCommands))
            {
                msg.WriteUInt16((UInt16)PermittedConsoleCommands.Count);
                foreach (DebugConsole.Command command in PermittedConsoleCommands)
                {
                    msg.WriteString(command.names[0]);
                }
            }
        }
        public static void ReadPermissions(IReadMessage inc, out ClientPermissions permissions, out List<DebugConsole.Command> permittedCommands)
        {
            int permissionsInt = inc.ReadRangedInteger(0, (int)ClientPermissions.All);
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

        public void ReadPermissions(IReadMessage inc)
        {
            ReadPermissions(inc, out ClientPermissions permissions, out List<DebugConsole.Command> permittedCommands);
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
