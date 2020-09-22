using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public string Name;
        public string EndPoint; public bool IsRangeBan;
        public UInt64 SteamID;
        public string Reason;
        public DateTime? ExpirationTime;
        public UInt16 UniqueIdentifier;

        private void ParseEndPointAsSteamId()
        {
            ulong endPointAsSteamId = SteamManager.SteamIDStringToUInt64(EndPoint);
            if (endPointAsSteamId != 0 && SteamID == 0) { SteamID = endPointAsSteamId; }
        }
    }

    partial class BanList
    {
        private readonly List<BannedPlayer> bannedPlayers;
        public IEnumerable<string> BannedNames
        {
            get { return bannedPlayers.Select(bp => bp.Name); }
        }

        public IEnumerable<string> BannedEndPoints
        {
            get { return bannedPlayers.Select(bp => bp.EndPoint).Where(endPoint => !string.IsNullOrEmpty(endPoint)); }
        }

        partial void InitProjectSpecific();


        public BanList()
        {
            bannedPlayers = new List<BannedPlayer>();
            InitProjectSpecific();
        }

        public static string ToRange(string ip)
        {
            if (SteamManager.SteamIDStringToUInt64(ip) != 0) { return ip; }
            for (int i = ip.Length - 1; i > 0; i--)
            {
                if (ip[i] == '.')
                {
                    ip = ip.Substring(0, i) + ".x";
                    break;
                }
            }
            return ip;
        }
    }
}
