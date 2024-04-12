#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Lidgren.Network;
using Barotrauma.Steam;
using System.Net.Sockets;

namespace Barotrauma.Networking
{
    internal sealed class LidgrenClientPeer : ClientPeer<LidgrenEndpoint>
    {
        private NetClient? netClient;
        private readonly NetPeerConfiguration netPeerConfiguration;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenClientPeer(LidgrenEndpoint endpoint, Callbacks callbacks, Option<int> ownerKey) : base(endpoint, ((Endpoint)endpoint).ToEnumerable().ToImmutableArray(), callbacks, ownerKey)
        {
            ServerConnection = null;

            netClient = null;
            isActive = false;

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                DualStack = GameSettings.CurrentConfig.UseDualModeSockets
            };
            if (endpoint.NetEndpoint.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                netPeerConfiguration.LocalAddress = System.Net.IPAddress.IPv6Any;
            }

            netPeerConfiguration.DisableMessageType(
                NetIncomingMessageType.DebugMessage
                | NetIncomingMessageType.WarningMessage
                | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage
                | NetIncomingMessageType.Error);

            incomingLidgrenMessages = new List<NetIncomingMessage>();
        }

        public override void Start()
        {
            if (isActive) { return; }

            incomingLidgrenMessages.Clear();

            ContentPackageOrderReceived = false;

            netClient = new NetClient(netPeerConfiguration);

            initializationStep = ConnectionInitialization.AuthInfoAndVersion;

            if (ServerEndpoint is not { } lidgrenEndpointValue)
            {
                throw new InvalidCastException($"Endpoint is not {nameof(LidgrenEndpoint)}");
            }

            if (ServerConnection != null)
            {
                throw new InvalidOperationException("ServerConnection is not null");
            }

            netClient.Start();

            TaskPool.Add(
                $"{nameof(LidgrenClientPeer)}.GetAuthTicket",
                AuthenticationTicket.Create(ServerEndpoint),
                t =>
                {
                    if (!t.TryGetResult(out Option<AuthenticationTicket> authenticationTicket))
                    {
                        Close(PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
                        return;
                    }
                    authTicket = authenticationTicket;

                    var netConnection = netClient.Connect(lidgrenEndpointValue.NetEndpoint);
        
                    ServerConnection = new LidgrenConnection(netConnection)
                    {
                        Status = NetworkConnectionStatus.Connected
                    };
                });

            isActive = true;
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            ToolBox.ThrowIfNull(netClient);
            ToolBox.ThrowIfNull(incomingLidgrenMessages);

            if (IsOwner && !ChildServerRelay.IsProcessAlive)
            {
                var gameClient = GameMain.Client;
                Close(PeerDisconnectPacket.WithReason(DisconnectReason.ServerCrashed));
                gameClient?.CreateServerCrashMessage();
                return;
            }

            incomingLidgrenMessages.Clear();
            netClient.ReadMessages(incomingLidgrenMessages);

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, netClient.Statistics.ReceivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, netClient.Statistics.SentBytes);

            foreach (NetIncomingMessage inc in incomingLidgrenMessages)
            {
                var remoteEndpoint = new LidgrenEndpoint(inc.SenderEndPoint);
                
                if (remoteEndpoint != ServerEndpoint)
                {
                    DebugConsole.AddWarning($"Mismatched endpoint: expected {ServerEndpoint.NetEndpoint}, got {inc.SenderConnection.RemoteEndPoint}");
                    continue;
                }

                switch (inc.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        HandleDataMessage(inc);
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        HandleStatusChanged(inc);
                        break;
                }
            }
        }

        private void HandleDataMessage(NetIncomingMessage lidgrenMsg)
        {
            if (!isActive) { return; }

            ToolBox.ThrowIfNull(ServerConnection);

            IReadMessage inc = lidgrenMsg.ToReadMessage();

            var (_, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (packetHeader.IsConnectionInitializationStep())
            {
                if (initializationStep == ConnectionInitialization.Success) { return; }

                ReadConnectionInitializationStep(new IncomingInitializationMessage
                {
                    InitializationStep = initialization ?? throw new Exception("Initialization step missing"),
                    Message = inc
                });
            }
            else
            {
                OnInitializationComplete();

                var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                callbacks.OnMessageReceived.Invoke(packet.GetReadMessage(packetHeader.IsCompressed(), ServerConnection));
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            NetConnectionStatus status = inc.ReadHeader<NetConnectionStatus>();
            switch (status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg = inc.ReadString();
                    var peerDisconnectPacket =
                        PeerDisconnectPacket.FromLidgrenStringRepresentation(disconnectMsg);
                    Close(peerDisconnectPacket.Fallback(PeerDisconnectPacket.WithReason(DisconnectReason.Unknown)));
                    break;
            }
        }

        public override void SendPassword(string password)
        {
            if (!isActive) { return; }

            ToolBox.ThrowIfNull(netClient);

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

            ToolBox.ThrowIfNull(netClient);

            isActive = false;

            netClient.Shutdown(peerDisconnectPacket.ToLidgrenStringRepresentation());
            netClient = null;

            callbacks.OnDisconnect.Invoke(peerDisconnectPacket);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            ToolBox.ThrowIfNull(netClient);
            ToolBox.ThrowIfNull(netPeerConfiguration);

#if DEBUG
            netPeerConfiguration.SimulatedDuplicatesChance = GameMain.Client.SimulatedDuplicatesChance;
            netPeerConfiguration.SimulatedMinimumLatency = GameMain.Client.SimulatedMinimumLatency;
            netPeerConfiguration.SimulatedRandomLatency = GameMain.Client.SimulatedRandomLatency;
            netPeerConfiguration.SimulatedLoss = GameMain.Client.SimulatedLoss;
#endif

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
            
            SendMsgInternal(headers, body);
        }

        protected override void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            ToolBox.ThrowIfNull(netClient);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteNetSerializableStruct(headers);
            body?.Write(msg);

            NetSendResult result = ForwardToLidgren(msg, DeliveryMethod.Reliable);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage($"Failed to send message to host: {result}\n{Environment.StackTrace}");
            }
        }

        private NetSendResult ForwardToLidgren(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            ToolBox.ThrowIfNull(netClient);

            return netClient.SendMessage(msg.ToLidgren(netClient), deliveryMethod.ToLidgren());
        }

        protected override async Task<Option<AccountId>> GetAccountId()
        {
            if (!EosInterface.Core.IsInitialized) { return SteamManager.GetSteamId().Select(id => (AccountId)id); }

            var selfPuids = EosInterface.IdQueries.GetLoggedInPuids();
            if (selfPuids.None()) { return Option.None; }
            var accountIdsResult = await EosInterface.IdQueries.GetSelfExternalAccountIds(selfPuids.First());
            return accountIdsResult.TryUnwrapSuccess(out var accountIds) && accountIds.Length > 0
                ? Option.Some(accountIds[0])
                : Option.None;
        }

#if DEBUG
        

        public override void ForceTimeOut()
        {
            netClient?.ServerConnection?.ForceTimeOut();
        }

        public override void DebugSendRawMessage(IWriteMessage msg)
            => ForwardToLidgren(msg, DeliveryMethod.Reliable);
#endif
    }
}