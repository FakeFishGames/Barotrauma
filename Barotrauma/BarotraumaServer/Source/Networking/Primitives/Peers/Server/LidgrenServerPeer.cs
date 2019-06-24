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

            public PendingClient(NetConnection conn)
            {
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicket;
                Retries = 0;
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
        }

        public override void Close()
        {
            netServer.Shutdown(DisconnectReason.ServerShutdown.ToString());
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

        private void HandleConnection(NetIncomingMessage inc)
        {
            PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

            if (inc.MessageType == NetIncomingMessageType.ConnectionApproval)
            {
                if (pendingClient == null)
                {
                    pendingClient = new PendingClient(inc.SenderConnection);
                    pendingClients.Add(pendingClient);
                }
            }
            else if (inc.MessageType == NetIncomingMessageType.Data)
            {
                //TODO: check step, do steam validation, etc
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
