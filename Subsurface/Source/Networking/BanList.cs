using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
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
                    DebugConsole.ThrowError("Failed to open the list of banned players in "+SavePath, e);
                    return;
                }
                
                foreach (string line in lines)
                {
                    string[] separatedLine = line.Split(',');
                    if (separatedLine.Length != 2) continue;

                    bannedPlayers.Add(new BannedPlayer(separatedLine[0],separatedLine[1]));
                }
            }
        }

        public void BanPlayer(string name, string ip)
        {
            if (bannedPlayers.FirstOrDefault(bp => bp.IP == ip)!=null) return;

            bannedPlayers.Add(new BannedPlayer(name,ip));
        }

        public bool IsBanned(string IP)
        {
            return bannedPlayers.FirstOrDefault(bp => bp.IP == IP)!=null;
        }
        
        public GUIComponent CreateBanFrame(GUIComponent parent)
        {
            //GUIFrame banFrame = new GUIFrame(new Rectangle(0,0,0,0), null, Alignment.Center, GUI.Style, parent);

            //new GUITextBlock(new Rectangle(0, -10, 0, 30), "Banned IPs:", GUI.Style, Alignment.Left, Alignment.Left, banFrame, false, GUI.LargeFont);
            banFrame = new GUIListBox(new Rectangle(0,0,0,0), GUI.Style, parent);

            foreach (BannedPlayer bannedPlayer in bannedPlayers)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    bannedPlayer.IP+" ("+bannedPlayer.Name+")",
                    GUI.Style,
                    Alignment.Left, Alignment.Left, banFrame);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = banFrame;

                var removeButton = new GUIButton(new Rectangle(0,00,100,20), "Remove", Alignment.Right | Alignment.CenterY, GUI.Style, textBlock);
                removeButton.UserData = bannedPlayer;
                removeButton.OnClicked = RemoveBan;
            }

            //var closeButton = new GUIButton(new Rectangle(0,30,100,20), "Close", Alignment.BottomRight, GUI.Style, banFrame);
            //closeButton.OnClicked = CloseFrame;


            return banFrame;
        }

        private bool RemoveBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            bannedPlayers.Remove(banned);


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
                DebugConsole.ThrowError("Saving the list of banned players to "+SavePath+" failed", e);
            }
        }

    }    
    
    class BannedPlayer
    {
        public string Name;
        public string IP;

        public BannedPlayer(string name, string ip)
        {
            this.Name = name;
            this.IP = ip;
        }
    }
}
