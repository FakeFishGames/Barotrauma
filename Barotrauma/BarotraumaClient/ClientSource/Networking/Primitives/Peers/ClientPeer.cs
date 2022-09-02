#nullable enable
using Barotrauma.Steam;
using System;
using System.Collections.Immutable;

namespace Barotrauma.Networking
{
    internal abstract class ClientPeer
    {
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

            public Callbacks(MessageCallback onMessageReceived,
                             DisconnectCallback onDisconnect,
                             DisconnectMessageCallback onDisconnectMessageReceived,
                             PasswordCallback onRequestPassword,
                             InitializationCompleteCallback onInitializationComplete)
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

        protected abstract void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body);

        protected ConnectionInitialization initializationStep;
        public bool ContentPackageOrderReceived { get; protected set; }
        protected int passwordSalt;
        protected Steamworks.AuthTicket? steamAuthTicket;

        public struct IncomingInitializationMessage
        {
            public ConnectionInitialization InitializationStep;
            public IReadMessage Message;
        }

        protected void ReadConnectionInitializationStep(IncomingInitializationMessage inc)
        {
            switch (inc.InitializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                {
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }

                    PeerPacketHeaders headers = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsConnectionInitializationStep,
                        Initialization = ConnectionInitialization.SteamTicketAndVersion
                    };

                    ClientSteamTicketAndVersionPacket body = new ClientSteamTicketAndVersionPacket
                    {
                        Name = GameMain.Client.Name,
                        OwnerKey = ownerKey,
                        SteamId = SteamManager.GetSteamId().Select(id => (AccountId)id),
                        SteamAuthTicket = steamAuthTicket switch
                        {
                            null => Option<byte[]>.None(),
                            var ticket => Option<byte[]>.Some(ticket.Data)
                        },
                        GameVersion = GameMain.Version.ToString(),
                        Language = GameSettings.CurrentConfig.Language.Value
                    };

                    SendMsgInternal(headers, body);
                    break;
                }
                case ConnectionInitialization.ContentPackageOrder:
                {
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion ||
                        initializationStep == ConnectionInitialization.Password)
                    {
                        initializationStep = ConnectionInitialization.ContentPackageOrder;
                    }

                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }

                    PeerPacketHeaders headers = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsConnectionInitializationStep,
                        Initialization = ConnectionInitialization.ContentPackageOrder
                    };

                    var orderPacket = INetSerializableStruct.Read<ServerPeerContentPackageOrderPacket>(inc.Message);

                    if (!ContentPackageOrderReceived)
                    {
                        ServerContentPackages = orderPacket.ContentPackages;
                        if (ServerContentPackages.Length == 0)
                        {
                            string errorMsg = "Error in ContentPackageOrder message: list of content packages enabled on the server was empty.";
                            GameAnalyticsManager.AddErrorEventOnce("ClientPeer.ReadConnectionInitializationStep:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            DebugConsole.ThrowError(errorMsg);
                        }
                        ContentPackageOrderReceived = true;
                        
                        SendMsgInternal(headers, null);
                    }

                    break;
                }
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion)
                    {
                        initializationStep = ConnectionInitialization.Password;
                    }

                    if (initializationStep != ConnectionInitialization.Password) { return; }

                    var passwordPacket = INetSerializableStruct.Read<ServerPeerPasswordPacket>(inc.Message);

                    passwordPacket.Salt.TryUnwrap(out passwordSalt);
                    passwordPacket.RetriesLeft.TryUnwrap(out var retries);

                    callbacks.OnRequestPassword.Invoke(passwordSalt, retries);
                    break;
            }
        }

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}