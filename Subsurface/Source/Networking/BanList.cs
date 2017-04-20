using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class BannedPlayer
    {
        public string Name;
        public string IP;

        public bool CompareTo(string ipCompare)
        {
            int rangeBanIndex = IP.IndexOf(".x");
            if (rangeBanIndex<=-1)
            {
                return ipCompare == IP;
            }
            else
            {
                if (ipCompare.Length < rangeBanIndex) return false;
                return ipCompare.Substring(0, rangeBanIndex) == IP.Substring(0, rangeBanIndex);
            }
        }

        public BannedPlayer(string name, string ip)
        {
            this.Name = name;
            this.IP = ip;
        }
    }

    class BanList
    {
        const string SavePath = "Data/bannedplayers.txt";

        private List<BannedPlayer> bannedPlayers;

        private GUIComponent banFrame;

        public GUIComponent BanFrame
        {
            get { return banFrame; }
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

                    string name = String.Join(",", separatedLine.Take(separatedLine.Length - 1));
                    string ip = separatedLine.Last();

                    bannedPlayers.Add(new BannedPlayer(name, ip));
                }
            }
        }

        public void BanPlayer(string name, string ip)
        {
            if (bannedPlayers.Any(bp => bp.IP == ip)) return;

            DebugConsole.Log("Banned " + name);

            bannedPlayers.Add(new BannedPlayer(name, ip));
            Save();
        }

        public bool IsBanned(string IP)
        {
            return bannedPlayers.Any(bp => bp.CompareTo(IP));
        }

        public GUIComponent CreateBanFrame(GUIComponent parent)
        {
            banFrame = new GUIListBox(new Rectangle(0, 0, 0, 0), "", parent);

            foreach (BannedPlayer bannedPlayer in bannedPlayers)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    bannedPlayer.IP + " (" + bannedPlayer.Name + ")",
                    "",
                    Alignment.Left, Alignment.Left, banFrame);
                textBlock.Padding = new Vector4(10.0f, 10.0f, 0.0f, 0.0f);
                textBlock.UserData = banFrame;

                var removeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Remove", Alignment.Right | Alignment.CenterY, "", textBlock);
                removeButton.UserData = bannedPlayer;
                removeButton.OnClicked = RemoveBan;
                if (bannedPlayer.IP.IndexOf(".x") <= -1)
                {
                    var rangeBanButton = new GUIButton(new Rectangle(-100, 0, 100, 20), "Ban range", Alignment.Right | Alignment.CenterY, "", textBlock);
                    rangeBanButton.UserData = bannedPlayer;
                    rangeBanButton.OnClicked = RangeBan;
                }
            }

            return banFrame;
        }

        private bool RemoveBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            DebugConsole.Log("Removing ban from " + banned.Name);
            GameServer.Log("Removing ban from " + banned.Name, null);

            bannedPlayers.Remove(banned);

            Save();

            if (banFrame != null)
            {
                banFrame.Parent.RemoveChild(banFrame);
                CreateBanFrame(banFrame.Parent);
            }

            return true;
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

        private bool RangeBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            banned.IP = ToRange(banned.IP);

            BannedPlayer bp;
            while ((bp = bannedPlayers.Find(x => banned.CompareTo(x.IP)))!=null)
            {
                //remove all specific bans that are now covered by the rangeban
                bannedPlayers.Remove(bp);
            }

            bannedPlayers.Add(banned);

            Save();

            if (banFrame != null)
            {
                banFrame.Parent.RemoveChild(banFrame);
                CreateBanFrame(banFrame.Parent);
            }

            return true;
        }

        private bool CloseFrame(GUIButton button, object obj)
        {
            banFrame = null;

            return true;
        }

        public void Save()
        {
            GameServer.Log("Saving banlist", null);

            List<string> lines = new List<string>();

            foreach (BannedPlayer banned in bannedPlayers)
            {
                lines.Add(banned.Name + "," + banned.IP);
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
