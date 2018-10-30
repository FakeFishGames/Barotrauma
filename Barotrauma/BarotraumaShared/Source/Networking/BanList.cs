using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public string Name;
        public string IP;
        public UInt64 SteamID;
        public string Reason;
        public DateTime? ExpirationTime;
        public Int32 UniqueIdentifier;

        public bool CompareTo(string ipCompare)
        {
            int rangeBanIndex = IP.IndexOf(".x");
            if (rangeBanIndex <= -1)
            {
                return ipCompare == IP;
            }
            else
            {
                if (ipCompare.Length < rangeBanIndex) return false;
                return ipCompare.Substring(0, rangeBanIndex) == IP.Substring(0, rangeBanIndex);
            }
        }
    }

    partial class BanList
    {
        const string SavePath = "Data/bannedplayers.txt";

        private List<BannedPlayer> bannedPlayers;

        partial void InitProjectSpecific();

        public BanList()
        {
            bannedPlayers = new List<BannedPlayer>();

            InitProjectSpecific();
        }

        public bool IsBanned(string IP, ulong steamID)
        {
            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);
            return bannedPlayers.Any(bp => bp.CompareTo(IP) || (steamID != 0 && bp.SteamID == steamID));
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
