#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Barotrauma.Steam;
using System.Threading;

namespace Barotrauma.Networking
{
    internal sealed class SteamP2PClientPeer : ClientPeer
    {
        private readonly SteamId hostSteamId;
        private double timeout;
        private double heartbeatTimer;
        private double connectionStatusTimer;

        private long sentBytes, receivedBytes;

        private readonly List<IncomingInitializationMessage> incomingInitializationMessages = new List<IncomingInitializationMessage>();
        private readonly List<IReadMessage> incomingDataMessages = new List<IReadMessage>();

        public SteamP2PClientPeer(SteamP2PEndpoint endpoint, Callbacks callbacks) : base(endpoint, callbacks, Option<int>.None())
        {
            ServerConnection = null;

            isActive = false;

            if (!(ServerEndpoint is SteamP2PEndpoint steamIdEndpoint))
            {
                throw new InvalidCastException("endPoint is not SteamId");
            }

            hostSteamId = steamIdEndpoint.SteamId;
        }

        public override void Start()
        {
            if (isActive) { return; }

            ContentPackageOrderReceived = false;

            steamAuthTicket = SteamManager.GetAuthSessionTicket();
            //TODO: wait for GetAuthSessionTicketResponse_t

            if (steamAuthTicket == null)
            {
                throw new Exception("GetAuthSessionTicket returned null");
            }

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.OnP2PSessionRequest = OnIncomingConnection;
            Steamworks.SteamNetworking.OnP2PConnectionFailed = OnConnectionFailed;

            Steamworks.SteamNetworking.AllowP2PPacketRelay(true);

            ServerConnection = new SteamP2PConnection(hostSteamId);
            ServerConnection.SetAccountInfo(new AccountInfo(hostSteamId));

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsConnectionInitializationStep,
                Initialization = ConnectionInitialization.ConnectionStarted
            };
            SendMsgInternal(headers, null);

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            timeout = NetworkConnection.TimeoutThreshold;
            heartbeatTimer = 1.0;
            connectionStatusTimer = 0.0;

            isActive = true;
        }

        private void OnIncomingConnection(Steamworks.SteamId steamId)
        {
            if (!isActive) { return; }

            if (steamId == hostSteamId.Value)
            {
                Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamId);
            }
            else if (initializationStep != ConnectionInitialization.Password &&
                     initializationStep != ConnectionInitialization.ContentPackageOrder &&
                     initializationStep != ConnectionInitialization.Success)
            {
                DebugConsole.ThrowError("Connection from incorrect SteamID was rejected: " +
                                        $"expected {hostSteamId}," +
                                        $"got {new SteamId(steamId)}");
            }
        }

        private void OnConnectionFailed(Steamworks.SteamId steamId, Steamworks.P2PSessionError error)
        {
            if (!isActive) { return; }

            if (steamId != hostSteamId.Value) { return; }

            Close(PeerDisconnectPacket.SteamP2PError(error));
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength)
        {
            if (!isActive) { return; }

            if (steamId != hostSteamId.Value) { return; }

            timeout = Screen.Selected == GameMain.GameScreen
                ? NetworkConnection.TimeoutThresholdInGame
                : NetworkConnection.TimeoutThreshold;

            try
            {
                IReadMessage inc = new ReadOnlyMessage(data, false, 0, dataLength, ServerConnection);
                ProcessP2PData(inc);
            }
            catch (Exception e)
            {
                string errorMsg = $"Client failed to read an incoming P2P message. {{{e}}}\n{e.StackTrace.CleanupStackTrace()}";
                GameAnalyticsManager.AddErrorEventOnce($"SteamP2PClientPeer.OnP2PData:ClientReadException{e.TargetSite}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }
        }

        private void ProcessP2PData(IReadMessage inc)
        {
            var (deliveryMethod, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (!packetHeader.IsServerMessage()) { return; }

            if (packetHeader.IsConnectionInitializationStep())
            {
                if (!initialization.HasValue) { return; }

                var relayPacket = INetSerializableStruct.Read<SteamP2PInitializationRelayPacket>(inc);

                SteamManager.JoinLobby(relayPacket.LobbyID, false);
                if (initializationStep != ConnectionInitialization.Success)
                {
                    incomingInitializationMessages.Add(new IncomingInitializationMessage
                    {
                        InitializationStep = initialization.Value,
                        Message = relayPacket.Message.GetReadMessageUncompressed()
                    });
                }
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

            if (initializationStep != ConnectionInitialization.Password &&
                initializationStep != ConnectionInitialization.ContentPackageOrder &&
                initializationStep != ConnectionInitialization.Success)
            {
                connectionStatusTimer -= deltaTime;
                if (connectionStatusTimer <= 0.0)
                {
                    if (Steamworks.SteamNetworking.GetP2PSessionState(hostSteamId.Value) is { } state)
                    {
                        if (state.P2PSessionError != Steamworks.P2PSessionError.None)
                        {
                            Close(PeerDisconnectPacket.SteamP2PError(state.P2PSessionError));
                        }
                    }
                    else
                    {
                        Close(PeerDisconnectPacket.WithReason(DisconnectReason.Timeout));
                    }

                    connectionStatusTimer = 1.0f;
                }
            }

            for (int i = 0; i < 100; i++)
            {
                if (!Steamworks.SteamNetworking.IsP2PPacketAvailable()) { break; }

                var packet = Steamworks.SteamNetworking.ReadP2PPacket();
                if (packet is { SteamId: var steamId, Data: var data })
                {
                    OnP2PData(steamId, data, data.Length);
                    if (!isActive) { return; }
                    receivedBytes += data.Length;
                }
            }

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
                    callbacks.OnInitializationComplete.Invoke();
                    initializationStep = ConnectionInitialization.Success;
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

            SteamManager.LeaveLobby();

            isActive = false;

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsDisconnectMessage,
                Initialization = null
            };
            SendMsgInternal(headers, peerDisconnectPacket);

            Thread.Sleep(100);

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.CloseP2PSessionWithUser(hostSteamId.Value);

            steamAuthTicket?.Cancel();
            steamAuthTicket = null;

            callbacks.OnDisconnect.Invoke(peerDisconnectPacket);
        }

        protected override void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.WriteNetSerializableStruct(headers);
            body?.Write(msgToSend);
            ForwardToSteamP2P(msgToSend, headers.DeliveryMethod);
        }

        private void ForwardToSteamP2P(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            heartbeatTimer = 5.0;
            int length = msg.LengthBytes;

            bool successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, msg.Buffer, length, 0, deliveryMethod.ToSteam());
            sentBytes += length;

            if (successSend) { return; }

            if (deliveryMethod is DeliveryMethod.Unreliable)
            {
                DebugConsole.Log($"WARNING: message couldn't be sent unreliably, forcing reliable send ({length} bytes)");
                successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, msg.Buffer, length, 0, DeliveryMethod.Reliable.ToSteam());
                sentBytes += length;
            }

            if (!successSend)
            {
                DebugConsole.AddWarning($"Failed to send message to remote peer! ({length} bytes)");
            }
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            timeout = 0.0f;
        }
#endif
    }
}