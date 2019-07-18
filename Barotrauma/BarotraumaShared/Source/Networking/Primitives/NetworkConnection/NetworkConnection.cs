using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace Barotrauma.Networking
{
    public enum NetworkConnectionStatus
    {
        Connected = 0x1,
        Disconnected = 0x2
    }

    public abstract class NetworkConnection
    {
        public string Name;
        public IPAddress IP
        {
            get;
            protected set;
        }
        public UInt64 SteamID
        {
            get;
            protected set;
        }
        public string EndPointString
        {
            get;
            protected set;
        }

        public NetworkConnectionStatus Status = NetworkConnectionStatus.Disconnected;
    }
}
