using Barotrauma.Extensions;
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;

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

            public RegularPackage RegularPackage
            {
                get
                {
                    return ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Hash.Equals(Hash));
                }
            }

            public CorePackage CorePackage
            {
                get
                {
                    return ContentPackageManager.CorePackages.FirstOrDefault(p => p.Hash.Equals(Hash));
                }
            }

            public ContentPackage ContentPackage
                => (ContentPackage)RegularPackage ?? CorePackage;
            
            
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
        
        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public DisconnectMessageCallback OnDisconnectMessageReceived;
        public PasswordCallback OnRequestPassword;
        public InitializationCompleteCallback OnInitializationComplete;

        public string Name;

        public string Version { get; protected set; }

        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint, int ownerKey);
        public abstract void Close(string msg = null, bool disableReconnect = false);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void SendPassword(string password);

        protected abstract void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg);

        protected ConnectionInitialization initializationStep;
        protected bool contentPackageOrderReceived;
        protected int ownerKey = 0;
        protected int passwordSalt;
        protected Steamworks.AuthTicket steamAuthTicket;
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
                    outMsg.Write(Name);
                    outMsg.Write(ownerKey);
                    outMsg.Write(SteamManager.GetSteamID());
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

                    string serverName = inc.ReadString();

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

                    if (!contentPackageOrderReceived)
                    {
                        ServerContentPackages = serverPackages.ToImmutableArray();
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
                    OnRequestPassword?.Invoke(passwordSalt, retries);
                    break;
            }
        }

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}
