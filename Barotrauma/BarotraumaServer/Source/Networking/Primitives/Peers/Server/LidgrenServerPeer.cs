using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Lidgren.Network;
using Facepunch.Steamworks;

namespace Barotrauma.Networking
{
    class LidgrenServerPeer : ServerPeer
    {
        private ServerSettings serverSettings;

        private NetPeerConfiguration netPeerConfiguration;
        private NetServer netServer;

        private int maximumConnections
        {
            get { return netPeerConfiguration.MaximumConnections / 2; }
        }

        public int ConnectedClientsCount
        {
            get { return connectedClients.Count; }
        }

        private class PendingClient
        {
            public string Name;
            public NetConnection Connection;
            public ConnectionInitialization InitializationStep;
            public double UpdateTime;
            public double TimeOut;
            public int Retries;
            public UInt64? SteamID;
            public Int32? PasswordNonce;

            public PendingClient(NetConnection conn)
            {
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicket;
                Retries = 0;
                SteamID = null;
                PasswordNonce = null;
                UpdateTime = Timing.TotalTime;
                TimeOut = Timing.TotalTime + 20.0;
            }
        }

        private List<LidgrenConnection> connectedClients;
        private List<PendingClient> pendingClients;

        private List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(int? ownerKey, ServerSettings settings)
        {
            serverSettings = settings;
            netPeerConfiguration = new NetPeerConfiguration("barotrauma");
            netPeerConfiguration.AcceptIncomingConnections = true;
            netPeerConfiguration.AutoExpandMTU = false;
            netPeerConfiguration.MaximumConnections = serverSettings.MaxPlayers * 2;
            netPeerConfiguration.EnableUPnP = serverSettings.EnableUPnP;
            netPeerConfiguration.Port = serverSettings.Port;
            
            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage |
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            netServer = new NetServer(netPeerConfiguration);

            connectedClients = new List<LidgrenConnection>();
            pendingClients = new List<PendingClient>();

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            OwnerKey = ownerKey;
        }

        public override void Start()
        {
            netServer.Start();

            if (serverSettings.EnableUPnP)
            {
                InitUPnP();

                while (DiscoveringUPnP()) { }

                FinishUPnP();
            }
        }

        public override void Close(string msg=null)
        {
            netServer.Shutdown(msg ?? DisconnectReason.ServerShutdown.ToString());
        }

        public override void Update()
        {
            netServer.ReadMessages(incomingLidgrenMessages);
            
            foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType == NetIncomingMessageType.ConnectionApproval))
            {
                HandleConnection(inc);
            }

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

            for (int i=0;i<pendingClients.Count;i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient);
                if (i>=pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }

