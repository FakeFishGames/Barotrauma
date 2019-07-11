using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public class SteamP2PConnection : NetworkConnection
    {
        public double Timeout = 0.0;

        public SteamP2PConnection(UInt64 steamId)
        {
            SteamID = steamId;
            Heartbeat();
        }

        public void Heartbeat()
        {
            Timeout = Timing.TotalTime + 5.0;
        }
    }
}
