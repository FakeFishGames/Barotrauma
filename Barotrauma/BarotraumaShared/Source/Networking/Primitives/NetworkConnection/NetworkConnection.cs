using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace Barotrauma.Networking
{
    public enum NetworkConnectionStatus
    {
        Connected = 0x1,
        Pending = 0x2,
        Disconnected = 0x4,
        Banned = 0x4 | 0x8
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

        public NetworkConnectionStatus Status;
    }
}