            incomingLidgrenMessages.Clear();
        }

        private void InitUPnP()
        {
            netServer.UPnP.ForwardPort(netPeerConfiguration.Port, "barotrauma");
            if (Steam.SteamManager.USE_STEAM)
            {
                netServer.UPnP.ForwardPort(serverSettings.QueryPort, "barotrauma");
            }
        }

        private bool DiscoveringUPnP()
        {
            return netServer.UPnP.Status == UPnPStatus.Discovering;
        }

        private void FinishUPnP()
        {
            //do nothing
        }

        private void HandleConnection(NetIncomingMessage inc)
        {
            if (inc.MessageType == NetIncomingMessageType.ConnectionApproval)
            {
                if (serverSettings.BanList.IsBanned(inc.SenderConnection.RemoteEndPoint.Address, 0))
                {
                    //IP banned: deny immediately
                    //TODO: use TextManager
                    inc.SenderConnection.Deny("IP banned");
                    return;
                }

                PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

                if (pendingClient == null)
                {
                    pendingClient = new PendingClient(inc.SenderConnection);
                    pendingClients.Add(pendingClient);
                }

                inc.SenderConnection.Approve();
            }
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & 0x1) != 0;
            bool isConnectionInitializationStep = (incByte & 0x2) != 0;
            if (isConnectionInitializationStep && pendingClient != null)
            {
                ReadConnectionInitializationStep(pendingClient, inc);
            }
            else if (!isConnectionInitializationStep)
            {
                LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                if (conn == null)
                {
                    inc.SenderConnection.Disconnect("Received data message from unauthenticated client");
                    if (pendingClient != null) { pendingClients.Remove(pendingClient); }
                    return;
                }
                if (pendingClient != null) { pendingClients.Remove(pendingClient); }
                if (serverSettings.BanList.IsBanned(conn.IPEndPoint.Address, conn.SteamID))
                {
                    inc.SenderConnection.Disconnect("Received data message from banned client");
                    connectedClients.Remove(conn);
                    return;
                }
                UInt16 length = inc.ReadUInt16();
                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, conn);
                OnMessageReceived?.Invoke(conn, msg);
            }
        }
        
        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Disconnected:
                    LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                    if (conn != null)
                    {
                        OnStatusChanged?.Invoke(conn, ConnectionStatus.Disconnected);
                        connectedClients.Remove(conn);
                    }
                    break;
            }
        }

        private void ReadConnectionInitializationStep(PendingClient pendingClient, NetIncomingMessage inc)
        {
            pendingClient.TimeOut = Timing.TotalTime + 20.0;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();
            if (pendingClient.InitializationStep != initializationStep) return;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicket:
                    string name = inc.ReadString();
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticket = inc.ReadBytes(ticketLength);

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        pendingClient.Connection.Disconnect("The name \""+name+"\" is invalid");
                        pendingClients.Remove(pendingClient);
                        return;
                    }

                    if (pendingClient.SteamID == null)
                    {
                        ServerAuth.StartAuthSessionResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                        if (authSessionStartState != ServerAuth.StartAuthSessionResult.OK)
                        {
                            pendingClient.Connection.Disconnect("Steam auth session failed to start: "+authSessionStartState.ToString());
                            pendingClients.Remove(pendingClient);
                            return;
                        }
                        pendingClient.SteamID = steamId;
                        pendingClient.Name = name;
                    }
                    else //TODO: could remove since this seems impossible
                    {
                        if (pendingClient.SteamID != steamId)
                        {
                            pendingClient.Connection.Disconnect("SteamID mismatch");
                            pendingClients.Remove(pendingClient);
                            return;
                        }
                    }
                    break;
                case ConnectionInitialization.Password:
                    string incPassword = inc.ReadString();
                    if (pendingClient.PasswordNonce == null)
                    {
                        DebugConsole.ThrowError("Received password message from client without nonce");
                        return;
                    }
                    if (serverSettings.IsPasswordCorrect(incPassword, pendingClient.PasswordNonce.Value))
                    {
                        pendingClient.InitializationStep = ConnectionInitialization.Success;
                    }
                    else
                    {
                        pendingClient.Retries++;

                        if (pendingClient.Retries >= 3)
                        {
                            string banMsg = "Failed to enter correct password too many times";
                            pendingClient.Connection.Disconnect(banMsg);
                            if (pendingClient.SteamID != null)
                            {
                                serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.SteamID.Value, banMsg, null);
                            }
                            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.Connection.RemoteEndPoint.Address, banMsg, null);
                            pendingClients.Remove(pendingClient);
                            return;
                        }
                    }
                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
            }
        }

        private void UpdatePendingClient(PendingClient pendingClient)
        {
            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, pendingClient.SteamID ?? 0))
            {
                pendingClient.Connection.Disconnect("Initialization interrupted by ban");
                pendingClients.Remove(pendingClient);
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                LidgrenConnection newConnection = new LidgrenConnection(pendingClient.Name, pendingClient.Connection, pendingClient.SteamID ?? 0);
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);
                OnInitializationComplete?.Invoke(newConnection);
            }

            if (Timing.TotalTime > pendingClient.TimeOut)
            {
                pendingClient.Connection.Disconnect("Timed out");
                pendingClients.Remove(pendingClient);
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;

            NetOutgoingMessage outMsg = netServer.CreateMessage();
            outMsg.Write((byte)0x2);
            outMsg.Write((byte)pendingClient.InitializationStep);
            switch (pendingClient.InitializationStep)
            {
                case ConnectionInitialization.Password:
                    outMsg.Write(pendingClient.PasswordNonce == null); outMsg.WritePadBits();
                    if (pendingClient.PasswordNonce == null)
                    {
                        pendingClient.PasswordNonce = CryptoRandom.Instance.Next();
                        outMsg.Write(pendingClient.PasswordNonce.Value);
                    }
                    else
                    {
                        outMsg.Write(pendingClient.Retries);
                    }
                    break;
            }

            netServer.SendMessage(outMsg, pendingClient.Connection, NetDeliveryMethod.ReliableUnordered);
        }

        public override void OnAuthChange(ulong steamID, ulong ownerID, ServerAuth.Status status)
        {
            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);
            if (pendingClient == null) { return; }

            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, steamID))
            {
                pendingClient.Connection.Disconnect("SteamID banned");
                pendingClients.Remove(pendingClient);
                return;
            }

            if (status == ServerAuth.Status.OK)
            {
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.Success;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                pendingClient.Connection.Disconnect("Steam authentication failed: "+status.ToString());
                pendingClients.Remove(pendingClient);
                return;
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
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
            byte[] msgData = new byte[1500];
            bool isCompressed; int length;
            msg.PrepareForSending(msgData, out isCompressed, out length);
            lidgrenMsg.Write((byte)(isCompressed ? 0x1 : 0x0));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netServer.SendMessage(lidgrenMsg, lidgrenConn.NetConnection, lidgrenDeliveryMethod);
        }

        public override NetworkConnection GetConnectionByName(string name)
        {
            return connectedClients.Find(c => c.Name == name);
        }

        public override NetworkConnection GetConnectionByEndPoint(object endPoint)
        {
            if (endPoint is IPEndPoint)
            {
                IPEndPoint ipEndPoint = (IPEndPoint)endPoint;
                return connectedClients.Find(c => c.IPEndPoint.Address.Equals(ipEndPoint.Address) && c.IPEndPoint.Port == ipEndPoint.Port);
            }
            else if (endPoint is string)
            {
                string strEndPoint = (string)endPoint;
                int colonCount = strEndPoint.Count(c => c == ':');
                if (colonCount == 1 || colonCount == 7)
                {
                    string[] split = strEndPoint.Split(':');
                    string ip = string.Join(":", split, 0, split.Length-1); UInt16 port = UInt16.Parse(split[split.Length-1]);
                    return connectedClients.Find(c => c.IPString == ip && c.Port == port);
                }
                return connectedClients.Find(c => c.IPString == strEndPoint);
            }
            return null;
        }

        public override NetworkConnection GetConnectionBySteamID(ulong steamId)
        {
            return connectedClients.Find(c => c.SteamID == steamId);
        }

        public override void Disconnect(NetworkConnection conn,string msg=null)
        {
            if (!(conn is LidgrenConnection lidgrenConn)) { return; }
            if (connectedClients.Contains(lidgrenConn))
            {
                connectedClients.Remove(lidgrenConn);
            }
            lidgrenConn.NetConnection.Disconnect(msg ?? "Disconnected");
        }
    }
}
