using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    class LidgrenServerPeer : ServerPeer
    {
        private NetPeerConfiguration netPeerConfiguration;
        private NetServer netServer;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(int? ownKey, ServerSettings settings)
        {
            serverSettings = settings;

            netServer = null;

            connectedClients = new List<NetworkConnection>();
            pendingClients = new List<PendingClient>();

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            ownerKey = ownKey;
        }

        public override void Start()
        {
            if (netServer != null) { return; }

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                AcceptIncomingConnections = true,
                AutoExpandMTU = false,
                MaximumConnections = NetConfig.MaxPlayers * 2,
                EnableUPnP = serverSettings.EnableUPnP,
                Port = serverSettings.Port
            };

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage |
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            netServer = new NetServer(netPeerConfiguration);

            netServer.Start();

            if (serverSettings.EnableUPnP)
            {
                InitUPnP();

                while (DiscoveringUPnP()) { }

                FinishUPnP();
            }
        }

        public override void Close(string msg = null)
        {
            if (netServer == null) { return; }

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], DisconnectReason.ServerShutdown, msg);
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            netServer.Shutdown(msg ?? DisconnectReason.ServerShutdown.ToString());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;

            Steamworks.SteamServer.OnValidateAuthTicketResponse -= OnAuthChange;

            OnShutdown?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (netServer == null) { return; }

            if (OnOwnerDetermined != null && OwnerConnection != null)
            {
                OnOwnerDetermined?.Invoke(OwnerConnection);
                OnOwnerDetermined = null;
            }

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
                GameAnalyticsManager.AddErrorEventOnce("LidgrenServerPeer.Update:ClientReadException" + e.TargetSite.ToString(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];

                var connection = pendingClient.Connection as LidgrenConnection;
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
            if (netServer == null) { return; }

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
                inc.SenderConnection.Deny(DisconnectReason.ServerFull.ToString());
                return;
            }

            if (serverSettings.BanList.IsBanned(inc.SenderConnection.RemoteEndPoint.Address, 0, 0, out string banReason))
            {
                //IP banned: deny immediately
                inc.SenderConnection.Deny(DisconnectReason.Banned.ToString() + "/ " + banReason);
                return;
            }

            PendingClient pendingClient = pendingClients.Find(c => c.Connection is LidgrenConnection l && l.NetConnection == inc.SenderConnection);

            if (pendingClient == null)
            {
                pendingClient = new PendingClient(new LidgrenConnection("PENDING", inc.SenderConnection, 0));
                pendingClients.Add(pendingClient);
            }

            inc.SenderConnection.Approve();
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => (c.Connection is LidgrenConnection l) && l.NetConnection == inc.SenderConnection);

            PacketHeader packetHeader = (PacketHeader)inc.ReadByte();

            if (packetHeader.IsConnectionInitializationStep() && pendingClient != null)
            {
                ReadConnectionInitializationStep(pendingClient, new ReadWriteMessage(inc.Data, (int)inc.Position, inc.LengthBits, false));
            }
            else if (!packetHeader.IsConnectionInitializationStep())
            {
                LidgrenConnection conn = connectedClients.Find(c => (c is LidgrenConnection l) && l.NetConnection == inc.SenderConnection) as LidgrenConnection;
                if (conn == null)
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.AuthenticationRequired, "Received data message from unauthenticated client");
                    }
                    else if (inc.SenderConnection.Status != NetConnectionStatus.Disconnected &&
                             inc.SenderConnection.Status != NetConnectionStatus.Disconnecting)
                    {
                        inc.SenderConnection.Disconnect(DisconnectReason.AuthenticationRequired.ToString() + "/ Received data message from unauthenticated client");
                    }
                    return;
                }
                if (pendingClient != null) { pendingClients.Remove(pendingClient); }
                if (serverSettings.BanList.IsBanned(conn.IPEndPoint.Address, conn.SteamID, conn.OwnerSteamID, out string banReason))
                {
                    Disconnect(conn, DisconnectReason.Banned.ToString() + "/ " + banReason);
                    return;
                }
                UInt16 length = inc.ReadUInt16();

                //DebugConsole.NewMessage(isCompressed + " " + isConnectionInitializationStep + " " + (int)incByte + " " + length);

                IReadMessage msg = new ReadOnlyMessage(inc.Data, packetHeader.IsCompressed(), inc.PositionInBytes, length, conn);
                OnMessageReceived?.Invoke(conn, msg);
            }
        }
        
        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg;
                    LidgrenConnection conn = connectedClients.Select(c => c as LidgrenConnection).FirstOrDefault(c => c.NetConnection == inc.SenderConnection);
                    if (conn != null)
                    {
                        if (conn == OwnerConnection)
                        {
                            DebugConsole.NewMessage("Owner disconnected: closing the server...");
                            GameServer.Log("Owner disconnected: closing the server...", ServerLog.MessageType.ServerMessage);
                            Close(DisconnectReason.ServerShutdown.ToString() + "/ Owner disconnected");
                        }
                        else
                        {
                            disconnectMsg = $"ServerMessage.HasDisconnected~[client]={conn.Name}";
                            Disconnect(conn, disconnectMsg);
                        }
                    }
                    else
                    {
                        PendingClient pendingClient = pendingClients.Find(c => (c.Connection is LidgrenConnection l) && l.NetConnection == inc.SenderConnection);
                        if (pendingClient != null)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.Unknown, $"ServerMessage.HasDisconnected~[client]={pendingClient.Name}");
                        }
                    }
                    break;
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            Steamworks.SteamServer.OnValidateAuthTicketResponse += OnAuthChange;
        }

        private void OnAuthChange(Steamworks.SteamId steamID, Steamworks.SteamId ownerID, Steamworks.AuthResponse status)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);
            DebugConsole.Log(steamID + " validation: " + status+", "+(pendingClient!=null));
            
            if (pendingClient == null)
            {
                if (status != Steamworks.AuthResponse.OK)
                {
                    LidgrenConnection connection = connectedClients.Find(c => c.SteamID == steamID) as LidgrenConnection;
                    if (connection != null)
                    {
                        Disconnect(connection, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam authentication status changed: " + status.ToString());
                    }
                }
                return;
            }

            LidgrenConnection pendingConnection = pendingClient.Connection as LidgrenConnection;
            string banReason;
            if (serverSettings.BanList.IsBanned(pendingConnection.NetConnection.RemoteEndPoint.Address, steamID, ownerID, out banReason))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                return;
            }

            if (status == Steamworks.AuthResponse.OK)
            {
                pendingClient.OwnerSteamID = ownerID;
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "Steam authentication failed: " + status.ToString());
                return;
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) return;
            if (!connectedClients.Contains(lidgrenConn))
            {
                DebugConsole.ThrowError("Tried to send message to unauthenticated connection: " + lidgrenConn.IPString);
                return;
            }

            NetDeliveryMethod lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Unreliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
                    break;
                case DeliveryMethod.Reliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableUnordered;
                    break;
                case DeliveryMethod.ReliableOrdered:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableOrdered;
                    break;
            }

            NetOutgoingMessage lidgrenMsg = netServer.CreateMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, out bool isCompressed, out int length);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            NetSendResult result = netServer.SendMessage(lidgrenMsg, lidgrenConn.NetConnection, lidgrenDeliveryMethod);
            if (result != NetSendResult.Sent && result != NetSendResult.Queued)
            {
                DebugConsole.NewMessage("Failed to send message to "+conn.Name+": " + result.ToString(), Microsoft.Xna.Framework.Color.Yellow);
            }
        }
        
        public override void Disconnect(NetworkConnection conn,string msg=null)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) { return; }
            if (connectedClients.Contains(lidgrenConn))
            {
                lidgrenConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(lidgrenConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(conn.SteamID);
            }
            lidgrenConn.NetConnection.Disconnect(msg ?? "Disconnected");
        }

        protected override void SendMsgInternal(NetworkConnection conn, DeliveryMethod deliveryMethod, IWriteMessage msg)
        {
            LidgrenConnection lidgrenConn = conn as LidgrenConnection;
            NetDeliveryMethod lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Unreliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
                    break;
                case DeliveryMethod.Reliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableUnordered;
                    break;
                case DeliveryMethod.ReliableOrdered:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableOrdered;
                    break;
            }

            NetOutgoingMessage lidgrenMsg = netServer.CreateMessage();
            lidgrenMsg.Write(msg.Buffer, 0, msg.LengthBytes);
            NetSendResult result = netServer.SendMessage(lidgrenMsg, lidgrenConn.NetConnection, lidgrenDeliveryMethod);
            if (result != NetSendResult.Sent && result != NetSendResult.Queued)
            {
                DebugConsole.NewMessage("Failed to send message to " + conn.Name + ": " + result.ToString(), Microsoft.Xna.Framework.Color.Yellow);
            }
        }

        protected override void CheckOwnership(PendingClient pendingClient)
        {
            LidgrenConnection l = pendingClient.Connection as LidgrenConnection;
            if (OwnerConnection == null &&
                    IPAddress.IsLoopback(l.NetConnection.RemoteEndPoint.Address.MapToIPv4NoThrow()) &&
                    ownerKey != null && pendingClient.OwnerKey != 0 && pendingClient.OwnerKey == ownerKey)
            {
                ownerKey = null;
                OwnerConnection = pendingClient.Connection;
            }
        }

        protected override void ProcessAuthTicket(string name, int ownKey, ulong steamId, PendingClient pendingClient, byte[] ticket)
        {
            if (pendingClient.SteamID == null)
            {
                bool requireSteamAuth = GameMain.Config.RequireSteamAuthentication;
#if DEBUG
                requireSteamAuth = false;
#endif

                //steam auth cannot be done (SteamManager not initialized or no ticket given),
                //but it's not required either -> let the client join without auth
                if ((!Steam.SteamManager.IsInitialized || (ticket?.Length ?? 0) == 0) &&
                    !requireSteamAuth)
                {
                    pendingClient.Connection.Name = name;
                    pendingClient.Name = name;
                    pendingClient.OwnerKey = ownKey;
                    pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                }
                else
                {
                    Steamworks.BeginAuthResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                    if (authSessionStartState != Steamworks.BeginAuthResult.OK)
                    {
                        if (requireSteamAuth)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "Steam auth session failed to start: " + authSessionStartState.ToString());
                            return;
                        }
                        else
                        {
                            steamId = 0;
                            pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                        }
                    }
                    pendingClient.SteamID = steamId;
                    pendingClient.Connection.Name = name;
                    pendingClient.Name = name;
                    pendingClient.OwnerKey = ownKey;
                    pendingClient.AuthSessionStarted = true;
                }
            }
            else
            {
                if (pendingClient.SteamID != steamId)
                {
                    RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "SteamID mismatch");
                    return;
                }
            }
        }
    }
}
