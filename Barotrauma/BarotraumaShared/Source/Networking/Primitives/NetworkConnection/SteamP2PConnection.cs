using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public class SteamP2PConnection : NetworkConnection
    {
        public SteamP2PConnection(UInt64 steamId)
        {
            SteamID = steamId;
        }
    }
}
