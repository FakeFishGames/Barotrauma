#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Barotrauma.Steam;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    internal sealed class LidgrenServerPeer : ServerPeer
    {
        private readonly NetPeerConfiguration netPeerConfiguration;
        private NetServer? netServer;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(Option<int> ownKey, ServerSettings settings, Callbacks callbacks) : base(callbacks)
        {
            serverSettings = settings;

            netServer = null;

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                AcceptIncomingConnections = true,
                AutoExpandMTU = false,
                MaximumConnections = NetConfig.MaxPlayers * 2,
                EnableUPnP = serverSettings.EnableUPnP,
                Port = serverSettings.Port
            };

            netPeerConfiguration.DisableMessageType(
                NetIncomingMessageType.DebugMessage
                | NetIncomingMessageType.WarningMessage
                | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage
                | NetIncomingMessageType.Error
                | NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            connectedClients = new List<NetworkConnection>();
            pendingClients = new List<PendingClient>();

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            ownerKey = ownKey;
        }

        public override void Start()
        {
            if (netServer != null) { return; }

            incomingLidgrenMessages.Clear();

            netServer = new NetServer(netPeerConfiguration);

            netServer.Start();

            if (serverSettings.EnableUPnP)
            {
                InitUPnP();

                while (DiscoveringUPnP()) { }

                FinishUPnP();
            }
        }

        public override void Close()
        {
            if (netServer == null) { return; }

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            netServer.Shutdown(PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown).ToLidgrenStringRepresentation());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;

            Steamworks.SteamServer.OnValidateAuthTicketResponse -= OnAuthChange;

            callbacks.OnShutdown.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (netServer is null) { return; }

            ToolBox.ThrowIfNull(incomingLidgrenMessages);

            netServer.ReadMessages(incomingLidgrenMessages);

            //process incoming connections first
            foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType == NetIncomingMessageType.ConnectionApproval))
            {
                HandleConnection(inc);
            }

            try
            {
                //after processing connections, go ahead with the rest of the messages
                foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType != NetIncomingMessageType.ConnectionApproval))
                {
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

            catch (Exception e)
            {
                string errorMsg = "Server failed to read an incoming message. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce($"LidgrenServerPeer.Update:ClientReadException{e.TargetSite}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];

                LidgrenConnection connection = (LidgrenConnection)pendingClient.Connection;

                if (connection.NetConnection.Status == NetConnectionStatus.InitiatedConnect ||
                    connection.NetConnection.Status == NetConnectionStatus.ReceivedInitiation ||
                    connection.NetConnection.Status == NetConnectionStatus.RespondedAwaitingApproval ||
                    connection.NetConnection.Status == NetConnectionStatus.RespondedConnect)
                {
                    continue;
                }

                UpdatePendingClient(pendingClient);
                if (i >= pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }

            incomingLidgrenMessages.Clear();
        }

        private void InitUPnP()
        {
            if (netServer is null) { return; }

            ToolBox.ThrowIfNull(netPeerConfiguration);

            netServer.UPnP.ForwardPort(netPeerConfiguration.Port, "barotrauma");
#if USE_STEAM
            netServer.UPnP.ForwardPort(serverSettings.QueryPort, "barotrauma");
#endif
        }

        private bool DiscoveringUPnP()
        {
            if (netServer == null) { return false; }

            return netServer.UPnP.Status == UPnPStatus.Discovering;
        }

        private void FinishUPnP()
        {
            //do nothing
        }

        private void HandleConnection(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                inc.SenderConnection.Deny(PeerDisconnectPacket.WithReason(DisconnectReason.ServerFull).ToLidgrenStringRepresentation());
                return;
            }

            if (serverSettings.BanList.IsBanned(new LidgrenEndpoint(inc.SenderConnection.RemoteEndPoint), out string banReason))
            {
                //IP banned: deny immediately
                inc.SenderConnection.Deny(PeerDisconnectPacket.Banned(banReason).ToLidgrenStringRepresentation());
                return;
            }

            PendingClient? pendingClient = pendingClients.Find(c => c.Connection is LidgrenConnection l && l.NetConnection == inc.SenderConnection);

            if (pendingClient is null)
            {
                pendingClient = new PendingClient(new LidgrenConnection(inc.SenderConnection));
                pendingClients.Add(pendingClient);
            }

            inc.SenderConnection.Approve();
        }

        private void HandleDataMessage(NetIncomingMessage lidgrenMsg)
        {
            if (netServer == null) { return; }

            PendingClient? pendingClient = pendingClients.Find(c => c.Connection is LidgrenConnection l && l.NetConnection == lidgrenMsg.SenderConnection);

            IReadMessage inc = lidgrenMsg.ToReadMessage();

            var (_, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (packetHeader.IsConnectionInitializationStep() && pendingClient != null && initialization.HasValue)
            {
                ReadConnectionInitializationStep(pendingClient, inc, initialization.Value);
            }
            else if (!packetHeader.IsConnectionInitializationStep())
            {
                if (!(connectedClients.Find(c => c is LidgrenConnection l && l.NetConnection == lidgrenMsg.SenderConnection) is LidgrenConnection conn))
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationRequired));
                    }
                    else if (lidgrenMsg.SenderConnection.Status != NetConnectionStatus.Disconnected &&
                             lidgrenMsg.SenderConnection.Status != NetConnectionStatus.Disconnecting)
                    {
                        lidgrenMsg.SenderConnection.Disconnect(PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationRequired).ToLidgrenStringRepresentation());
                    }

                    return;
                }

                if (pendingClient != null) { pendingClients.Remove(pendingClient); }

                if (serverSettings.BanList.IsBanned(conn.Endpoint, out string banReason)
                    || (conn.AccountInfo.AccountId.TryUnwrap(out var accountId) && serverSettings.BanList.IsBanned(accountId, out banReason))
                    || conn.AccountInfo.OtherMatchingIds.Any(id => serverSettings.BanList.IsBanned(id, out banReason)))
                {
                    Disconnect(conn, PeerDisconnectPacket.Banned(banReason));
                    return;
                }

                var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                callbacks.OnMessageReceived.Invoke(conn, packet.GetReadMessage(packetHeader.IsCompressed(), conn));
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Disconnected:
                    LidgrenConnection? conn = connectedClients.Cast<LidgrenConnection>().FirstOrDefault(c => c.NetConnection == inc.SenderConnection);
                    if (conn != null)
                    {
                        if (conn == OwnerConnection)
                        {
                            DebugConsole.NewMessage("Owner disconnected: closing the server...");
                            GameServer.Log("Owner disconnected: closing the server...", ServerLog.MessageType.ServerMessage);
                            Close();
                        }
                        else
                        {
                            Disconnect(conn, PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                        }
                    }
                    else
                    {
                        PendingClient? pendingClient = pendingClients.Find(c => c.Connection is LidgrenConnection l && l.NetConnection == inc.SenderConnection);
                        if (pendingClient != null)
                        {
                            RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                        }
                    }

                    break;
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            Steamworks.SteamServer.OnValidateAuthTicketResponse += OnAuthChange;
        }

        private void OnAuthChange(Steamworks.SteamId steamId, Steamworks.SteamId ownerId, Steamworks.AuthResponse status)
        {
            if (netServer == null) { return; }

            PendingClient? pendingClient = pendingClients.Find(c => c.AccountInfo.AccountId is Some<AccountId> { Value: SteamId id } && id.Value == steamId);
            DebugConsole.Log($"{steamId} validation: {status}, {(pendingClient != null)}");

            if (pendingClient is null)
            {
                if (status == Steamworks.AuthResponse.OK) { return; }

                if (connectedClients.Find(c
                        => c.AccountInfo.AccountId is Some<AccountId> { Value: SteamId id } && id.Value == steamId)
                    is LidgrenConnection connection)
                {
                    Disconnect(connection,  PeerDisconnectPacket.SteamAuthError(status));
                }

                return;
            }

            LidgrenConnection pendingConnection = (LidgrenConnection)pendingClient.Connection;
            if (serverSettings.BanList.IsBanned(pendingConnection.Endpoint, out string banReason)
                || serverSettings.BanList.IsBanned(new SteamId(steamId), out banReason)
                || serverSettings.BanList.IsBanned(new SteamId(ownerId), out banReason))
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.Banned(banReason));
                return;
            }

            if (status == Steamworks.AuthResponse.OK)
            {
                pendingClient.Connection.SetAccountInfo(new AccountInfo(new SteamId(steamId), new SteamId(ownerId)));
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.SteamAuthError(status));
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (netServer == null) { return; }

            if (!connectedClients.Contains(conn))
            {
                DebugConsole.ThrowError($"Tried to send message to unauthenticated connection: {conn.Endpoint.StringRepresentation}");
                return;
            }

            byte[] bufAux = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);

