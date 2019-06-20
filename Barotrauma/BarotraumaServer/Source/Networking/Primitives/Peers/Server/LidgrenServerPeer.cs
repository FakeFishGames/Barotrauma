using System;
using System.Collections.Generic;
using System.Net;
using Lidgren.Network;

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

        private List<LidgrenConnection> connectedClients;
        private List<LidgrenConnection> pendingClients;

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
            pendingClients = new List<LidgrenConnection>();
        }

        public override NetworkConnection GetPlayerByName(string name)
        {
            return connectedClients.Find(c => c.Name == name);
        }

        public override NetworkConnection GetPlayerByEndPoint(object endPoint)
        {
            if (endPoint is IPEndPoint)
            {
                IPEndPoint ipEndPoint = (IPEndPoint)endPoint;
                return connectedClients.Find(c => c.IPEndPoint == ipEndPoint);
            }
            else if (endPoint is string)
            {
                string strEndPoint = (string)endPoint;
                if (strEndPoint.Contains(":"))
                {
                    string[] split = strEndPoint.Split(':');
                    string ip = split[0]; UInt16 port = UInt16.Parse(split[1]);
                    return connectedClients.Find(c => c.IP == ip && c.Port == port);
                }
                return connectedClients.Find(c => c.IP == strEndPoint);
            }
            return null;
        }
    }
}
