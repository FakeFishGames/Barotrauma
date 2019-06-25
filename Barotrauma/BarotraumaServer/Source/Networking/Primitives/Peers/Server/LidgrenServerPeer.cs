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
            public NetConnection Connection;
            public ConnectionInitialization InitializationStep;
            public int Retries;
            public UInt64 SteamID;
            public Int32 PasswordNonce;

            public PendingClient(NetConnection conn)
            {
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicket;
                Retries = 0;
                SteamID = 0;
                PasswordNonce = 0;
            }
        }

        private List<LidgrenConnection> connectedClients;
        private List<PendingClient> pendingClients;

        private List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(ServerSettings settings)
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

            foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType == NetIncomingMessageType.Data))
            {
                HandleDataMessage(inc);
            }
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

            if (pendingClient != null)
            {
                UpdateConnectionValidation(pendingClient, inc);
            }
            else
            {
                LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                if (conn == null)
                {
                    inc.SenderConnection.Disconnect("Received data message from unauthenticated client");
                    return;
                }
                byte isCompressedByte = inc.ReadByte();
                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressedByte != 0, inc.LengthBytes - 1, conn);
                OnMessageReceived?.Invoke(conn, msg);
            }
        }

        private void UpdateConnectionValidation(PendingClient pendingClient, NetIncomingMessage inc)
        {
            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();
            if (pendingClient.InitializationStep != initializationStep) return;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicket:
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticket = inc.ReadBytes(ticketLength);

                    if (pendingClient.SteamID != 0)
                    {
                        bool startedAuthSession = Steam.SteamManager.StartAuthSession(ticket, steamId);
                        if (!startedAuthSession)
                        {
                            pendingClient.Connection.Disconnect("Steam authentication failed");
                            pendingClients.Remove(pendingClient);
                            return;
                        }
                        pendingClient.SteamID = steamId;
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
            }
        }

        public void OnAuthChange(ulong steamID, ulong ownerID, Facepunch.Steamworks.ServerAuth.Status status)
        {
            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);

            if (status == ServerAuth.Status.OK)
            {
                pendingClient.InitializationStep = ConnectionInitialization.Success;
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
                if (strEndPoint.Contains(":"))
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
    }
}
