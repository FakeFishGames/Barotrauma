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
        public DateTime? ExpirationTime;

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

        public BannedPlayer(string name, string ip, string reason, DateTime? expirationTime)
        {
            this.Name = name;
            this.IP = ip;
            this.Reason = reason;
            this.ExpirationTime = expirationTime;
        }
    }

    partial class BanList
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

                    DateTime? expirationTime = null;
                    if (separatedLine.Length > 2 && !string.IsNullOrEmpty(separatedLine[2]))
                    {
                        DateTime parsedTime;
                        if (DateTime.TryParse(separatedLine[2], out parsedTime))
                        {
                            expirationTime = parsedTime;
                        }
                    }
                    string reason = separatedLine.Length > 3 ? string.Join(",", separatedLine.Skip(3)) : "";

                    if (expirationTime.HasValue && DateTime.Now > expirationTime.Value) continue;

                    bannedPlayers.Add(new BannedPlayer(name, ip, reason, expirationTime));
                }
            }
        }

        public void BanPlayer(string name, string ip, string reason, TimeSpan? duration)
        {
            if (bannedPlayers.Any(bp => bp.IP == ip)) return;

            System.Diagnostics.Debug.Assert(!name.Contains(','));

            string logMsg = "Banned " + name;
            if (!string.IsNullOrEmpty(reason)) logMsg += ", reason: " + reason;
            if (duration.HasValue) logMsg += ", duration: " + duration.Value.ToString();

            DebugConsole.Log(logMsg);

            DateTime? expirationTime = null;
            if (duration.HasValue)
            {
                expirationTime = DateTime.Now + duration.Value;
            }

            bannedPlayers.Add(new BannedPlayer(name, ip, reason, expirationTime));
            Save();
        }

        public void UnbanPlayer(string name)
        {
            var player = bannedPlayers.Find(bp => bp.Name == name);
            if (player == null)
            {
                DebugConsole.Log("Could not unban player \""+name+"\". Matching player not found.");
            }
            else
            {
                DebugConsole.Log("Unbanned \"" + name + ".");
                bannedPlayers.Remove(player);
                Save();
            }
        }

        public void UnbanIP(string ip)
        {
            var player = bannedPlayers.Find(bp => bp.IP == ip);
            if (player == null)
            {
                DebugConsole.Log("Could not unban IP \"" + ip + "\". Matching player not found.");
            }
            else
            {
                DebugConsole.Log("Unbanned \"" + ip + ".");
                bannedPlayers.Remove(player);
                Save();
            }
        }

        public bool IsBanned(string IP)
        {
            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);
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

            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);

            List<string> lines = new List<string>();
            foreach (BannedPlayer banned in bannedPlayers)
            {
                string line = banned.Name + "," + banned.IP;
                line += "," + (banned.ExpirationTime.HasValue ? banned.ExpirationTime.Value.ToString() : "");
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
