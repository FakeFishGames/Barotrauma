using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public string Name;
        public string IP; public bool IsRangeBan;
        public UInt64 SteamID;
        public string Reason;
        public DateTime? ExpirationTime;
        public UInt16 UniqueIdentifier;
    }

    public partial class BanList
    {
        const string SavePath = "Data/bannedplayers.txt";

        private List<BannedPlayer> bannedPlayers;

        public IEnumerable<string> BannedNames
        {
            get { return bannedPlayers.Select(bp => bp.Name); }
        }

        public IEnumerable<string> BannedIPs
        {
            get { return bannedPlayers.Select(bp => bp.IP); }
        }

        partial void InitProjectSpecific();


        public BanList()
        {
            bannedPlayers = new List<BannedPlayer>();
            InitProjectSpecific();
        }

        public string ToRange(string ip)
        {
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
