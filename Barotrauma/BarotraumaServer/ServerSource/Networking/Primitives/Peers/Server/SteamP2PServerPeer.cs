#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    internal sealed class SteamP2PServerPeer : ServerPeer
    {
        private bool started;

        private readonly SteamId ownerSteamId;

        private UInt64 ownerKey64 => unchecked((UInt64)ownerKey.Fallback(0));

        private SteamId ReadSteamId(IReadMessage inc) => new SteamId(inc.ReadUInt64() ^ ownerKey64);
        private void WriteSteamId(IWriteMessage msg, SteamId val) => msg.WriteUInt64(val.Value ^ ownerKey64);

        public SteamP2PServerPeer(SteamId steamId, int ownerKey, ServerSettings settings)
        {
            serverSettings = settings;

            connectedClients = new List<NetworkConnection>();
            pendingClients = new List<PendingClient>();

            this.ownerKey = Option<int>.Some(ownerKey);

            ownerSteamId = steamId;

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
            SendMsgInternal(ownerSteamId, headers, null);

            started = true;
        }

        public override void Close(string? msg = null)
        {
            if (!started) { return; }

            if (OwnerConnection != null) { OwnerConnection.Status = NetworkConnectionStatus.Disconnected; }

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], DisconnectReason.ServerShutdown, msg);
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            pendingClients.Clear();
            connectedClients.Clear();

            ChildServerRelay.ShutDown();

            OnShutdown?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (!started) { return; }

            if (OnOwnerDetermined != null && OwnerConnection != null)
            {
                OnOwnerDetermined?.Invoke(OwnerConnection);
                OnOwnerDetermined = null;
            }

            //backwards for loop so we can remove elements while iterating
            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                SteamP2PConnection conn = (SteamP2PConnection)connectedClients[i];
                conn.Decay(deltaTime);
                if (conn.Timeout < 0.0)
                {
                    Disconnect(conn, "Timed out");
                }
            }

            try
            {
                while (ChildServerRelay.Read(out byte[] incBuf))
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

            SteamId senderSteamId = ReadSteamId(inc);
            SteamId sentOwnerSteamId = ReadSteamId(inc);

            var (deliveryMethod, packetHeader, initialization) = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (packetHeader.IsServerMessage())
            {
                DebugConsole.ThrowError($"Got server message from {senderSteamId}");
                return;
            }

            if (senderSteamId != ownerSteamId) //sender is remote, handle disconnects and heartbeats
            {
                bool connectionMatches(NetworkConnection conn) =>
                    conn is SteamP2PConnection { Endpoint: SteamP2PEndpoint { SteamId: var steamId } }
                    && steamId == senderSteamId;

                PendingClient? pendingClient = pendingClients.Find(c => connectionMatches(c.Connection));
                SteamP2PConnection? connectedClient = connectedClients.Find(connectionMatches) as SteamP2PConnection;

                pendingClient?.Heartbeat();
                connectedClient?.Heartbeat();

                if (serverSettings.BanList.IsBanned(senderSteamId, out string banReason) ||
                    serverSettings.BanList.IsBanned(sentOwnerSteamId, out banReason))
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                    }
                    else if (connectedClient != null)
                    {
                        Disconnect(connectedClient, $"{DisconnectReason.Banned}/ {banReason}");
                    }
                }
                else if (packetHeader.IsDisconnectMessage())
                {
                    if (pendingClient != null)
                    {
                        string disconnectMsg = $"ServerMessage.HasDisconnected~[client]={pendingClient.Name}";
                        RemovePendingClient(pendingClient, DisconnectReason.Unknown, disconnectMsg);
                    }
                    else if (connectedClient != null)
                    {
                        string disconnectMsg = $"ServerMessage.HasDisconnected~[client]={GameMain.Server.ConnectedClients.First(c => c.Connection == connectedClient).Name}";
                        Disconnect(connectedClient, disconnectMsg, false);
                    }
                }
                else if (packetHeader.IsHeartbeatMessage())
                {
                    //message exists solely as a heartbeat, ignore its contents
                    return;
                }
                else if (packetHeader.IsConnectionInitializationStep())
                {
                    if (pendingClient != null)
                    {
                        pendingClient.Connection.SetAccountInfo(new AccountInfo(senderSteamId, sentOwnerSteamId));
                        ReadConnectionInitializationStep(
                            pendingClient,
                            new ReadWriteMessage(inc.Buffer, inc.BitPosition, inc.LengthBits, false),
                            initialization ?? throw new Exception("Initialization step missing"));
                    }
                    else if (initialization.HasValue)
                    {
                        ConnectionInitialization initializationStep = initialization.Value;
                        if (initializationStep == ConnectionInitialization.ConnectionStarted)
                        {
                            pendingClients.Add(new PendingClient(new SteamP2PConnection(senderSteamId)));
                        }
                    }
                }
                else if (connectedClient != null)
                {
                    var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                    IReadMessage msg = new ReadOnlyMessage(packet.Buffer, packetHeader.IsCompressed(), 0, packet.Length, connectedClient);
                    OnMessageReceived?.Invoke(connectedClient, msg);
                }
            }
            else //sender is owner
            {
                (OwnerConnection as SteamP2PConnection)?.Heartbeat();

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
                        var packet = INetSerializableStruct.Read<SteamP2PInitializationOwnerPacket>(inc);
                        OwnerConnection = new SteamP2PConnection(ownerSteamId)
                        {
                            Language = GameSettings.CurrentConfig.Language
                        };
                        OwnerConnection.SetAccountInfo(new AccountInfo(ownerSteamId, ownerSteamId));

                        OnInitializationComplete?.Invoke(OwnerConnection, packet.OwnerName);
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
                    OnMessageReceived?.Invoke(OwnerConnection!, msg);
                }
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            throw new InvalidOperationException("Called InitializeSteamServerCallbacks on SteamP2PServerPeer!");
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!started) { return; }

            if (!(conn is SteamP2PConnection steamP2PConn)) { return; }

            if (!connectedClients.Contains(steamP2PConn) && conn != OwnerConnection)
            {
                DebugConsole.ThrowError($"Tried to send message to unauthenticated connection: {steamP2PConn.AccountInfo.AccountId}");
                return;
            }

            if (!conn.AccountInfo.AccountId.TryUnwrap(out var connAccountId) || !(connAccountId is SteamId connSteamId)) { return; }

            byte[] bufAux = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);

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
            SendMsgInternal(steamP2PConn, headers, body);
        }

        private void SendDisconnectMessage(SteamId steamId, string? msg)
        {
            if (!started) { return; }

            if (string.IsNullOrWhiteSpace(msg)) { return; }

            var headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsDisconnectMessage | PacketHeader.IsServerMessage,
                Initialization = null
            };
            var packet = new PeerDisconnectPacket
            {
                Message = msg
            };

            SendMsgInternal(steamId, headers, packet);
        }

        private void Disconnect(NetworkConnection conn, string? msg, bool sendDisconnectMessage)
        {
            if (!started) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) { return; }

            if (!conn.AccountInfo.AccountId.TryUnwrap(out var connAccountId) || !(connAccountId is SteamId connSteamId)) { return; }

            if (sendDisconnectMessage) { SendDisconnectMessage(connSteamId, msg); }

            if (connectedClients.Contains(steamp2pConn))
            {
                steamp2pConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(steamp2pConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(connSteamId);
            }
            else if (steamp2pConn == OwnerConnection)
            {
                //TODO: fix?
            }
        }

        public override void Disconnect(NetworkConnection conn, string? msg = null)
        {
            Disconnect(conn, msg, true);
        }

        protected override void SendMsgInternal(NetworkConnection conn, PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            var connSteamId = conn is SteamP2PConnection { Endpoint: SteamP2PEndpoint { SteamId: var id } } ? id : null;
            if (connSteamId is null) { return; }

            SendMsgInternal(connSteamId, headers, body);
        }
        
        private void SendMsgInternal(SteamId connSteamId, PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            WriteSteamId(msgToSend, connSteamId);
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

        protected override void ProcessAuthTicket(ClientSteamTicketAndVersionPacket packet, PendingClient pendingClient)
        {
            pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
            pendingClient.Name = packet.Name;
            pendingClient.AuthSessionStarted = true;
        }
    }
}