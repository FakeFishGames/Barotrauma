#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Lidgren.Network;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    internal sealed class LidgrenClientPeer : ClientPeer
    {
        private NetClient? netClient;
        private readonly NetPeerConfiguration netPeerConfiguration;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        private LidgrenEndpoint lidgrenEndpoint =>
            ServerConnection is LidgrenConnection { Endpoint: LidgrenEndpoint result }
                ? result
                : throw new InvalidOperationException();

        public LidgrenClientPeer(LidgrenEndpoint endpoint, Callbacks callbacks, Option<int> ownerKey) : base(endpoint, callbacks, ownerKey)
        {
            ServerConnection = null;

            netClient = null;
            isActive = false;

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                UseDualModeSockets = GameSettings.CurrentConfig.UseDualModeSockets
            };

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

            if (SteamManager.IsInitialized)
            {
                steamAuthTicket = SteamManager.GetAuthSessionTicket();
                if (steamAuthTicket == null)
                {
                    throw new Exception("GetAuthSessionTicket returned null");
                }
            }

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            if (!(ServerEndpoint is LidgrenEndpoint lidgrenEndpointValue))
            {
                throw new InvalidCastException($"Endpoint is not {nameof(LidgrenEndpoint)}");
            }

            if (ServerConnection != null)
            {
                throw new InvalidOperationException("ServerConnection is not null");
            }

            netClient.Start();

            var netConnection = netClient.Connect(lidgrenEndpointValue.NetEndpoint);

            ServerConnection = new LidgrenConnection(netConnection)
            {
                Status = NetworkConnectionStatus.Connected
            };

            isActive = true;
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            ToolBox.ThrowIfNull(netClient);
            ToolBox.ThrowIfNull(incomingLidgrenMessages);

            if (isOwner && !(ChildServerRelay.Process is { HasExited: false }))
            {
                Close(PeerDisconnectPacket.WithReason(DisconnectReason.ServerCrashed));
                var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), ChildServerRelay.CrashMessage);
                msgBox.Buttons[0].OnClicked += (btn, obj) =>
                {
                    GameMain.MainMenuScreen.Select();
                    return false;
                };
                return;
            }

            incomingLidgrenMessages.Clear();
            netClient.ReadMessages(incomingLidgrenMessages);

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, netClient.Statistics.ReceivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, netClient.Statistics.SentBytes);

            foreach (NetIncomingMessage inc in incomingLidgrenMessages)
            {
                if (!inc.SenderConnection.RemoteEndPoint.Equals(lidgrenEndpoint.NetEndpoint))
                {
                    DebugConsole.AddWarning($"Mismatched endpoint: expected {lidgrenEndpoint.NetEndpoint}, got {inc.SenderConnection.RemoteEndPoint}");
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
                if (initializationStep != ConnectionInitialization.Success)
                {
                    callbacks.OnInitializationComplete.Invoke();
                    initializationStep = ConnectionInitialization.Success;
                }

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

            steamAuthTicket?.Cancel();
            steamAuthTicket = null;

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

#if DEBUG
        public override void ForceTimeOut()
        {
            netClient?.ServerConnection?.ForceTimeOut();
        }
#endif
    }
}