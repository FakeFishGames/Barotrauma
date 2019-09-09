using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    public enum SelectionMode
    {
        Manual = 0, Random = 1, Vote = 2
    }

    public enum YesNoMaybe
    {
        No = 0, Maybe = 1, Yes = 2
    }

    public enum BotSpawnMode
    {
        Normal, Fill
    }

    public enum PlayStyle
    {
        Roleplay = 0,
        Casual = 1,
        Serious = 2,
        Rampage = 3,
        SomethingDifferent = 4
    }

    partial class ServerSettings : ISerializableEntity
    {
        public const string SettingsFile = "serversettings.xml";

        [Flags]
        public enum NetFlags : byte
        {
            Name = 0x1,
            Message = 0x2,
            Properties = 0x4,
            Misc = 0x8,
            LevelSeed = 0x10
        }

        public static readonly string PermissionPresetFile = "Data" + Path.DirectorySeparatorChar + "permissionpresets.xml";

        public string Name
        {
            get { return "ServerSettings"; }
        }

        /// <summary>
        /// Have some of the properties listed in the server list changed
        /// </summary>
        public bool ServerDetailsChanged;

        public class SavedClientPermission
        {
            public readonly string EndPoint;
            public readonly ulong SteamID;
            public readonly string Name;
            public List<DebugConsole.Command> PermittedCommands;

            public ClientPermissions Permissions;

            public SavedClientPermission(string name, string endpoint, ClientPermissions permissions, List<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.EndPoint = endpoint;
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
            private SerializableProperty property;
            private string typeString;

            private object parentObject;

            public string Name
            {
                get { return property.Name; }
            }

            public object Value
            {
                get { return property.GetValue(parentObject); }
                set { property.SetValue(parentObject, value); }
            }

            public NetPropertyData(object parentObject, SerializableProperty property, string typeString)
            {
                this.property = property;
                this.typeString = typeString;
                this.parentObject = parentObject;
            }

            public bool PropEquals(object a, object b)
            {
                switch (typeString)
                {
                    case "float":
                        if (!(a is float?)) return false;
                        if (!(b is float?)) return false;
                        return MathUtils.NearlyEqual((float)a, (float)b);
                    case "int":
                        if (!(a is int?)) return false;
                        if (!(b is int?)) return false;
                        return (int)a == (int)b;
                    case "bool":
                        if (!(a is bool?)) return false;
                        if (!(b is bool?)) return false;
                        return (bool)a == (bool)b;
                    case "Enum":
                        if (!(a is Enum)) return false;
                        if (!(b is Enum)) return false;
                        return ((Enum)a).Equals((Enum)b);
                    default:
                        return a.ToString().Equals(b.ToString(), StringComparison.InvariantCulture);
                }
            }

            public void Read(IReadMessage msg)
            {
                int oldPos = msg.BitPosition;
                UInt32 size = msg.ReadVariableUInt32();

                float x; float y; float z; float w;
                byte r; byte g; byte b; byte a;
                int ix; int iy; int width; int height;

                switch (typeString)
                {
                    case "float":
                        if (size != 4) break;
                        property.SetValue(parentObject, msg.ReadSingle());
                        return;
                    case "int":
                        if (size != 4) break;
                        property.SetValue(parentObject, msg.ReadInt32());
                        return;
                    case "vector2":
                        if (size != 8) break;
                        x = msg.ReadSingle();
                        y = msg.ReadSingle();
                        property.SetValue(parentObject, new Vector2(x, y));
                        return;
                    case "vector3":
                        if (size != 12) break;
                        x = msg.ReadSingle();
                        y = msg.ReadSingle();
                        z = msg.ReadSingle();
                        property.SetValue(parentObject, new Vector3(x, y, z));
                        return;
                    case "vector4":
                        if (size != 16) break;
                        x = msg.ReadSingle();
                        y = msg.ReadSingle();
                        z = msg.ReadSingle();
                        w = msg.ReadSingle();
                        property.SetValue(parentObject, new Vector4(x, y, z, w));
                        return;
                    case "color":
                        if (size != 4) break;
                        r = msg.ReadByte();
                        g = msg.ReadByte();
                        b = msg.ReadByte();
                        a = msg.ReadByte();
                        property.SetValue(parentObject, new Color(r, g, b, a));
                        return;
                    case "rectangle":
                        if (size != 16) break;
                        ix = msg.ReadInt32();
                        iy = msg.ReadInt32();
                        width = msg.ReadInt32();
                        height = msg.ReadInt32();
                        property.SetValue(parentObject, new Rectangle(ix, iy, width, height));
                        return;
                    default:
                        msg.BitPosition = oldPos; //reset position to properly read the string
                        string incVal = msg.ReadString();
                        property.TrySetValue(parentObject, incVal);
                        return;
                }

                //size didn't match: skip this
                msg.BitPosition += (int)(8 * size);
            }

            public void Write(IWriteMessage msg, object overrideValue = null)
            {
                if (overrideValue == null) overrideValue = property.GetValue(parentObject);
                switch (typeString)
                {
                    case "float":
                        msg.WriteVariableUInt32(4);
                        msg.Write((float)overrideValue);
                        break;
                    case "int":
                        msg.WriteVariableUInt32(4);
                        msg.Write((int)overrideValue);
                        break;
                    case "vector2":
                        msg.WriteVariableUInt32(8);
                        msg.Write(((Vector2)overrideValue).X);
                        msg.Write(((Vector2)overrideValue).Y);
                        break;
                    case "vector3":
                        msg.WriteVariableUInt32(12);
                        msg.Write(((Vector3)overrideValue).X);
                        msg.Write(((Vector3)overrideValue).Y);
                        msg.Write(((Vector3)overrideValue).Z);
                        break;
                    case "vector4":
                        msg.WriteVariableUInt32(16);
                        msg.Write(((Vector4)overrideValue).X);
                        msg.Write(((Vector4)overrideValue).Y);
                        msg.Write(((Vector4)overrideValue).Z);
                        msg.Write(((Vector4)overrideValue).W);
                        break;
                    case "color":
                        msg.WriteVariableUInt32(4);
                        msg.Write(((Color)overrideValue).R);
                        msg.Write(((Color)overrideValue).G);
                        msg.Write(((Color)overrideValue).B);
                        msg.Write(((Color)overrideValue).A);
                        break;
                    case "rectangle":
                        msg.WriteVariableUInt32(16);
                        msg.Write(((Rectangle)overrideValue).X);
                        msg.Write(((Rectangle)overrideValue).Y);
                        msg.Write(((Rectangle)overrideValue).Width);
                        msg.Write(((Rectangle)overrideValue).Height);
                        break;
                    default:
                        string strVal = overrideValue.ToString();

                        msg.Write(strVal);
                        break;
                }
            }
        };

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        Dictionary<UInt32, NetPropertyData> netProperties;

        partial void InitProjSpecific();

        public ServerSettings(NetworkMember networkMember, string serverName, int port, int queryPort, int maxPlayers, bool isPublic, bool enableUPnP)
        {
            ServerLog = new ServerLog(serverName);

            Voting = new Voting();

            Whitelist = new WhiteList();
            BanList = new BanList();

            ExtraCargo = new Dictionary<ItemPrefab, int>();

            PermissionPreset.LoadAll(PermissionPresetFile);
            InitProjSpecific();

            ServerName = serverName;
            Port = port;
            QueryPort = queryPort;
            EnableUPnP = enableUPnP;
            this.maxPlayers = maxPlayers;
            this.isPublic = isPublic;

            netProperties = new Dictionary<UInt32, NetPropertyData>();

            using (MD5 md5 = MD5.Create())
            {
                var saveProperties = SerializableProperty.GetProperties<Serialize>(this);
                foreach (var property in saveProperties)
                {
                    object value = property.GetValue(this);
                    if (value == null) { continue; }

                    string typeName = SerializableProperty.GetSupportedTypeName(value.GetType());
                    if (typeName != null || property.PropertyType.IsEnum)
                    {
                        NetPropertyData netPropertyData = new NetPropertyData(this, property, typeName);
                        UInt32 key = ToolBox.StringToUInt32Hash(property.Name, md5);
                        if (netProperties.ContainsKey(key)){ throw new Exception("Hashing collision in ServerSettings.netProperties: " + netProperties[key] + " has same key as " + property.Name + " (" + key.ToString() + ")"); }
                        netProperties.Add(key, netPropertyData);
                    }
                }

                var karmaProperties = SerializableProperty.GetProperties<Serialize>(networkMember.KarmaManager);
                foreach (var property in karmaProperties)
                {
                    object value = property.GetValue(networkMember.KarmaManager);
                    if (value == null) { continue; }

                    string typeName = SerializableProperty.GetSupportedTypeName(value.GetType());
                    if (typeName != null || property.PropertyType.IsEnum)
                    {
                        NetPropertyData netPropertyData = new NetPropertyData(networkMember.KarmaManager, property, typeName);
                        UInt32 key = ToolBox.StringToUInt32Hash(property.Name, md5);
                        if (netProperties.ContainsKey(key)) { throw new Exception("Hashing collision in ServerSettings.netProperties: " + netProperties[key] + " has same key as " + property.Name + " (" + key.ToString() + ")"); }
                        netProperties.Add(key, netPropertyData);
                    }
                }
            }
        }

        public string ServerName;

        private string serverMessageText;
        public string ServerMessageText
        {
            get { return serverMessageText; }
            set
            {
                if (serverMessageText == value) { return; }
                serverMessageText = value;
                ServerDetailsChanged = true;
            }
        }

        public int Port;

        public int QueryPort;

        public bool EnableUPnP;

        public ServerLog ServerLog;

        public Voting Voting;

        public Dictionary<string, bool> MonsterEnabled { get; private set; }

        public Dictionary<ItemPrefab, int> ExtraCargo { get; private set; }

        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);
        private float selectedLevelDifficulty;
        private byte[] password;

        public float AutoRestartTimer;

        private bool autoRestart;

        public bool isPublic;

        private int maxPlayers;

        public List<SavedClientPermission> ClientPermissions { get; private set; } = new List<SavedClientPermission>();

        public WhiteList Whitelist { get; private set; }

        [Serialize(20, true)]
        public int TickRate
        {
            get;
            set;
        }

        [Serialize(true, true)]
        public bool RandomizeSeed
        {
            get;
            set;
        }

        [Serialize(true, true)]
        public bool UseRespawnShuttle
        {
            get;
            private set;
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

        [Serialize(false, true)]
        public bool StartWhenClientsReady
        {
            get;
            set;
        }

        [Serialize(0.8f, true)]
        public float StartWhenClientsReadyRatio
        {
            get;
            private set;
        }

        private bool allowSpectating;
        [Serialize(true, true)]
        public bool AllowSpectating
        {
            get { return allowSpectating; }
            private set
            {
                if (allowSpectating == value) { return; }
                allowSpectating = value;
                ServerDetailsChanged = true;
            }
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
            set;
        }

        [Serialize(true, true)]
        public bool AllowFileTransfers
        {
            get;
            private set;
        }

        private bool voiceChatEnabled;
        [Serialize(true, true)]
        public bool VoiceChatEnabled
        {
            get { return voiceChatEnabled; }
            set
            {
                if (voiceChatEnabled == value) { return; }
                voiceChatEnabled = value;
                ServerDetailsChanged = true;
            }
        }

        [Serialize(PlayStyle.Serious, true)]
        public PlayStyle PlayStyle
        {
            get;
            set;
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
            get { return password != null; }
#if CLIENT
            set
            {
                password = value ? (password ?? new byte[1]) : null;
            }
#endif
        }

        [Serialize(true, true)]
        public bool AllowVoteKick
        {
            get
            {
                return Voting.AllowVoteKick;
            }
            set
            {
                Voting.AllowVoteKick = value;
            }
        }

        [Serialize(true, true)]
        public bool AllowEndVoting
        {
            get
            {
                return Voting.AllowEndVoting;
            }
            set
            {
                Voting.AllowEndVoting = value;
            }
        }

        private bool allowRespawn;
        [Serialize(true, true)]
        public bool AllowRespawn
        {
            get { return allowRespawn; ; }
            set
            {
                if (allowRespawn == value) { return; }
                allowRespawn = value;
                ServerDetailsChanged = true;
            }
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

        [Serialize(BotSpawnMode.Normal, true)]
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

        [Serialize(true, true)]
        public bool AllowRewiring
        {
            get;
            set;
        }

        [Serialize(true, true)]
        public bool AllowFriendlyFire
        {
            get;
            set;
        }

        private YesNoMaybe traitorsEnabled;
        [Serialize(YesNoMaybe.No, true)]
        public YesNoMaybe TraitorsEnabled
        {
            get { return traitorsEnabled; }
            set
            {
                if (traitorsEnabled == value) { return; }
                traitorsEnabled = value;
                ServerDetailsChanged = true;
            }
        }

        [Serialize(defaultValue: 1, isSaveable: true)]
        public int TraitorsMinPlayerCount
        {
            get;
            set;
        }

        [Serialize(defaultValue: 90.0f, isSaveable: true)]
        public float TraitorsMinStartDelay
        {
            get;
            set;
        }

        [Serialize(defaultValue: 180.0f, isSaveable: true)]
        public float TraitorsMaxStartDelay
        {
            get;
            set;
        }

        [Serialize(defaultValue: 30.0f, isSaveable: true)]
        public float TraitorsMinRestartDelay
        {
            get;
            set;
        }

        [Serialize(defaultValue: 90.0f, isSaveable: true)]
        public float TraitorsMaxRestartDelay
        {
            get;
            set;
        }

        private SelectionMode subSelectionMode;
        [Serialize(SelectionMode.Manual, true)]
        public SelectionMode SubSelectionMode
        {
            get { return subSelectionMode; }
            set
            {
                subSelectionMode = value;
                Voting.AllowSubVoting = subSelectionMode == SelectionMode.Vote;
                ServerDetailsChanged = true;
            }
        }

        private SelectionMode modeSelectionMode;
        [Serialize(SelectionMode.Manual, true)]
        public SelectionMode ModeSelectionMode
        {
            get { return modeSelectionMode; }
            set
            {
                modeSelectionMode = value;
                Voting.AllowModeVoting = modeSelectionMode == SelectionMode.Vote;
                ServerDetailsChanged = true;
            }
        }

        public BanList BanList { get; private set; }

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

        [Serialize(120.0f, true)]
        public float KickAFKTime
        {
            get;
            private set;
        }

        private bool karmaEnabled;
        [Serialize(false, true)]
        public bool KarmaEnabled
        {
            get { return karmaEnabled; }
            set
            {
                karmaEnabled = value;
#if CLIENT
                if (karmaSettingsBlocker != null) { karmaSettingsBlocker.Visible = !karmaEnabled || karmaPresetDD.SelectedData as string != "custom"; }
#endif
            }
        }

        [Serialize("default", true)]
        public string KarmaPreset
        {
            get;
            set;
        } = "default";

        [Serialize("sandbox", true)]
        public string GameModeIdentifier
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
            set { maxPlayers = MathHelper.Clamp(value, 1, NetConfig.MaxPlayers); }
        }

        public List<MissionType> AllowedRandomMissionTypes
        {
            get;
            set;
        }

        [Serialize(60f * 60.0f, true)]
        public float AutoBanTime
        {
            get;
            private set;
        }

        [Serialize(60.0f * 60.0f * 24.0f, true)]
        public float MaxAutoBanTime
        {
            get;
            private set;
        }

        public void SetPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                this.password = null;
            }
            else
            {
                this.password = Lidgren.Network.NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password));
            }
        }

        public static byte[] SaltPassword(byte[] password, int salt)
        {
            byte[] saltedPw = new byte[password.Length*2];
            for (int i = 0; i < password.Length; i++)
            {
                saltedPw[(i * 2)] = password[i];
                saltedPw[(i * 2) + 1] = (byte)((salt >> (8 * (i % 4))) & 0xff);
            }
            saltedPw = Lidgren.Network.NetUtility.ComputeSHAHash(saltedPw);
            return saltedPw;
        }

        public bool IsPasswordCorrect(byte[] input, int salt)
        {
            if (!HasPassword) return true;
            byte[] saltedPw = SaltPassword(password, salt);
            DebugConsole.NewMessage(ToolBox.ByteArrayToString(input)+" "+ToolBox.ByteArrayToString(saltedPw));
            if (input.Length != saltedPw.Length) return false;
            for (int i=0;i<input.Length;i++)
            {
                if (input[i] != saltedPw[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// A list of int pairs that represent the ranges of UTF-16 codes allowed in client names
        /// </summary>
        public List<Pair<int, int>> AllowedClientNameChars
        {
            get;
            private set;
        } = new List<Pair<int, int>>();

        private void InitMonstersEnabled()
        {
            //monster spawn settings
            if (MonsterEnabled == null)
            {
                List<string> monsterNames1 = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
                for (int i = 0; i < monsterNames1.Count; i++)
                {
                    monsterNames1[i] = Path.GetFileName(Path.GetDirectoryName(monsterNames1[i]));
                }

                MonsterEnabled = new Dictionary<string, bool>();
                foreach (string s in monsterNames1)
                {
                    if (!MonsterEnabled.ContainsKey(s)) MonsterEnabled.Add(s, true);
                }
            }
        }

        public void ReadMonsterEnabled(IReadMessage inc)
        {
            InitMonstersEnabled();
            List<string> monsterNames = MonsterEnabled.Keys.ToList();
            foreach (string s in monsterNames)
            {
                MonsterEnabled[s] = inc.ReadBoolean();
            }
            inc.ReadPadBits();
        }

        public void WriteMonsterEnabled(IWriteMessage msg, Dictionary<string, bool> monsterEnabled = null)
        {
            //monster spawn settings
            if (monsterEnabled == null) monsterEnabled = MonsterEnabled;

            List<string> monsterNames = monsterEnabled.Keys.ToList();
            foreach (string s in monsterNames)
            {
                msg.Write(monsterEnabled[s]);
            }
            msg.WritePadBits();
        }

        public bool ReadExtraCargo(IReadMessage msg)
        {
            bool changed = false;
            UInt32 count = msg.ReadUInt32();
            if (ExtraCargo == null || count != ExtraCargo.Count) changed = true;
            Dictionary<ItemPrefab, int> extraCargo = new Dictionary<ItemPrefab, int>();
            for (int i = 0; i < count; i++)
            {
                string prefabIdentifier = msg.ReadString();
                string prefabName = msg.ReadString();
                byte amount = msg.ReadByte();

                var itemPrefab = string.IsNullOrEmpty(prefabIdentifier) ?
                    MapEntityPrefab.Find(prefabName, null, showErrorMessages: false) as ItemPrefab :
                    MapEntityPrefab.Find(prefabName, prefabIdentifier, showErrorMessages: false) as ItemPrefab;
                if (itemPrefab != null && amount > 0)
                {
                    if (changed || !ExtraCargo.ContainsKey(itemPrefab) || ExtraCargo[itemPrefab] != amount) changed = true;
                    extraCargo.Add(itemPrefab, amount);
                }
            }
            if (changed) ExtraCargo = extraCargo;
            return changed;
        }

        public void WriteExtraCargo(IWriteMessage msg)
        {
            if (ExtraCargo == null)
            {
                msg.Write((UInt32)0);
                return;
            }

            msg.Write((UInt32)ExtraCargo.Count);
            foreach (KeyValuePair<ItemPrefab, int> kvp in ExtraCargo)
            {
                msg.Write(kvp.Key.Identifier ?? "");
                msg.Write(kvp.Key.OriginalName ?? "");
                msg.Write((byte)kvp.Value);
            }
        }
    }
}
