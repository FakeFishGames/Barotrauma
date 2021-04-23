using Barotrauma.Steam;
using System;

namespace Barotrauma.Networking
{
    public class SteamP2PConnection : NetworkConnection
    {
        public double Timeout = 0.0;

        public SteamP2PConnection(string name, UInt64 steamId)
        {
            SteamID = steamId;
            OwnerSteamID = 0;
            EndPointString = SteamManager.SteamIDUInt64ToString(SteamID);
            Name = name;
            Heartbeat();
        }

        public void Decay(float deltaTime)
        {
            Timeout -= deltaTime;
        }

        public void Heartbeat()
        {
            Timeout = TimeoutThreshold;
        }

        public override bool EndpointMatches(string endPoint)
        {
            return SteamManager.SteamIDStringToUInt64(endPoint) == SteamID;
        }
    }
}
