using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public class SteamP2PConnection : NetworkConnection
    {
        public double Timeout = 0.0;

        public SteamP2PConnection(string name, UInt64 steamId)
        {
            SteamID = steamId;
            EndPointString = SteamID.ToString();
            Name = name;
            Heartbeat();
        }

        public void Decay(float deltaTime)
        {
            Timeout -= deltaTime;
        }

        public void Heartbeat()
        {
            Timeout = 20.0;
        }
    }
}
