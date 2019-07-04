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
                return IPEndPoint.Address.IsIPv4MappedToIPv6 ? IPEndPoint.Address.MapToIPv4().ToString() : IPEndPoint.Address.ToString();
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
            IP = netConnection.RemoteEndPoint.Address;
            EndPointString = IPString;
        }
    }
}
