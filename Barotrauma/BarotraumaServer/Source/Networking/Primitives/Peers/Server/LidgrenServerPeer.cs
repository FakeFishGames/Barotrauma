using System;
using System.Collections.Generic;
using System.Net;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    public class LidgrenServerPeer : ServerPeer
    {
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

        public LidgrenServerPeer(UInt16 port, UInt16 queryPort, int maxConnections, bool enableUPnP)
        {
            netPeerConfiguration = new NetPeerConfiguration("barotrauma");
            netPeerConfiguration.AcceptIncomingConnections = true;
            netPeerConfiguration.AutoExpandMTU = false;
            netPeerConfiguration.MaximumConnections = maxConnections * 2;
            netPeerConfiguration.EnableUPnP = enableUPnP;
            netPeerConfiguration.Port = port;
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

        public override void KickPlayer(NetworkConnection connection, string reason)
        {
            throw new NotImplementedException();
        }

        public override void BanPlayer(NetworkConnection connection, string reason, TimeSpan? duration)
        {
            throw new NotImplementedException();
        }
    }
}
