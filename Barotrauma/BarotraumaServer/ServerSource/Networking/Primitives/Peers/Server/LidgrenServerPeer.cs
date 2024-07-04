#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Steam;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    internal sealed class LidgrenServerPeer : ServerPeer<LidgrenConnection>
    {
        private readonly NetPeerConfiguration netPeerConfiguration;
        private ImmutableDictionary<AuthenticationTicketKind, Authenticator>? authenticators;
        private NetServer? netServer;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(Option<int> ownKey, ServerSettings settings, Callbacks callbacks) : base(callbacks, settings)
        {
            authenticators = null;
            netServer = null;

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                AcceptIncomingConnections = true,
                AutoExpandMTU = false,
                MaximumConnections = NetConfig.MaxPlayers * 2,
                EnableUPnP = serverSettings.EnableUPnP,
                Port = serverSettings.Port,
                DualStack = GameSettings.CurrentConfig.UseDualModeSockets
            };

            netPeerConfiguration.DisableMessageType(
                NetIncomingMessageType.DebugMessage
                | NetIncomingMessageType.WarningMessage
                | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage
                | NetIncomingMessageType.Error
                | NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            ownerKey = ownKey;
        }

        public override void Start()
        {
            if (netServer != null) { return; }

            authenticators = Authenticator.GetAuthenticatorsForHost(Option.None);

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
                Disconnect(connectedClients[i].Connection, PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            netServer.Shutdown(PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown).ToLidgrenStringRepresentation());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;

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
            if (SteamManager.IsInitialized)
            {
                netServer.UPnP.ForwardPort(serverSettings.QueryPort, "barotrauma");
            }
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

            PendingClient? pendingClient = pendingClients.Find(c => c.Connection.NetConnection == inc.SenderConnection);

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

            PendingClient? pendingClient = pendingClients.Find(c => c.Connection.NetConnection == lidgrenMsg.SenderConnection);

            IReadMessage inc = lidgrenMsg.ToReadMessage();

            var (_, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (packetHeader.IsConnectionInitializationStep() && pendingClient != null && initialization.HasValue)
            {
                ReadConnectionInitializationStep(pendingClient, inc, initialization.Value);
            }
            else if (!packetHeader.IsConnectionInitializationStep())
            {
                if (FindConnection(lidgrenMsg.SenderConnection) is not { } conn)
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

            LidgrenConnection? FindConnection(NetConnection ligdrenConn)
            {
                if (connectedClients.Find(c => c.Connection.NetConnection == ligdrenConn) is { Connection: LidgrenConnection conn })
                {
                    return conn;
                }
                return null;
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Disconnected:
                    LidgrenConnection? conn = connectedClients.Select(c => c.Connection).FirstOrDefault(c => c.NetConnection == inc.SenderConnection);
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

        private void OnSteamAuthChange(Steamworks.SteamId steamId, Steamworks.SteamId ownerId, Steamworks.AuthResponse status)
        {
            if (netServer == null) { return; }

            PendingClient? pendingClient = pendingClients.Find(c => c.AccountInfo.AccountId.TryUnwrap<SteamId>(out var id) && id.Value == steamId);
            DebugConsole.Log($"{steamId} validation: {status}, {(pendingClient != null)}");

            if (pendingClient is null)
            {
                if (status == Steamworks.AuthResponse.OK) { return; }

                if (connectedClients.Find(c
                        => c.Connection.AccountInfo.AccountId.TryUnwrap<SteamId>(out var id) && id.Value == steamId)
                    is { Connection: LidgrenConnection connection })
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

            if (conn is not LidgrenConnection lidgrenConnection)
            {
                DebugConsole.ThrowError($"Tried to send message to connection of incorrect type: expected {nameof(LidgrenConnection)}, got {conn.GetType().Name}");
                return;
            }

            if (!connectedClients.Any(cc => cc.Connection == lidgrenConnection))
            {
                DebugConsole.ThrowError($"Tried to send message to unauthenticated connection: {lidgrenConnection.Endpoint.StringRepresentation}");
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
            SendMsgInternal(lidgrenConnection, headers, body);
        }

        public override void Disconnect(NetworkConnection conn, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (netServer == null) { return; }

            if (conn is not LidgrenConnection lidgrenConn) { return; }

            if (connectedClients.FindIndex(cc => cc.Connection == lidgrenConn) is >= 0 and var ccIndex)
            {
                lidgrenConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.RemoveAt(ccIndex);
                callbacks.OnDisconnect.Invoke(conn, peerDisconnectPacket);
                if (conn.AccountInfo.AccountId.TryUnwrap(out var accountId))
                {
                    authenticators?.Values.ForEach(authenticator => authenticator.EndAuthSession(accountId));
                }
            }

            lidgrenConn.NetConnection.Disconnect(peerDisconnectPacket.ToLidgrenStringRepresentation());
        }

        protected override void SendMsgInternal(LidgrenConnection conn, PeerPacketHeaders headers, INetSerializableStruct? body)
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
            if (OwnerConnection != null
                || pendingClient.Connection is not LidgrenConnection l
                || !IPAddress.IsLoopback(l.NetConnection.RemoteEndPoint.Address)
                || !ownerKey.IsSome() || pendingClient.OwnerKey != ownerKey)
            {
                return;
            }

            ownerKey = Option.None;
            OwnerConnection = pendingClient.Connection;
            callbacks.OnOwnerDetermined.Invoke(OwnerConnection);
        }

        private enum AuthResult
        {
            Success,
            Failure
        }

        protected override void ProcessAuthTicket(ClientAuthTicketAndVersionPacket packet, PendingClient pendingClient)
        {
            if (pendingClient.AccountInfo.AccountId.IsSome())
            {
                if (pendingClient.AccountInfo.AccountId != packet.AccountId)
                {
                    RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
                }
                return;
            }

            void acceptClient(AccountInfo accountInfo)
            {
                pendingClient.Connection.SetAccountInfo(accountInfo);
                pendingClient.Name = packet.Name;
                pendingClient.OwnerKey = packet.OwnerKey;
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
            }

            void rejectClient()
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
            }

            if (authenticators is null && 
                GameMain.Server.ServerSettings.RequireAuthentication)
            {
                DebugConsole.NewMessage(
                    "The server is configured to require authentication from clients, but there are no authenticators available. " +
                    $"If you're for example trying to host a server in a local network without being connected to Steam or Epic Online Services, please set {nameof(GameMain.Server.ServerSettings.RequireAuthentication)} to false in the server settings.", 
                    Microsoft.Xna.Framework.Color.Yellow);
            }

            if (authenticators is null
                || !packet.AuthTicket.TryUnwrap(out var authTicket)
                || !authenticators.TryGetValue(authTicket.Kind, out var authenticator))
            {                
#if DEBUG
                DebugConsole.NewMessage("Debug server accepts unauthenticated connections", Microsoft.Xna.Framework.Color.Yellow);
                acceptClient(new AccountInfo(new UnauthenticatedAccountId(packet.Name)));
#else
                if (GameMain.Server.ServerSettings.RequireAuthentication)
                {
                    DebugConsole.NewMessage(
                        "A client attempted to join without an authentication ticket, but the server is configured to require authentication. " +
                        $"If you're for example trying to host a server in a local network without being connected to Steam or Epic Online Services, please set {nameof(GameMain.Server.ServerSettings.RequireAuthentication)} to false in the server settings.",
                        Microsoft.Xna.Framework.Color.Yellow);
                    rejectClient();
                }
                else
                {
                    acceptClient(new AccountInfo(new UnauthenticatedAccountId(packet.Name)));
                }
#endif
                return;
            }

            pendingClient.AuthSessionStarted = true;
            TaskPool.Add($"{nameof(LidgrenServerPeer)}.ProcessAuth", authenticator.VerifyTicket(authTicket), t =>
            {
                if (!t.TryGetResult(out AccountInfo accountInfo)
                    || accountInfo.IsNone)
                {
                    rejectClient();
                    return;
                }

                acceptClient(accountInfo);
            });
        }

        private NetSendResult ForwardToLidgren(IWriteMessage msg, NetworkConnection connection, DeliveryMethod deliveryMethod)
        {
            ToolBox.ThrowIfNull(netServer);

            LidgrenConnection conn = (LidgrenConnection)connection;
            return netServer.SendMessage(msg.ToLidgren(netServer), conn.NetConnection, deliveryMethod.ToLidgren());
        }
    }
}