#if DEBUG
            ToolBox.ThrowIfNull(netPeerConfiguration);
            netPeerConfiguration.SimulatedDuplicatesChance = GameMain.Server.SimulatedDuplicatesChance;
            netPeerConfiguration.SimulatedMinimumLatency = GameMain.Server.SimulatedMinimumLatency;
            netPeerConfiguration.SimulatedRandomLatency = GameMain.Server.SimulatedRandomLatency;
            netPeerConfiguration.SimulatedLoss = GameMain.Server.SimulatedLoss;
#endif

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
            SendMsgInternal(conn, headers, body);
        }

        public override void Disconnect(NetworkConnection conn, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) { return; }

            if (connectedClients.Contains(lidgrenConn))
            {
                lidgrenConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(lidgrenConn);
                callbacks.OnDisconnect.Invoke(conn, peerDisconnectPacket);
                if (conn.AccountInfo.AccountId is Some<AccountId> { Value: SteamId steamId }) { SteamManager.StopAuthSession(steamId); }
            }

            lidgrenConn.NetConnection.Disconnect(peerDisconnectPacket.ToLidgrenStringRepresentation());
        }

        protected override void SendMsgInternal(NetworkConnection conn, PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.WriteNetSerializableStruct(headers);
            body?.Write(msgToSend);

            NetSendResult result = ForwardToLidgren(msgToSend, conn, headers.DeliveryMethod);
            if (result != NetSendResult.Sent && result != NetSendResult.Queued)
            {
                DebugConsole.NewMessage($"Failed to send message to {conn.Endpoint}: {result}", Microsoft.Xna.Framework.Color.Yellow);
            }
        }

        protected override void CheckOwnership(PendingClient pendingClient)
        {
            if (OwnerConnection == null
                && pendingClient.Connection is LidgrenConnection l
                && IPAddress.IsLoopback(l.NetConnection.RemoteEndPoint.Address)
                && ownerKey.IsSome() && pendingClient.OwnerKey == ownerKey)
            {
                ownerKey = Option<int>.None();
                OwnerConnection = pendingClient.Connection;
                callbacks.OnOwnerDetermined.Invoke(OwnerConnection);
            }
        }

        protected override void ProcessAuthTicket(ClientSteamTicketAndVersionPacket packet, PendingClient pendingClient)
        {
            if (pendingClient.AccountInfo.AccountId.IsNone())
            {
                bool requireSteamAuth = GameSettings.CurrentConfig.RequireSteamAuthentication;
#if DEBUG
                requireSteamAuth = false;
#endif
                bool hasSteamAuth = packet.SteamAuthTicket.TryUnwrap(out var ticket);

                //steam auth cannot be done (SteamManager not initialized or no ticket given),
                //but it's not required either -> let the client join without auth
                if ((!SteamManager.IsInitialized || !hasSteamAuth) && !requireSteamAuth)
                {
                    pendingClient.Name = packet.Name;
                    pendingClient.OwnerKey = packet.OwnerKey;
                    pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                }
                else
                {
                    if (!packet.SteamId.TryUnwrap(out var id) || !(id is SteamId steamId))
                    {
                        if (requireSteamAuth)
                        {
                            RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.SteamAuthenticationFailed));
                            return;
                        }
                    }
                    else
                    {
                        Steamworks.BeginAuthResult authSessionStartState = SteamManager.StartAuthSession(ticket, steamId);
                        if (authSessionStartState != Steamworks.BeginAuthResult.OK)
                        {
                            if (requireSteamAuth)
                            {
                                RemovePendingClient(pendingClient, PeerDisconnectPacket.SteamAuthError(authSessionStartState));
                            }
                            else
                            {
                                packet.SteamId = Option<AccountId>.None();
                                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                            }
                        }
                    }

                    pendingClient.Connection.SetAccountInfo(new AccountInfo(packet.SteamId.Select(uid => (AccountId)uid)));
                    pendingClient.Name = packet.Name;
                    pendingClient.OwnerKey = packet.OwnerKey;
                    pendingClient.AuthSessionStarted = true;
                }
            }
            else
            {
                if (pendingClient.AccountInfo.AccountId != packet.SteamId.Select(uid => (AccountId)uid))
                {
                    RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.SteamAuthenticationFailed));
                }
            }
        }

        private NetSendResult ForwardToLidgren(IWriteMessage msg, NetworkConnection connection, DeliveryMethod deliveryMethod)
        {
            ToolBox.ThrowIfNull(netServer);

            LidgrenConnection conn = (LidgrenConnection)connection;
            return netServer.SendMessage(msg.ToLidgren(netServer), conn.NetConnection, deliveryMethod.ToLidgren());
        }
    }
}