#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    internal sealed class P2PClientPeer : ClientPeer<P2PEndpoint>
    {
        private double timeout;
        private double heartbeatTimer;

        private long sentBytes, receivedBytes;

        private readonly List<IncomingInitializationMessage> incomingInitializationMessages = new List<IncomingInitializationMessage>();
        private readonly List<IReadMessage> incomingDataMessages = new List<IReadMessage>();
        private readonly MessageFragmenter fragmenter = new();
        private readonly MessageDefragmenter defragmenter = new();

        private P2PSocket? socket;

        private static P2PEndpoint GetPrimaryEndpoint(ImmutableArray<P2PEndpoint> allEndpoints)
        {
            var steamEndpointOption = allEndpoints.OfType<SteamP2PEndpoint>().FirstOrNone();
            var eosEndpointOption = allEndpoints.OfType<EosP2PEndpoint>().FirstOrNone();
            if (SteamManager.IsInitialized)
            {
                if (steamEndpointOption.TryUnwrap(out var steamEndpoint)) { return steamEndpoint; }
            }
            if (EosInterface.Core.IsInitialized)
            {
                if (eosEndpointOption.TryUnwrap(out var eosEndpoint)) { return eosEndpoint; }
            }

            throw new Exception($"Couldn't pick out a primary endpoint: {string.Join(", ", allEndpoints.Select(e => e.GetType().Name))}");
        }

        public P2PClientPeer(ImmutableArray<P2PEndpoint> allEndpoints, Callbacks callbacks)
            : base(
                GetPrimaryEndpoint(allEndpoints),
                allEndpoints.Cast<Endpoint>().ToImmutableArray(),
                callbacks,
                Option.None)
        {
            ServerConnection = null;

            isActive = false;
        }

        public override void Start()
        {
            ContentPackageOrderReceived = false;

            ServerConnection = ServerEndpoint.MakeConnectionFromEndpoint();

            var socketCallbacks = new P2PSocket.Callbacks(OnIncomingConnection, OnConnectionClosed, OnP2PData);
            var socketCreateResult = ServerEndpoint switch
            {
                EosP2PEndpoint => EosP2PSocket.Create(socketCallbacks),
                SteamP2PEndpoint steamP2PEndpoint => SteamConnectSocket.Create(steamP2PEndpoint, socketCallbacks),
                _ => throw new Exception($"Invalid server endpoint: {ServerEndpoint.GetType()} {ServerEndpoint}")
            };
            socket = socketCreateResult.TryUnwrapSuccess(out var s)
                ? s
                : throw new Exception($"Failed to create socket for {ServerEndpoint}: {socketCreateResult}");
            TaskPool.Add(
                $"{nameof(P2PClientPeer)}.GetAuthTicket",
                AuthenticationTicket.Create(ServerEndpoint),
                t =>
                {
                    if (!t.TryGetResult(out Option<AuthenticationTicket> authenticationTicket))
                    {
                        Close(PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
                        return;
                    }
                    authTicket = authenticationTicket;

                    var headers = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsConnectionInitializationStep,
                        Initialization = ConnectionInitialization.ConnectionStarted
                    };
                    SendMsgInternal(headers, null);
                });
            initializationStep = ConnectionInitialization.AuthInfoAndVersion;

            timeout = NetworkConnection.TimeoutThreshold;
            heartbeatTimer = 1.0;

            isActive = true;
        }

        private bool OnIncomingConnection(P2PEndpoint remoteEndpoint)
        {
            if (remoteEndpoint == ServerEndpoint)
            {
                return true;
            }

            if (initializationStep != ConnectionInitialization.Password &&
                 initializationStep != ConnectionInitialization.ContentPackageOrder &&
                 initializationStep != ConnectionInitialization.Success)
            {
                DebugConsole.AddWarning(
                    "Connection from incorrect endpoint was rejected: " +
                    $"expected {ServerEndpoint}, " +
                    $"got {remoteEndpoint}");
            }

            return false;
        }

        private void OnConnectionClosed(P2PEndpoint remoteEndpoint, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (remoteEndpoint != ServerEndpoint) { return; }

            Close(peerDisconnectPacket);
        }

        private void OnP2PData(P2PEndpoint senderEndpoint, IReadMessage inc)
        {
            if (!isActive) { return; }

            receivedBytes += inc.LengthBytes;

            if (senderEndpoint != ServerEndpoint) { return; }

            timeout = Screen.Selected == GameMain.GameScreen
                ? NetworkConnection.TimeoutThresholdInGame
                : NetworkConnection.TimeoutThreshold;

            var (_, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (!packetHeader.IsServerMessage()) { return; }

            if (packetHeader.IsConnectionInitializationStep())
            {
                if (!initialization.HasValue) { return; }

                var relayPacket = INetSerializableStruct.Read<P2PInitializationRelayPacket>(inc);

                if (initializationStep != ConnectionInitialization.Success)
                {
                    incomingInitializationMessages.Add(new IncomingInitializationMessage
                    {
                        InitializationStep = initialization.Value,
                        Message = relayPacket.Message.GetReadMessageUncompressed()
                    });
                }
            }
            else if (packetHeader.IsDataFragment())
            {
                var completeMessageOption = defragmenter.ProcessIncomingFragment(INetSerializableStruct.Read<MessageFragment>(inc));
                if (!completeMessageOption.TryUnwrap(out var completeMessage)) { return; }

                int completeMessageLengthBits = completeMessage.Length * 8;
                incomingDataMessages.Add(new ReadWriteMessage(completeMessage.ToArray(), 0, completeMessageLengthBits, copyBuf: false));
            }
            else if (packetHeader.IsHeartbeatMessage())
            {
                return; //TODO: implement heartbeats
            }
            else if (packetHeader.IsDisconnectMessage())
            {
                PeerDisconnectPacket packet = INetSerializableStruct.Read<PeerDisconnectPacket>(inc);
                Close(packet);
            }
            else
            {
                var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                incomingDataMessages.Add(packet.GetReadMessage(packetHeader.IsCompressed(), ServerConnection!));
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (GameMain.Client == null || !GameMain.Client.RoundStarting)
            {
                timeout -= deltaTime;
            }

            heartbeatTimer -= deltaTime;

            socket?.ProcessIncomingMessages();

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, receivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, sentBytes);

            if (heartbeatTimer < 0.0)
            {
                var headers = new PeerPacketHeaders
                {
                    DeliveryMethod = DeliveryMethod.Unreliable,
                    PacketHeader = PacketHeader.IsHeartbeatMessage,
                    Initialization = null
                };
                SendMsgInternal(headers, null);
            }

            if (timeout < 0.0)
            {
                Close(PeerDisconnectPacket.WithReason(DisconnectReason.SteamP2PTimeOut));
                return;
            }

            if (initializationStep != ConnectionInitialization.Success)
            {
                if (incomingDataMessages.Count > 0)
                {
                    void initializationError(string errorMsg, string analyticsTag)
                    {
                        GameAnalyticsManager.AddErrorEventOnce($"SteamP2PClientPeer.OnInitializationComplete:{analyticsTag}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        DebugConsole.ThrowError(errorMsg);
                        Close(PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                    }
                    
                    if (!ContentPackageOrderReceived)
                    {
                        initializationError(
                            errorMsg: "Error during connection initialization: completed initialization before receiving content package order.",
                            analyticsTag: "ContentPackageOrderNotReceived");
                        return;
                    }
                    if (ServerContentPackages.Length == 0)
                    {
                        initializationError(
                            errorMsg: "Error during connection initialization: list of content packages enabled on the server was empty when completing initialization.",
                            analyticsTag: "NoContentPackages");
                        return;
                    }
                    OnInitializationComplete();
                }
                else
                {
                    foreach (var inc in incomingInitializationMessages)
                    {
                        ReadConnectionInitializationStep(inc);
                    }
                }
            }

            if (initializationStep == ConnectionInitialization.Success)
            {
                foreach (IReadMessage inc in incomingDataMessages)
                {
                    callbacks.OnMessageReceived.Invoke(inc);
                }
            }

            incomingInitializationMessages.Clear();
            incomingDataMessages.Clear();
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            byte[] bufAux = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);
            if (bufAux.Length > MessageFragment.MaxSize)
            {
                var fragments = fragmenter.FragmentMessage(msg.Buffer.AsSpan()[..msg.LengthBytes]);
                foreach (var fragment in fragments)
                {
                    var fragmentHeaders = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsDataFragment,
                        Initialization = null
                    };
                    SendMsgInternal(fragmentHeaders, fragment);
                }
                return;
            }

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = deliveryMethod,
                PacketHeader = isCompressed ? PacketHeader.IsCompressed : PacketHeader.None,
                Initialization = null
            };
            var body = new PeerPacketMessage
            {
                Buffer = bufAux
            };

            heartbeatTimer = 5.0;

            // Using an extra local method here to reduce chance of error whenever we need to change this
            void performSend() => SendMsgInternal(headers, body);
#if DEBUG
            CoroutineManager.Invoke(() =>
                {
                    if (GameMain.Client == null) { return; }

                    if (Rand.Range(0.0f, 1.0f) < GameMain.Client.SimulatedLoss && deliveryMethod is DeliveryMethod.Unreliable) { return; }

                    int count = Rand.Range(0.0f, 1.0f) < GameMain.Client.SimulatedDuplicatesChance ? 2 : 1;
                    for (int i = 0; i < count; i++)
                    {
                        performSend();
                    }
                },
                GameMain.Client.SimulatedMinimumLatency + Rand.Range(0.0f, GameMain.Client.SimulatedRandomLatency));
#else
            performSend();
#endif
        }

        public override void SendPassword(string password)
        {
            if (!isActive) { return; }

            if (initializationStep != ConnectionInitialization.Password) { return; }

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsConnectionInitializationStep,
                Initialization = ConnectionInitialization.Password
            };
            var body = new ClientPeerPasswordPacket
            {
                Password = ServerSettings.SaltPassword(Encoding.UTF8.GetBytes(password), passwordSalt)
            };

            SendMsgInternal(headers, body);
        }

        public override void Close(PeerDisconnectPacket peerDisconnectPacket)
        {
            if (!isActive) { return; }

            isActive = false;

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsDisconnectMessage,
                Initialization = null
            };
            SendMsgInternal(headers, peerDisconnectPacket);

            Thread.Sleep(100);

            socket?.CloseConnection(ServerEndpoint);
            socket?.Dispose();
            socket = null;

            callbacks.OnDisconnect.Invoke(peerDisconnectPacket);
        }

        protected override void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.WriteNetSerializableStruct(headers);
            body?.Write(msgToSend);
            ForwardToRemotePeer(msgToSend, headers.DeliveryMethod);
        }

        private void ForwardToRemotePeer(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            if (!isActive) { return; }
            if (socket is null) { return; }
            
            heartbeatTimer = 5.0;

            int length = msg.LengthBytes;

            if (length + 4 >= MsgConstants.MTU)
            {
                DebugConsole.Log($"WARNING: message length comes close to exceeding MTU, forcing reliable send ({length} bytes)");
                deliveryMethod = DeliveryMethod.Reliable;
            }

            bool success = socket.SendMessage(ServerEndpoint, msg, deliveryMethod);

            sentBytes += length;

            if (success) { return; }

            if (deliveryMethod is DeliveryMethod.Unreliable)
            {
                DebugConsole.Log($"WARNING: message couldn't be sent unreliably, forcing reliable send ({length} bytes)");
                success = socket.SendMessage(ServerEndpoint, msg, DeliveryMethod.Reliable);
                sentBytes += length;
            }

            if (!success)
            {
                DebugConsole.AddWarning($"Failed to send message to remote peer! ({length} bytes)");
            }
        }

        protected override async Task<Option<AccountId>> GetAccountId()
        {
            if (SteamManager.IsInitialized) { return SteamManager.GetSteamId().Select(id => (AccountId)id); }

            if (EosInterface.IdQueries.GetLoggedInPuids() is not { Length: > 0 } puids)
            {
                return Option.None;
            }
            var externalAccountIdsResult = await EosInterface.IdQueries.GetSelfExternalAccountIds(puids[0]);
            if (!externalAccountIdsResult.TryUnwrapSuccess(out var externalAccountIds)
                || externalAccountIds is not { Length: > 0 })
            {
                return Option.None;
            }
            return Option.Some(externalAccountIds[0]);
        }

#if DEBUG

        public override void ForceTimeOut()
        {
            timeout = 0.0f;
        }
        
        public override void DebugSendRawMessage(IWriteMessage msg)
            => ForwardToRemotePeer(msg, DeliveryMethod.Reliable);
#endif
    }
}