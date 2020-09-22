using Barotrauma.Steam;
using System;

namespace Barotrauma.Networking
{
    public class PipeConnection : NetworkConnection
    {
        public PipeConnection(ulong steamId)
        {
            EndPointString = "PIPE";
            SteamID = steamId;
        }

        public override bool EndpointMatches(string endPoint)
        {
            return SteamManager.SteamIDStringToUInt64(endPoint) == SteamID || endPoint == "PIPE";
        }
    }
}

