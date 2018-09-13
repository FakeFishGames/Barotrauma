using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    enum SelectionMode
    {
        Manual = 0, Random = 1, Vote = 2
    }

    enum YesNoMaybe
    {
        No = 0, Maybe = 1, Yes = 2
    }

    enum BotSpawnMode
    {
        Normal, Fill
    }

    partial class ServerSettings : ISerializableEntity
    {
        public string Name
        {
            get { return "ServerSettings"; }
        }

        public class SavedClientPermission
        {
            public readonly string IP;
            public readonly ulong SteamID;
            public readonly string Name;
            public List<DebugConsole.Command> PermittedCommands;

            public ClientPermissions Permissions;

            public SavedClientPermission(string name, string ip, ClientPermissions permissions, List<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.IP = ip;

                this.Permissions = permissions;
                this.PermittedCommands = permittedCommands;
            }
            public SavedClientPermission(string name, ulong steamID, ClientPermissions permissions, List<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.SteamID = steamID;

                this.Permissions = permissions;
                this.PermittedCommands = permittedCommands;
            }
        }
        
        partial class NetPropertyData
        {
            SerializableProperty property;
            string typeString;
            public string Name
            {
                get { return property.Name; }
            }
            
            public NetPropertyData(SerializableProperty property,string typeString)
            {
                this.property = property;
                this.typeString = typeString;
            }
        };

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        Dictionary<UInt32,NetPropertyData> netProperties;

        partial void InitProjSpecific();

        public ServerSettings(string serverName, int port, int queryPort, int maxPlayers, bool isPublic, bool enableUPnP)
        {
            ServerName = serverName;
            Port = port;
            QueryPort = queryPort;
            EnableUPnP = enableUPnP;
            this.maxPlayers = maxPlayers;
            this.isPublic = isPublic;
            
            ServerLog = new ServerLog(serverName);
            
            Voting = new Voting();

            Whitelist = new WhiteList();
            BanList = new BanList();

            InitProjSpecific();

            netProperties = new Dictionary<UInt32, NetPropertyData>();

            using (MD5 md5 = MD5.Create())
            {
                var saveProperties = SerializableProperty.GetProperties<Serialize>(this);
                foreach (var property in saveProperties)
                {
                    object value = property.GetValue();
                    if (value == null) continue;

                    string typeName = SerializableProperty.GetSupportedTypeName(value.GetType());
                    if (typeName != null || property.PropertyType.IsEnum)
                    {
                        NetPropertyData netPropertyData = new NetPropertyData(property, typeName);

                        //calculate key based on MD5 hash instead of string.GetHashCode
                        //to ensure consistent results across platforms
                        byte[] inputBytes = Encoding.ASCII.GetBytes(property.Name);
                        byte[] hash = md5.ComputeHash(inputBytes);

                        UInt32 key = (UInt32)((property.Name.Length & 0xff) << 24); //could use more of the hash here instead?
                        key |= (UInt32)(hash[hash.Length - 3] << 16);
                        key |= (UInt32)(hash[hash.Length - 2] << 8);
                        key |= (UInt32)(hash[hash.Length - 1]);

                        if (netProperties.ContainsKey(key)) throw new Exception("Hashing collision in ServerSettings.netProperties: " + netProperties[key] + " has same key as " + property.Name + " (" + key.ToString() + ")");

                        netProperties.Add(key, netPropertyData);
                    }
                }
            }
        }

        public string ServerName;

        public string ServerMessageText;

        public int Port;

        public int QueryPort;

        public bool EnableUPnP;

        public ServerLog ServerLog;

        public Voting Voting;

        public Dictionary<string, bool> monsterEnabled;

        public Dictionary<ItemPrefab, int> extraCargo;

        public bool ShowNetStats;

        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);

        private SelectionMode subSelectionMode, modeSelectionMode;

        private float selectedLevelDifficulty;
        private string password;

        public float AutoRestartTimer;

        private bool autoRestart;

        public bool isPublic;

        private int maxPlayers;

        public List<SavedClientPermission> ClientPermissions { get; private set; } = new List<SavedClientPermission>();

        public WhiteList Whitelist { get; private set; }

        [Serialize(true, true)]
        public bool RandomizeSeed
        {
            get;
            set;
        }

        [Serialize(300.0f, true)]
        public float RespawnInterval
        {
            get;
            private set;
        }

        [Serialize(180.0f, true)]
        public float MaxTransportTime
        {
            get;
            private set;
        }

        [Serialize(0.2f, true)]
        public float MinRespawnRatio
        {
            get;
            private set;
        }


        [Serialize(60.0f, true)]
        public float AutoRestartInterval
        {
            get;
            set;
        }

        [Serialize(true, true)]
        public bool AllowSpectating
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool EndRoundAtLevelEnd
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool SaveServerLogs
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool AllowRagdollButton
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool AllowFileTransfers
        {
            get;
            private set;
        }

        [Serialize(800, true)]
        private int LinesPerLogFile
        {
            get
            {
                return ServerLog.LinesPerFile;
            }
            set
            {
                ServerLog.LinesPerFile = value;
            }
        }

        public bool AutoRestart
        {
            get { return autoRestart; }
            set
            {
                autoRestart = value;

                AutoRestartTimer = autoRestart ? AutoRestartInterval : 0.0f;
            }
        }

        public bool HasPassword
        {
            get { return !string.IsNullOrEmpty(password); }
        }

        [Serialize(true, true)]
        public bool AllowRespawn
        {
            get;
            set;
        }
        
        [Serialize(0, true)]
        public int BotCount
        {
            get;
            set;
        }

        [Serialize(16, true)]
        public int MaxBotCount
        {
            get;
            set;
        }
        
        public BotSpawnMode BotSpawnMode
        {
            get;
            set;
        }

        public float SelectedLevelDifficulty
        {
            get { return selectedLevelDifficulty; }
            set { selectedLevelDifficulty = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        [Serialize(true, true)]
        public bool AllowDisguises
        {
            get;
            set;
        }

        public YesNoMaybe TraitorsEnabled
        {
            get;
            set;
        }

        public SelectionMode SubSelectionMode
        {
            get { return subSelectionMode; }
        }

        public SelectionMode ModeSelectionMode
        {
            get { return modeSelectionMode; }
        }

        public BanList BanList { get; private set; }

        [Serialize(true, true)]
        public bool AllowVoteKick
        {
            get;
            private set;
        }

        [Serialize(0.6f, true)]
        public float EndVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(0.6f, true)]
        public float KickVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(30.0f, true)]
        public float KillDisconnectedTime
        {
            get;
            private set;
        }

        [Serialize(true, true)]
        public bool TraitorUseRatio
        {
            get;
            private set;
        }

        [Serialize(0.2f, true)]
        public float TraitorRatio
        {
            get;
            private set;
        }

        [Serialize(false, true)]
        public bool KarmaEnabled
        {
            get;
            set;
        }

        [Serialize("Sandbox", true)]
        public string GameMode
        {
            get;
            set;
        }

        [Serialize("Random", true)]
        public string MissionType
        {
            get;
            set;
        }
        
        public int MaxPlayers
        {
            get { return maxPlayers; }
        }

        public List<MissionType> AllowedRandomMissionTypes
        {
            get;
            set;
        }

        [Serialize(60f, true)]
        public float AutoBanTime
        {
            get;
            private set;
        }

        [Serialize(360f, true)]
        public float MaxAutoBanTime
        {
            get;
            private set;
        }
        
        public void SetPassword(string password)
        {
            this.password = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)));
        }

        public bool IsPasswordCorrect(string input,int nonce)
        {
            if (!HasPassword) return true;
            string saltedPw = password;
            saltedPw = saltedPw + Convert.ToString(nonce);
            saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));
            return input == password;
        }

        /// <summary>
        /// A list of int pairs that represent the ranges of UTF-16 codes allowed in client names
        /// </summary>
        public List<Pair<int, int>> AllowedClientNameChars
        {
            get;
            private set;
        } = new List<Pair<int, int>>();
        
        private void SharedWrite(NetBuffer outMsg)
        {
            outMsg.Write(ServerName);
            outMsg.Write((UInt16)Port);
            outMsg.Write((UInt16)maxPlayers);
            outMsg.Write(ServerName);
            outMsg.Write(ServerMessageText);
        }

        private void SharedRead(NetBuffer incMsg)
        {
            ServerName = incMsg.ReadString();
            Port = incMsg.ReadUInt16();
            maxPlayers = incMsg.ReadUInt16();
            ServerName = incMsg.ReadString();
            ServerMessageText = incMsg.ReadString();
        }
    }
}
