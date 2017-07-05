using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    class WhiteListedPlayer
    {
        public string Name;
        public string IP;

        public WhiteListedPlayer(string name,string ip)
        {
            Name = name;
            IP = ip;
        }
    }

    partial class WhiteList
    {
        const string SavePath = "Data/whitelist.txt";

        private List<WhiteListedPlayer> whitelistedPlayers;
        public List<WhiteListedPlayer> WhiteListedPlayers
        {
            get { return whitelistedPlayers; }
        }

        public bool Enabled;

        public WhiteList()
        {
            Enabled = false;
            whitelistedPlayers = new List<WhiteListedPlayer>();

            if (File.Exists(SavePath))
            {
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to open whitelist in " + SavePath, e);
                    return;
                }

                foreach (string line in lines)
                {
                    if (line[0] == '#')
                    {
                        string lineval = line.Substring(1, line.Length - 1);
                        int intVal = 0;
                        Int32.TryParse(lineval, out intVal);
                        if (lineval.ToLower() == "true" || intVal != 0)
                        {
                            Enabled = true;
                        }
                        else
                        {
                            Enabled = false;
                        }
                    }
                    else
                    {
                        string[] separatedLine = line.Split(',');
                        if (separatedLine.Length < 2) continue;

                        string name = String.Join(",", separatedLine.Take(separatedLine.Length - 1));
                        string ip = separatedLine.Last();

                        whitelistedPlayers.Add(new WhiteListedPlayer(name, ip));
                    }
                }
            }
        }

        public void Save()
        {
            GameServer.Log("Saving whitelist", ServerLog.MessageType.ServerMessage);

            List<string> lines = new List<string>();

            if (Enabled)
            {
                lines.Add("#true");
            }
            else
            {
                lines.Add("#false");
            }
            foreach (WhiteListedPlayer wlp in whitelistedPlayers)
            {
                lines.Add(wlp.Name + "," + wlp.IP);
            }

            try
            {
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the whitelist to " + SavePath + " failed", e);
            }
        }

        public bool IsWhiteListed(string name, string ip)
        {
            if (!Enabled) return true;
            WhiteListedPlayer wlp = whitelistedPlayers.Find(p => p.Name == name);
            if (wlp == null) return false;
            if (wlp.IP != ip && !string.IsNullOrWhiteSpace(wlp.IP)) return false;
            return true;
        }

        private void RemoveFromWhiteList(WhiteListedPlayer wlp)
        {
            DebugConsole.Log("Removing " + wlp.Name + " from whitelist");
            GameServer.Log("Removing " + wlp.Name + " from whitelist", ServerLog.MessageType.ServerMessage);

            whitelistedPlayers.Remove(wlp);
            Save();
        }

        private void AddToWhiteList(string name,string ip)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (whitelistedPlayers.Any(x => x.Name.ToLower() == name.ToLower() && x.IP == ip)) return;
            whitelistedPlayers.Add(new WhiteListedPlayer(name, ip));
            Save();
        }
    }
}
