#nullable enable
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    internal sealed class P2PServerPeer : ServerPeer<P2PConnection>
    {
        private bool started;

        private readonly P2PEndpoint ownerEndpoint;

        public P2PServerPeer(P2PEndpoint ownerEp, int ownerKey, ServerSettings settings, Callbacks callbacks) : base(callbacks, settings)
        {
            this.ownerKey = Option.Some(ownerKey);

            ownerEndpoint = ownerEp;

            started = false;
        }

        public override void Start()
        {
            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsConnectionInitializationStep | PacketHeader.IsServerMessage,
                Initialization = null
            };
            SendMsgInternal(ownerEndpoint, headers, null);

            started = true;
        }

        public override void Close()
        {
            if (!started) { return; }

            if (OwnerConnection != null) { OwnerConnection.Status = NetworkConnectionStatus.Disconnected; }

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i].Connection, PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            pendingClients.Clear();
            connectedClients.Clear();

            ChildServerRelay.ShutDown();

            callbacks.OnShutdown.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (!started) { return; }

            //backwards for loop so we can remove elements while iterating
            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                var conn = connectedClients[i].Connection;
                conn.Decay(deltaTime);
                if (conn.Timeout < 0.0)
                {
                    Disconnect(conn, PeerDisconnectPacket.WithReason(DisconnectReason.Timeout));
                }
            }

            try
            {
                foreach (var incBuf in ChildServerRelay.Read())
                {
                    IReadMessage inc = new ReadOnlyMessage(incBuf, false, 0, incBuf.Length, OwnerConnection);

                    HandleDataMessage(inc);
                }
            }

            catch (Exception e)
            {
                string errorMsg = "Server failed to read an incoming message. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce($"SteamP2PServerPeer.Update:ClientReadException{e.TargetSite}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient);
                if (i >= pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }
        }

        private void HandleDataMessage(IReadMessage inc)
        {
            if (!started) { return; }

            var senderInfo = INetSerializableStruct.Read<P2POwnerToServerHeader>(inc);
            if (!senderInfo.Endpoint.TryUnwrap(out var senderEndpoint)) { return; }
            var (_, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (packetHeader.IsServerMessage())
            {
                DebugConsole.ThrowError($"Got server message from {senderEndpoint}");
                return;
            }

            if (senderEndpoint != ownerEndpoint) //sender is remote, handle disconnects and heartbeats
            {
                bool connectionMatches(P2PConnection conn) =>
                    conn.Endpoint == senderEndpoint;

                var pendingClient = pendingClients.Find(c => connectionMatches(c.Connection));
                var connectedClient = connectedClients.Find(c => connectionMatches(c.Connection));
                pendingClient?.Connection.SetAccountInfo(senderInfo.AccountInfo);

                pendingClient?.Heartbeat();
                connectedClient?.Connection.Heartbeat();

                if (serverSettings.BanList.IsBanned(senderEndpoint, out string banReason)
                    || serverSettings.BanList.IsBanned(senderInfo.AccountInfo, out banReason))
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.Banned(banReason));
                    }
                    else if (connectedClient != null)
                    {
                        Disconnect(connectedClient.Connection, PeerDisconnectPacket.Banned(banReason));
                    }
                }
                else if (packetHeader.IsDisconnectMessage())
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                    }
                    else if (connectedClient != null)
                    {
                        Disconnect(connectedClient.Connection, PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                    }
                }
                else if (packetHeader.IsHeartbeatMessage())
                {
                    //message exists solely as a heartbeat, ignore its contents
                    return;
                }
                else if (packetHeader.IsConnectionInitializationStep())
                {
                    if (!initialization.HasValue) { return; }
                    ConnectionInitialization initializationStep = initialization.Value;

                    if (pendingClient != null)
                    {
                        ReadConnectionInitializationStep(
                            pendingClient,
                            new ReadWriteMessage(inc.Buffer, inc.BitPosition, inc.LengthBits, false),
                            initializationStep);
                    }
                    else if (initializationStep == ConnectionInitialization.ConnectionStarted)
                    {
                        pendingClients.Add(new PendingClient(senderEndpoint.MakeConnectionFromEndpoint()));
                    }
                }
                else if (connectedClient != null)
                {
                    if (packetHeader.IsDataFragment())
                    {
                        var fragment = INetSerializableStruct.Read<MessageFragment>(inc);
                        var completeMessageOption = connectedClient.Defragmenter.ProcessIncomingFragment(fragment);
                        if (!completeMessageOption.TryUnwrap(out var completeMessage)) { return; }

                        IReadMessage msg = new ReadOnlyMessage(completeMessage.ToArray(), false, 0, completeMessage.Length, connectedClient.Connection);
                        callbacks.OnMessageReceived.Invoke(connectedClient.Connection, msg);
                    }
                    else
                    {
                        var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                        IReadMessage msg = new ReadOnlyMessage(packet.Buffer, packetHeader.IsCompressed(), 0, packet.Length, connectedClient.Connection);
                        callbacks.OnMessageReceived.Invoke(connectedClient.Connection, msg);
                    }
                }
            }
            else //sender is owner
            {
                OwnerConnection?.Heartbeat();

                if (packetHeader.IsDisconnectMessage())
                {
                    DebugConsole.ThrowError("Received disconnect message from owner");
                    return;
                }

                if (packetHeader.IsServerMessage())
                {
                    DebugConsole.ThrowError("Received server message from owner");
                    return;
                }

                if (packetHeader.IsConnectionInitializationStep())
                {
                    if (OwnerConnection is null)
                    {
                        var packet = INetSerializableStruct.Read<P2PInitializationOwnerPacket>(inc);
                        OwnerConnection = ownerEndpoint.MakeConnectionFromEndpoint();
                        OwnerConnection.Language = GameSettings.CurrentConfig.Language;
                        OwnerConnection.SetAccountInfo(senderInfo.AccountInfo);

                        callbacks.OnInitializationComplete.Invoke(OwnerConnection, packet.Name);
                        callbacks.OnOwnerDetermined.Invoke(OwnerConnection);
                    }

                    return;
                }

                if (packetHeader.IsHeartbeatMessage())
                {
                    return;
                }
                else
                {
                    var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                    IReadMessage msg = new ReadOnlyMessage(packet.Buffer, packetHeader.IsCompressed(), 0, packet.Length, OwnerConnection);
                    callbacks.OnMessageReceived.Invoke(OwnerConnection!, msg);
                }
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!started) { return; }

            if (conn is not P2PConnection p2pConn) { return; }

            int ccIndex = connectedClients.FindIndex(cc => cc.Connection == p2pConn);
            if (ccIndex < 0 && conn != OwnerConnection)
            {
                DebugConsole.ThrowError($"Tried to send message to unauthenticated connection: {p2pConn.AccountInfo.AccountId}");
                return;
            }
            byte[] bufAux = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);

            if (bufAux.Length > MessageFragment.MaxSize && conn != OwnerConnection)
            {
                var cc = connectedClients[ccIndex];
                var fragments = cc.Fragmenter.FragmentMessage(msg.Buffer.AsSpan()[..msg.LengthBytes]);
                foreach (var fragment in fragments)
                {
                    var fragmentHeaders = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsDataFragment
                                       | PacketHeader.IsServerMessage,
                        Initialization = null
                    };
                    SendMsgInternal(p2pConn, fragmentHeaders, fragment);
                }
                return;
            }

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = deliveryMethod,
                PacketHeader = (isCompressed ? PacketHeader.IsCompressed : PacketHeader.None)
                               | PacketHeader.IsServerMessage,
                Initialization = null
            };
            var body = new PeerPacketMessage
            {
                Buffer = bufAux
            };
            SendMsgInternal(p2pConn, headers, body);
        }

        private void SendDisconnectMessage(P2PEndpoint endpoint, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (!started) { return; }

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsDisconnectMessage | PacketHeader.IsServerMessage,
                Initialization = null
            };

            SendMsgInternal(endpoint, headers, peerDisconnectPacket);
        }

        public override void Disconnect(NetworkConnection conn, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (!started) { return; }

            if (conn is not P2PConnection p2pConn) { return; }

            SendDisconnectMessage(p2pConn.Endpoint, peerDisconnectPacket);

            if (connectedClients.FindIndex(cc => cc.Connection == p2pConn) is >= 0 and var ccIndex)
            {
                p2pConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.RemoveAt(ccIndex);
                callbacks.OnDisconnect.Invoke(conn, peerDisconnectPacket);
            }
            else if (p2pConn == OwnerConnection)
            {
                throw new InvalidOperationException("Cannot disconnect owner peer");
            }
        }

        protected override void SendMsgInternal(P2PConnection conn, PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            SendMsgInternal(conn.Endpoint, headers, body);
        }

        private void SendMsgInternal(P2PEndpoint connEndpoint, PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.WriteNetSerializableStruct(new P2PServerToOwnerHeader
            {
                EndpointStr = connEndpoint.StringRepresentation
            });
            msgToSend.WriteNetSerializableStruct(headers);
            body?.Write(msgToSend);

            ForwardToOwnerProcess(msgToSend);
        }

        private static void ForwardToOwnerProcess(IWriteMessage msg)
        {
            byte[] bufToSend = (byte[])msg.Buffer.Clone();
            Array.Resize(ref bufToSend, msg.LengthBytes);
            ChildServerRelay.Write(bufToSend);
        }

        protected override void ProcessAuthTicket(ClientAuthTicketAndVersionPacket packet, PendingClient pendingClient)
        {
            // Do nothing with the auth ticket because that should be handled by the owner peer,
            // just assume that authentication succeeded
            pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
            pendingClient.Name = packet.Name;
            pendingClient.AuthSessionStarted = true;
        }
    }
}
