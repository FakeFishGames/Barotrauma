﻿using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Barotrauma.Networking
{
    public enum SelectionMode
    {
        Manual = 0, Random = 1, Vote = 2
    }

    public enum BotSpawnMode
    {
        Normal, Fill
    }

    public enum PlayStyle
    {
        Serious = 0,
        Casual = 1,
        Roleplay = 2,
        Rampage = 3,
        SomethingDifferent = 4
    }

    public enum RespawnMode
    {
        MidRound,
        BetweenRounds,
        Permadeath,
    }


    internal enum LootedMoneyDestination
    {
        Bank,
        Wallet
    }

    partial class ServerSettings : ISerializableEntity
    {
        public const int PacketLimitMin = 1200,
                         PacketLimitWarning = 3500,
                         PacketLimitDefault = 4000,
                         PacketLimitMax = 10000;

        public const string SettingsFile = "serversettings.xml";

        [Flags]
        public enum NetFlags : byte
        {
            None = 0x0,
            Properties = 0x4,
            Misc = 0x8,
            LevelSeed = 0x10,
            HiddenSubs = 0x20
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
            public readonly Either<Address, AccountId> AddressOrAccountId;
            public readonly string Name;
            public readonly ImmutableHashSet<DebugConsole.Command> PermittedCommands;

            public readonly ClientPermissions Permissions;

            public SavedClientPermission(string name, Either<Address, AccountId> addressOrAccountId, ClientPermissions permissions, IEnumerable<DebugConsole.Command> permittedCommands)
            {
                this.Name = name;
                this.AddressOrAccountId = addressOrAccountId;
                this.Permissions = permissions;
                this.PermittedCommands = permittedCommands.ToImmutableHashSet();
            }
        }

        partial class NetPropertyData
        {
            private readonly SerializableProperty property;
            private readonly string typeString;
            private readonly object parentObject;

            public Identifier Name => property.Name.ToIdentifier();

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
                        if (a is not float fa) { return false; }
                        if (b is not float fb) { return false; }
                        return MathUtils.NearlyEqual(fa, fb);
                    case "int":
                        if (a is not int ia) { return false; }
                        if (b is not int ib) { return false; }
                        return ia == ib;
                    case "bool":
                        if (a is not bool ba) { return false; }
                        if (b is not bool bb) { return false; }
                        return ba == bb;
                    case "Enum":
                        if (a is not Enum ea) { return false; }
                        if (b is not Enum eb) { return false; }
                        return ea.Equals(eb);
                    default:
                        return ReferenceEquals(a,b)
                            || string.Equals(a?.ToString(), b?.ToString(), StringComparison.OrdinalIgnoreCase);
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
                overrideValue ??= Value;
                switch (typeString)
                {
                    case "float":
                        msg.WriteVariableUInt32(4);
                        msg.WriteSingle((float)overrideValue);
                        break;
                    case "int":
                        msg.WriteVariableUInt32(4);
                        msg.WriteInt32((int)overrideValue);
                        break;
                    case "vector2":
                        msg.WriteVariableUInt32(8);
                        msg.WriteSingle(((Vector2)overrideValue).X);
                        msg.WriteSingle(((Vector2)overrideValue).Y);
                        break;
                    case "vector3":
                        msg.WriteVariableUInt32(12);
                        msg.WriteSingle(((Vector3)overrideValue).X);
                        msg.WriteSingle(((Vector3)overrideValue).Y);
                        msg.WriteSingle(((Vector3)overrideValue).Z);
                        break;
                    case "vector4":
                        msg.WriteVariableUInt32(16);
                        msg.WriteSingle(((Vector4)overrideValue).X);
                        msg.WriteSingle(((Vector4)overrideValue).Y);
                        msg.WriteSingle(((Vector4)overrideValue).Z);
                        msg.WriteSingle(((Vector4)overrideValue).W);
                        break;
                    case "color":
                        msg.WriteVariableUInt32(4);
                        msg.WriteByte(((Color)overrideValue).R);
                        msg.WriteByte(((Color)overrideValue).G);
                        msg.WriteByte(((Color)overrideValue).B);
                        msg.WriteByte(((Color)overrideValue).A);
                        break;
                    case "rectangle":
                        msg.WriteVariableUInt32(16);
                        msg.WriteInt32(((Rectangle)overrideValue).X);
                        msg.WriteInt32(((Rectangle)overrideValue).Y);
                        msg.WriteInt32(((Rectangle)overrideValue).Width);
                        msg.WriteInt32(((Rectangle)overrideValue).Height);
                        break;
                    default:
                        string strVal = overrideValue.ToString();

                        msg.WriteString(strVal);
                        break;
                }
            }
        };

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        private readonly Dictionary<UInt32, NetPropertyData> netProperties;

        partial void InitProjSpecific();

        public ServerSettings(NetworkMember networkMember, string serverName, int port, int queryPort, int maxPlayers, bool isPublic, bool enableUPnP)
        {
            ServerLog = new ServerLog(serverName);

            BanList = new BanList();

            ExtraCargo = new Dictionary<ItemPrefab, int>();

            HiddenSubs = new HashSet<string>();

            PermissionPreset.LoadAll(PermissionPresetFile);
            InitProjSpecific();

            ServerName = serverName;
            Port = port;
            QueryPort = queryPort;
            EnableUPnP = enableUPnP;
            MaxPlayers = maxPlayers;
            IsPublic = isPublic;

            netProperties = new Dictionary<UInt32, NetPropertyData>();

            using (MD5 md5 = MD5.Create())
            {
                var saveProperties = SerializableProperty.GetProperties<Serialize>(this);
                foreach (var property in saveProperties)
                {
                    string typeName = SerializableProperty.GetSupportedTypeName(property.PropertyType);
                    if (typeName != null || property.PropertyType.IsEnum)
                    {
                        NetPropertyData netPropertyData = new NetPropertyData(this, property, typeName);
                        UInt32 key = ToolBoxCore.IdentifierToUint32Hash(netPropertyData.Name, md5);
                        if (key == 0) { key++; } //0 is reserved to indicate the end of the netproperties section of a message
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
                        UInt32 key = ToolBoxCore.IdentifierToUint32Hash(netPropertyData.Name, md5);
                        if (netProperties.ContainsKey(key)) { throw new Exception("Hashing collision in ServerSettings.netProperties: " + netProperties[key] + " has same key as " + property.Name + " (" + key.ToString() + ")"); }
                        netProperties.Add(key, netPropertyData);
                    }
                }
            }
        }

        private string serverName = string.Empty;

        [Serialize("", IsPropertySaveable.Yes)]
        public string ServerName
        {
            get { return serverName; }
            set
            {
                string newName = value;
                if (newName.Length > NetConfig.ServerNameMaxLength) { newName = newName.Substring(0, NetConfig.ServerNameMaxLength); }
                if (serverName == newName) { return; }
                if (newName.IsNullOrWhiteSpace()) { return; }
                serverName = newName;
                ServerDetailsChanged = true;
            }
        }

        private string serverMessageText;

        [Serialize("", IsPropertySaveable.Yes)]
        public string ServerMessageText
        {
            get { return serverMessageText; }
            set
            {
                string val = value;
                if (val.Length > NetConfig.ServerMessageMaxLength) { val = val.Substring(0, NetConfig.ServerMessageMaxLength); }
                if (serverMessageText == val) { return; }
#if SERVER
                GameMain.Server?.SendChatMessage(TextManager.AddPunctuation(':', TextManager.Get("servermotd"), val).Value, ChatMessageType.Server);
#elif CLIENT
                if (GameMain.NetLobbyScreen.ServerMessageButton is { } serverMessageButton)
                {
                    serverMessageButton.Flash(GUIStyle.Green);
                    serverMessageButton.Pulsate(Vector2.One, Vector2.One * 1.2f, 1.0f);
                }
#endif
                serverMessageText = val;
                ServerDetailsChanged = true;
            }
        }

        public int Port;

        public int QueryPort;

        public bool EnableUPnP;

        public ServerLog ServerLog;

        public Dictionary<Identifier, bool> MonsterEnabled { get; private set; }

        public const int MaxExtraCargoItemsOfType = 10;
        public const int MaxExtraCargoItemTypes = 20;
        public Dictionary<ItemPrefab, int> ExtraCargo { get; private set; }

        public HashSet<string> HiddenSubs { get; private set; }

        private float selectedLevelDifficulty;
        private string password;

        public float AutoRestartTimer;

        private bool autoRestart;

        private int maxPlayers;

        public List<SavedClientPermission> ClientPermissions { get; private set; } = new List<SavedClientPermission>();

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool IsPublic
        {
            get;
            set;
        }

        public const int DefaultTickRate = 20;

        private int tickRate = DefaultTickRate;
        [Serialize(DefaultTickRate, IsPropertySaveable.Yes)]
        public int TickRate
        {
            get { return tickRate; }
            set { tickRate = MathHelper.Clamp(value, 1, 60); }
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Do clients need to be authenticated (e.g. based on Steam ID or an EGS ownership token). Can be disabled if you for example want to play the game in a local network without a connection to external services.")]
        public bool RequireAuthentication
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool RandomizeSeed
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool UseRespawnShuttle
        {
            get;
            private set;
        }

        [Serialize(300.0f, IsPropertySaveable.Yes)]
        public float RespawnInterval
        {
            get;
            private set;
        }

        [Serialize(180.0f, IsPropertySaveable.Yes)]
        public float MaxTransportTime
        {
            get;
            private set;
        }

        [Serialize(0.2f, IsPropertySaveable.Yes)]
        public float MinRespawnRatio
        {
            get;
            private set;
        }

        [Serialize(20f, IsPropertySaveable.Yes)]
        /// <summary>
        /// How much skills drop towards the job's default skill levels when dying
        /// </summary>
        public float SkillLossPercentageOnDeath
        {
            get;
            private set;
        }

        [Serialize(10f, IsPropertySaveable.Yes)]
        /// <summary>
        /// How much more the skills drop towards the job's default skill levels
        /// when dying, in addition to SkillLossPercentageOnDeath, if the player
        /// chooses to respawn in the middle of the round
        /// </summary>
        public float SkillLossPercentageOnImmediateRespawn
        {
            get;
            private set;
        }
        
        [Serialize(100f, IsPropertySaveable.Yes)]
        /// <summary>
        /// Percentage modifier for the cost of hiring a new character to replace a permanently killed one.
        /// </summary>
        public float ReplaceCostPercentage
        {
            get;
            private set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        /// <summary>
        /// Are players allowed to take over bots when permadeath is enabled?
        /// </summary>
        public bool AllowBotTakeoverOnPermadeath
        {
            get;
            private set;
        }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        /// <summary>
        /// This is an optional setting for permadeath mode. When it's enabled, a player client whose character dies cannot
        /// respawn or get a new character in any way in that game (unlike in normal permadeath mode), and can only spectate.
        /// </summary>
        public bool IronmanMode
        {
            get;
            private set;
        }

        [Serialize(60.0f, IsPropertySaveable.Yes)]
        public float AutoRestartInterval
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool StartWhenClientsReady
        {
            get;
            set;
        }

        [Serialize(0.8f, IsPropertySaveable.Yes)]
        public float StartWhenClientsReadyRatio
        {
            get;
            private set;
        }

        private bool allowSpectating;
        [Serialize(true, IsPropertySaveable.Yes)]
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

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool SaveServerLogs
        {
            get;
            private set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowModDownloads
        {
            get;
            private set;
        } = true;

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowFileTransfers
        {
            get;
            private set;
        }

        private bool voiceChatEnabled;
        [Serialize(true, IsPropertySaveable.Yes)]
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

        private PlayStyle playstyleSelection;
        [Serialize(PlayStyle.Casual, IsPropertySaveable.Yes)]
        public PlayStyle PlayStyle
        {
            get { return playstyleSelection; }
            set 
            {
                playstyleSelection = value;
                ServerDetailsChanged = true;
            }
        }

        [Serialize(LosMode.Transparent, IsPropertySaveable.Yes)]
        public LosMode LosMode
        {
            get;
            set;
        }

        [Serialize(EnemyHealthBarMode.ShowAll, IsPropertySaveable.Yes)]
        public EnemyHealthBarMode ShowEnemyHealthBars
        {
            get;
            set;
        }

        [Serialize(800, IsPropertySaveable.Yes)]
        public int LinesPerLogFile
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

        [Serialize(false, IsPropertySaveable.Yes)]
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
#if CLIENT
            set
            {
                password = value ? (password ?? "_") : null;
            }
#endif
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowVoteKick
        {
            get; set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowEndVoting
        {
            get; set;
        }

        private RespawnMode respawnMode;
        [Serialize(RespawnMode.MidRound, IsPropertySaveable.Yes)]
        public RespawnMode RespawnMode
        {
            get { return respawnMode; }
            set
            {
                if (respawnMode == value) { return; }
                //can't change this when a round is running (but clients can, if the server says so, e.g. when a client joins and needs to know what it's set to despite a round being running)
                if (GameMain.NetworkMember is { GameStarted: true, IsServer: true }) { return; }
                respawnMode = value;
                ServerDetailsChanged = true;
            }
        }

        [Serialize(0, IsPropertySaveable.Yes)]
        public int BotCount
        {
            get;
            set;
        }

        [Serialize(16, IsPropertySaveable.Yes)]
        public int MaxBotCount
        {
            get;
            set;
        }

        [Serialize(BotSpawnMode.Normal, IsPropertySaveable.Yes)]
        public BotSpawnMode BotSpawnMode
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool DisableBotConversations
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float SelectedLevelDifficulty
        {
            get { return selectedLevelDifficulty; }
            set { selectedLevelDifficulty = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowDisguises
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowRewiring
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowImmediateItemDelivery
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool LockAllDefaultWires
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AllowLinkingWifiToChat
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowFriendlyFire
        {
            get;
            set;
        }
        
        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowDragAndDropGive
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool DestructibleOutposts
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool KillableNPCs
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool BanAfterWrongPassword
        {
            get;
            set;
        }

        [Serialize(3, IsPropertySaveable.Yes)]
        public int MaxPasswordRetriesBeforeBan
        {
            get;
            private set;
        }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool EnableDoSProtection
        {
            get;
            private set;
        }

        [Serialize(PacketLimitDefault, IsPropertySaveable.Yes)]
        public int MaxPacketAmount
        {
            get;
            private set;
        }

        [Serialize("", IsPropertySaveable.Yes)]
        public string SelectedSubmarine
        {
            get;
            set;
        }
        [Serialize("", IsPropertySaveable.Yes)]
        public string SelectedShuttle
        {
            get;
            set;
        }

        private float traitorProbability;
        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float TraitorProbability
        {
            get { return traitorProbability; }
            set
            {
                if (MathUtils.NearlyEqual(traitorProbability, value)) { return; }
                traitorProbability = MathHelper.Clamp(value, 0.0f, 1.0f);
                ServerDetailsChanged = true;
            }
        }


        private int traitorDangerLevel;
        [Serialize(TraitorEventPrefab.MinDangerLevel, IsPropertySaveable.Yes)]
        public int TraitorDangerLevel
        {
            get { return traitorDangerLevel; }
            set
            {
                int clampedValue = MathHelper.Clamp(value, TraitorEventPrefab.MinDangerLevel, TraitorEventPrefab.MaxDangerLevel);
                if (traitorDangerLevel == clampedValue) { return; }
                traitorDangerLevel = clampedValue;
                ServerDetailsChanged = true;
#if CLIENT
                GameMain.NetLobbyScreen?.SetTraitorDangerLevel(traitorDangerLevel);
#endif
            }
        }

        private int traitorsMinPlayerCount;
        [Serialize(defaultValue: 1, isSaveable: IsPropertySaveable.Yes)]
        public int TraitorsMinPlayerCount
        {
            get { return traitorsMinPlayerCount; }
            set { traitorsMinPlayerCount = MathHelper.Clamp(value, 1, NetConfig.MaxPlayers); }
        }

        [Serialize(defaultValue: 50.0f, isSaveable: IsPropertySaveable.Yes)]
        public float MinPercentageOfPlayersForTraitorAccusation
        {
            get;
            set;
        }

        [Serialize(defaultValue: "", IsPropertySaveable.Yes)]
        public LanguageIdentifier Language { get; set; }

        private SelectionMode subSelectionMode;
        [Serialize(SelectionMode.Manual, IsPropertySaveable.Yes)]
        public SelectionMode SubSelectionMode
        {
            get { return subSelectionMode; }
            set
            {
                subSelectionMode = value;
                AllowSubVoting = subSelectionMode == SelectionMode.Vote;
                ServerDetailsChanged = true;
            }
        }

        private SelectionMode modeSelectionMode;
        [Serialize(SelectionMode.Manual, IsPropertySaveable.Yes)]
        public SelectionMode ModeSelectionMode
        {
            get { return modeSelectionMode; }
            set
            {
                modeSelectionMode = value;
                AllowModeVoting = modeSelectionMode == SelectionMode.Vote;
                ServerDetailsChanged = true;
            }
        }

        public BanList BanList { get; private set; }

        [Serialize(0.6f, IsPropertySaveable.Yes)]
        public float EndVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(0.6f, IsPropertySaveable.Yes)]
        public float VoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(30f, IsPropertySaveable.Yes)]
        public float VoteTimeout
        {
            get;
            private set;
        }

        [Serialize(0.6f, IsPropertySaveable.Yes)]
        public float KickVoteRequiredRatio
        {
            get;
            private set;
        }

        [Serialize(120.0f, IsPropertySaveable.Yes)]
        public float DisallowKickVoteTime
        {
            get;
            private set;
        }
        
        /// <summary>
        /// The number of seconds a disconnected player's Character remains in the world until despawned (via "braindeath").
        /// </summary>
        [Serialize(300.0f, IsPropertySaveable.Yes)]
        public float KillDisconnectedTime
        {
            get;
            private set;
        }
        
        /// <summary>
        /// The number of seconds a disconnected player's Character remains in the world until despawned, in permadeath mode.
        /// The Character is helpless and vulnerable, this should be short enough to avoid unintended permadeath, but
        /// also long enough to discourage disconnecting just to avoid a potential incoming permadeath.
        /// </summary>
        [Serialize(10.0f, IsPropertySaveable.Yes)]
        public float DespawnDisconnectedPermadeathTime
        {
            get;
            private set;
        }

        [Serialize(600.0f, IsPropertySaveable.Yes)]
        public float KickAFKTime
        {
            get;
            private set;
        }

        [Serialize(10.0f, IsPropertySaveable.Yes)]
        public float MinimumMidRoundSyncTimeout
        {
            get;
            private set;
        }

        private bool karmaEnabled;
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool KarmaEnabled
        {
            get { return karmaEnabled; }
            set
            {
                karmaEnabled = value;
#if CLIENT
                if (karmaSettingsList != null)
                {
                    SetElementInteractability(karmaSettingsList.Content, !karmaEnabled || KarmaPreset != "custom");
                }
                karmaElements.ForEach(e => e.Visible = karmaEnabled);
#endif
            }
        }

        private string karmaPreset = "default";
        [Serialize("default", IsPropertySaveable.Yes)]
        public string KarmaPreset
        {
            get { return karmaPreset; }
            set
            {
                if (karmaPreset == value) { return; }
                if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    GameMain.NetworkMember?.KarmaManager?.SelectPreset(value);
                }
                karmaPreset = value;
            }
        }

        [Serialize("sandbox", IsPropertySaveable.Yes)]
        public Identifier GameModeIdentifier
        {
            get;
            set;
        }

        [Serialize("All", IsPropertySaveable.Yes)]
        public string MissionType
        {
            get;
            set;
        }

        [Serialize(8, IsPropertySaveable.Yes)]
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

        [Serialize(60f * 60.0f, IsPropertySaveable.Yes)]
        public float AutoBanTime
        {
            get;
            private set;
        }

        [Serialize(60.0f * 60.0f * 24.0f, IsPropertySaveable.Yes)]
        public float MaxAutoBanTime
        {
            get;
            private set;
        }

        [Serialize(LootedMoneyDestination.Bank, IsPropertySaveable.Yes)]
        public LootedMoneyDestination LootedMoneyDestination { get; set; }

        [Serialize(999999, IsPropertySaveable.Yes)]
        public int MaximumMoneyTransferRequest { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float NewCampaignDefaultSalary { get; set; }

        public CampaignSettings CampaignSettings { get; set; } = CampaignSettings.Empty;

        private bool allowSubVoting;
        //Don't serialize: the value is set based on SubSelectionMode
        public bool AllowSubVoting
        {
            get { return allowSubVoting; }
            set
            {
                if (value == allowSubVoting) { return; }
                allowSubVoting = value;
#if CLIENT
                GameMain.NetLobbyScreen.SubList.Enabled = value ||
                    (GameMain.Client != null && GameMain.Client.HasPermission(Networking.ClientPermissions.SelectSub));
                var subVotesLabel = GameMain.NetLobbyScreen.Frame.FindChild("subvotes", true) as GUITextBlock;
                subVotesLabel.Visible = value;
                var subVisButton = GameMain.NetLobbyScreen.SubVisibilityButton;
                subVisButton.RectTransform.AbsoluteOffset
                    = new Point(value ? (int)(subVotesLabel.TextSize.X + subVisButton.Rect.Width) : 0, 0);

                GameMain.Client?.Voting.UpdateVoteTexts(null, VoteType.Sub);
                GameMain.NetLobbyScreen.SubList.Deselect();
#endif
            }
        }

        private bool allowModeVoting;
        //Don't serialize: the value is set based on ModeSelectionMode
        public bool AllowModeVoting
        {
            get { return allowModeVoting; }
            set
            {
                if (value == allowModeVoting) { return; }
                allowModeVoting = value;
#if CLIENT
                GameMain.NetLobbyScreen.ModeList.Enabled =
                    value ||
                    (GameMain.Client != null && GameMain.Client.HasPermission(Networking.ClientPermissions.SelectMode));
                GameMain.NetLobbyScreen.Frame.FindChild("modevotes", true).Visible = value;
                // Disable modes that cannot be voted on
                foreach (var guiComponent in GameMain.NetLobbyScreen.ModeList.Content.Children)
                {
                    if (guiComponent is GUIFrame frame)
                    {
                        frame.CanBeFocused = !allowModeVoting || ((GameModePreset)frame.UserData).Votable;
                    }
                }
                GameMain.Client?.Voting.UpdateVoteTexts(null, VoteType.Mode);
                GameMain.NetLobbyScreen.ModeList.Deselect();
#endif
            }
        }


        public void SetPassword(string password)
        {
            this.password = string.IsNullOrEmpty(password) ? null : password;
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
            if (!HasPassword) { return true; }
            byte[] saltedPw = SaltPassword(Encoding.UTF8.GetBytes(password), salt);
            return saltedPw.SequenceEqual(input);
        }

        /// <summary>
        /// A list of int pairs that represent the ranges of UTF-16 codes allowed in client names
        /// </summary>
        public List<Range<int>> AllowedClientNameChars
        {
            get;
            private set;
        } = new List<Range<int>>();

        private void InitMonstersEnabled()
        {
            //monster spawn settings
            if (MonsterEnabled is null || MonsterEnabled.Count != CharacterPrefab.Prefabs.Count())
            {
                MonsterEnabled = CharacterPrefab.Prefabs.Select(p => (p.Identifier, true)).ToDictionary();
            }
        }

        private static IReadOnlyList<Identifier> ExtractAndSortKeys(IReadOnlyDictionary<Identifier, bool> monsterEnabled)
            => monsterEnabled.Keys
                .OrderBy(k => CharacterPrefab.Prefabs[k].UintIdentifier)
                .ToImmutableArray();
        
        public bool ReadMonsterEnabled(IReadMessage inc)
        {
            bool changed = false;
            InitMonstersEnabled();
            var monsterNames = ExtractAndSortKeys(MonsterEnabled);
            uint receivedMonsterCount = inc.ReadVariableUInt32();
            if (monsterNames.Count != receivedMonsterCount)
            {
                inc.BitPosition += (int)receivedMonsterCount;
                DebugConsole.AddWarning($"Expected monster count {monsterNames.Count}, got {receivedMonsterCount}");
            }
            else
            {
                foreach (Identifier s in monsterNames)
                {
                    MonsterEnabled.TryGetValue(s, out bool prevEnabled);
                    MonsterEnabled[s] = inc.ReadBoolean();
                    changed |= prevEnabled != MonsterEnabled[s];
                }
            }
            inc.ReadPadBits();
            return changed;
        }

        public void WriteMonsterEnabled(IWriteMessage msg, Dictionary<Identifier, bool> monsterEnabled = null)
        {
            //monster spawn settings
            InitMonstersEnabled();
            monsterEnabled ??= MonsterEnabled;
            var monsterNames = ExtractAndSortKeys(monsterEnabled);
            msg.WriteVariableUInt32((uint)monsterNames.Count);
            foreach (Identifier s in monsterNames)
            {
                msg.WriteBoolean(monsterEnabled[s]);
            }
            msg.WritePadBits();
        }

        public bool ReadExtraCargo(IReadMessage msg)
        {
            bool changed = false;
            UInt32 count = msg.ReadUInt32();
            if (ExtraCargo == null || count != ExtraCargo.Count) { changed = true; }
            Dictionary<ItemPrefab, int> extraCargo = new Dictionary<ItemPrefab, int>();
            for (int i = 0; i < count; i++)
            {
                Identifier prefabIdentifier = msg.ReadIdentifier();
                byte amount = msg.ReadByte();

                if (MapEntityPrefab.Find(null, prefabIdentifier, showErrorMessages: false) is ItemPrefab itemPrefab && amount > 0)
                {
                    if (ExtraCargo.Keys.Count() >= MaxExtraCargoItemTypes) { continue; }
                    if (ExtraCargo.ContainsKey(itemPrefab) && ExtraCargo[itemPrefab] >= MaxExtraCargoItemsOfType) { continue; }
                    if (changed || !ExtraCargo.ContainsKey(itemPrefab) || ExtraCargo[itemPrefab] != amount) { changed = true; }
                    extraCargo.Add(itemPrefab, amount);
                }
            }
            if (changed) { ExtraCargo = extraCargo; }
            return changed;
        }

        public void WriteExtraCargo(IWriteMessage msg)
        {
            if (ExtraCargo == null)
            {
                msg.WriteUInt32((UInt32)0);
                return;
            }

            msg.WriteUInt32((UInt32)ExtraCargo.Count);
            foreach (KeyValuePair<ItemPrefab, int> kvp in ExtraCargo)
            {
                msg.WriteIdentifier(kvp.Key.Identifier);
                msg.WriteByte((byte)kvp.Value);
            }
        }

        public void ReadHiddenSubs(IReadMessage msg)
        {
            var subList = GameMain.NetLobbyScreen.GetSubList();

            HiddenSubs.Clear();
            uint count = msg.ReadVariableUInt32();
            for (int i = 0; i < count; i++)
            {
                int index = msg.ReadUInt16();
                if (index >= subList.Count) { continue; }
                string submarineName = subList[index].Name;
                HiddenSubs.Add(submarineName);
            }

#if SERVER
            SelectNonHiddenSubmarine();
#endif
        }

        public void WriteHiddenSubs(IWriteMessage msg)
        {
            var subList = GameMain.NetLobbyScreen.GetSubList();

            msg.WriteVariableUInt32((uint)HiddenSubs.Count);
            foreach (string submarineName in HiddenSubs)
            {
                msg.WriteUInt16((UInt16)subList.FindIndex(s => s.Name.Equals(submarineName, StringComparison.OrdinalIgnoreCase)));
            }
        }
        
        public void UpdateServerListInfo(Action<Identifier, object> setter)
        {
            void set(string key, object obj) => setter(key.ToIdentifier(), obj);

            set("ServerName", ServerName);
            set("MaxPlayers", MaxPlayers);
            set("HasPassword", HasPassword);
            set("message", ServerMessageText);
            set("version", GameMain.Version);
            set("playercount", GameMain.NetworkMember.ConnectedClients.Count);
            set("contentpackages", ContentPackageManager.EnabledPackages.All.Where(p => p.HasMultiplayerSyncedContent));
            set("modeselectionmode", ModeSelectionMode);
            set("subselectionmode", SubSelectionMode);
            set("voicechatenabled", VoiceChatEnabled);
            set("allowspectating", AllowSpectating);
            set("allowrespawn", RespawnMode is RespawnMode.MidRound or RespawnMode.BetweenRounds);
            set("traitors", TraitorProbability.ToString(CultureInfo.InvariantCulture));
            set("friendlyfireenabled", AllowFriendlyFire);
            set("karmaenabled", KarmaEnabled);
            set("gamestarted", GameMain.NetworkMember.GameStarted);
            set("gamemode", GameModeIdentifier);
            set("playstyle", PlayStyle);
            set("language", Language.ToString());
#if SERVER
            set("eoscrossplay", EosInterface.Core.IsInitialized);
#else
            set("eoscrossplay", EosInterface.IdQueries.IsLoggedIntoEosConnect || Eos.EosSessionManager.CurrentOwnedSession.IsSome());
#endif
            if (GameMain.NetLobbyScreen?.SelectedSub != null)
            {
                set("submarine", GameMain.NetLobbyScreen.SelectedSub.Name);
            }
            if (Steamworks.SteamClient.IsLoggedOn)
            {
                string pingLocation = Steamworks.SteamNetworkingUtils.LocalPingLocation?.ToString();
                if (!pingLocation.IsNullOrEmpty())
                {
                    set("steampinglocation", pingLocation);
                }
            }
        }
    }
}
