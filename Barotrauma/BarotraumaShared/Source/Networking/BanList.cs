using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    class BannedPlayer
    {
        public string Name;
        public string IP;
        public string Reason;

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

        public BannedPlayer(string name, string ip, string reason)
        {
            this.Name = name;
            this.IP = ip;
            this.Reason = reason;
        }
    }

    partial class BanList
    {
        const string SavePath = "Data/bannedplayers.txt";

        private List<BannedPlayer> bannedPlayers;

        public BanList()
        {
            bannedPlayers = new List<BannedPlayer>();

            if (File.Exists(SavePath))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to open the list of banned players in " + SavePath, e);
                    return;
                }

                foreach (string line in lines)
                {
                    string[] separatedLine = line.Split(',');
                    if (separatedLine.Length < 2) continue;

                    string name     = separatedLine[0];
                    string ip       = separatedLine[1];
                    string reason   = separatedLine.Length > 2 ? string.Join(",", separatedLine.Skip(2)) : "";

                    bannedPlayers.Add(new BannedPlayer(name, ip,reason));
                }
            }
        }

        public void BanPlayer(string name, string ip, string reason)
        {
            if (bannedPlayers.Any(bp => bp.IP == ip)) return;

            System.Diagnostics.Debug.Assert(!name.Contains(','));

            DebugConsole.Log("Banned " + name);

            bannedPlayers.Add(new BannedPlayer(name, ip, reason));
            Save();
        }

        public bool IsBanned(string IP)
        {
            return bannedPlayers.Any(bp => bp.CompareTo(IP));
        }

        private void RemoveBan(BannedPlayer banned)
        {
            DebugConsole.Log("Removing ban from " + banned.Name);
            GameServer.Log("Removing ban from " + banned.Name, ServerLog.MessageType.ServerMessage);

            bannedPlayers.Remove(banned);

            Save();
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

        private void RangeBan(BannedPlayer banned)
        {
            banned.IP = ToRange(banned.IP);

            BannedPlayer bp;
            while ((bp = bannedPlayers.Find(x => banned.CompareTo(x.IP))) != null)
            {
                //remove all specific bans that are now covered by the rangeban
                bannedPlayers.Remove(bp);
            }

            bannedPlayers.Add(banned);

            Save();
        }

        public void Save()
        {
            GameServer.Log("Saving banlist", ServerLog.MessageType.ServerMessage);

            List<string> lines = new List<string>();

            foreach (BannedPlayer banned in bannedPlayers)
            {
                string line = banned.Name + "," + banned.IP;
                if (!string.IsNullOrWhiteSpace(banned.Reason)) line += "," + banned.Reason;
                lines.Add(line);
            }

            try
            {
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the list of banned players to " + SavePath + " failed", e);
            }
        }
    }
}
