using System;
using System.Net;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    public class LidgrenConnection : NetworkConnection
    {
        public NetConnection NetConnection { get; private set; }

        public IPEndPoint IPEndPoint => NetConnection.RemoteEndPoint;

        public string IPString
        {
            get
            {
                return IPEndPoint.Address.IsIPv4MappedToIPv6 ? IPEndPoint.Address.MapToIPv4NoThrow().ToString() : IPEndPoint.Address.ToString();
            }
        }

        public UInt16 Port
        {
            get
            {
                return (UInt16)IPEndPoint.Port;
            }
        }

        public LidgrenConnection(string name, NetConnection netConnection, UInt64 steamId)
        {
            Name = name;
            NetConnection = netConnection;
            SteamID = steamId;
            EndPointString = IPString;
        }

        public override bool EndpointMatches(string endPoint)
        {
            if (IPEndPoint?.Address == null) { return false; }
            if (!IPAddress.TryParse(endPoint, out IPAddress addr)) { return false; }

            IPAddress ip1 = IPEndPoint.Address.IsIPv4MappedToIPv6 ? IPEndPoint.Address.MapToIPv4() : IPEndPoint.Address;
            IPAddress ip2 = addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr;

            return ip1.ToString() == ip2.ToString();
        }
    }
}
