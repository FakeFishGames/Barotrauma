#nullable enable
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public class ServerContentPackage
        {
            public readonly string Name;
            public readonly Md5Hash Hash;
            public readonly UInt64 WorkshopId;
            public readonly DateTime InstallTime;

            public RegularPackage? RegularPackage
            {
                get
                {
                    return ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Hash.Equals(Hash));
                }
            }

            public CorePackage? CorePackage
            {
                get
                {
                    return ContentPackageManager.CorePackages.FirstOrDefault(p => p.Hash.Equals(Hash));
                }
            }

            public ContentPackage? ContentPackage
                => (ContentPackage?)RegularPackage ?? CorePackage;
            
            
            public string GetPackageStr()
                => $"\"{Name}\" (hash {Hash.ShortRepresentation})";

            public ServerContentPackage(string name, Md5Hash hash, UInt64 workshopId, DateTime installTime)
            {
                Name = name;
                Hash = hash;
                WorkshopId = workshopId;
                InstallTime = installTime;
            }
        }

        public ImmutableArray<ServerContentPackage> ServerContentPackages { get; set; } =
            ImmutableArray<ServerContentPackage>.Empty;

        public delegate void MessageCallback(IReadMessage message);
        public delegate void DisconnectCallback(bool disableReconnect);
        public delegate void DisconnectMessageCallback(string message);
        public delegate void PasswordCallback(int salt, int retries);
        public delegate void InitializationCompleteCallback();

        [Obsolete("TODO: delete in nr3-layer-1-2-cleanup")]
        public readonly struct Callbacks
        {
            public readonly MessageCallback OnMessageReceived;
            public readonly DisconnectCallback OnDisconnect;
            public readonly DisconnectMessageCallback OnDisconnectMessageReceived;
            public readonly PasswordCallback OnRequestPassword;
            public readonly InitializationCompleteCallback OnInitializationComplete;
            
            public Callbacks(MessageCallback onMessageReceived, DisconnectCallback onDisconnect, DisconnectMessageCallback onDisconnectMessageReceived, PasswordCallback onRequestPassword, InitializationCompleteCallback onInitializationComplete)
            {
                OnMessageReceived = onMessageReceived;
                OnDisconnect = onDisconnect;
                OnDisconnectMessageReceived = onDisconnectMessageReceived;
                OnRequestPassword = onRequestPassword;
                OnInitializationComplete = onInitializationComplete;
            }
        }

        protected readonly Callbacks callbacks;

        public readonly Endpoint ServerEndpoint;
        public NetworkConnection? ServerConnection { get; protected set; }

        protected readonly bool isOwner;
        protected readonly Option<int> ownerKey;

        public ClientPeer(Endpoint serverEndpoint, Callbacks callbacks, Option<int> ownerKey)
        {
            ServerEndpoint = serverEndpoint;
            this.callbacks = callbacks;
            this.ownerKey = ownerKey;
            isOwner = ownerKey.IsSome();
        }
        
        public abstract void Start();
        public abstract void Close(string? msg = null, bool disableReconnect = false);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void SendPassword(string password);

        protected abstract void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg);

        protected ConnectionInitialization initializationStep;
        public bool ContentPackageOrderReceived { get; protected set; }
        protected int passwordSalt;
        protected Steamworks.AuthTicket? steamAuthTicket;
        protected void ReadConnectionInitializationStep(IReadMessage inc)
        {
            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();

            IWriteMessage outMsg;

            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(GameMain.Client.Name);
                    outMsg.Write(ownerKey.Fallback(0));
                    outMsg.Write(SteamManager.GetSteamId().Select(steamId => steamId.Value).Fallback(0));
                    if (steamAuthTicket == null)
                    {
                        outMsg.Write((UInt16)0);
                    }
                    else
                    {
                        outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                        outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);
                    }
                    outMsg.Write(GameMain.Version.ToString());
                    outMsg.Write(GameSettings.CurrentConfig.Language.Value);

                    SendMsgInternal(DeliveryMethod.Reliable, outMsg);
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion ||
                        initializationStep == ConnectionInitialization.Password) { initializationStep = ConnectionInitialization.ContentPackageOrder; }
                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.ContentPackageOrder);

                    UInt32 packageCount = inc.ReadVariableUInt32();
                    List<ServerContentPackage> serverPackages = new List<ServerContentPackage>();
                    for (int i = 0; i < packageCount; i++)
                    {
                        string name = inc.ReadString();
                        UInt32 hashByteCount = inc.ReadVariableUInt32();
                        byte[] hashBytes = inc.ReadBytes((int)hashByteCount);
                        UInt64 workshopId = inc.ReadUInt64();
                        UInt32 installTimeDiffSeconds = inc.ReadUInt32();
                        DateTime installTime = DateTime.UtcNow + TimeSpan.FromSeconds(installTimeDiffSeconds);

                        var pkg = new ServerContentPackage(name, Md5Hash.BytesAsHash(hashBytes), workshopId, installTime);
                        serverPackages.Add(pkg);
                    }

                    if (!ContentPackageOrderReceived)
                    {
                        ServerContentPackages = serverPackages.ToImmutableArray();
                        if (serverPackages.Count == 0)
                        {
                            string errorMsg = "Error in ContentPackageOrder message: list of content packages enabled on the server was empty.";
                            GameAnalyticsManager.AddErrorEventOnce("ClientPeer.ReadConnectionInitializationStep:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            DebugConsole.ThrowError(errorMsg);
                        }
                        ContentPackageOrderReceived = true;
                        SendMsgInternal(DeliveryMethod.Reliable, outMsg);
                    }
                    break;
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion) { initializationStep = ConnectionInitialization.Password; }
                    if (initializationStep != ConnectionInitialization.Password) { return; }
                    bool incomingSalt = inc.ReadBoolean(); inc.ReadPadBits();
                    int retries = 0;
                    if (incomingSalt)
                    {
                        passwordSalt = inc.ReadInt32();
                    }
                    else
                    {
                        retries = inc.ReadInt32();
                    }
                    callbacks.OnRequestPassword.Invoke(passwordSalt, retries);
                    break;
            }
        }

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}
