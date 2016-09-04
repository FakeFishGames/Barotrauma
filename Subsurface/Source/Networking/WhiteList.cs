using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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

    class WhiteList
    {
        const string SavePath = "Data/whitelist.txt";

        private List<WhiteListedPlayer> whitelistedPlayers;
        public List<WhiteListedPlayer> WhiteListedPlayers
        {
            get { return whitelistedPlayers; }
        }

        private GUIComponent whitelistFrame;
        private GUIComponent innerlistFrame;

        private GUITextBox nameBox;
        private GUITextBox ipBox;

        public bool enabled;

        public GUIComponent WhiteListFrame
        {
            get { return whitelistFrame; }
        }

        public WhiteList()
        {
            enabled = false;
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
                            enabled = true;
                        }
                        else
                        {
                            enabled = false;
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
            GameServer.Log("Saving whitelist", null);

            List<string> lines = new List<string>();

            if (enabled)
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
            if (!enabled) return true;
            WhiteListedPlayer wlp = whitelistedPlayers.Find(p => p.Name == name);
            if (wlp == null) return false;
            if (wlp.IP != ip && !string.IsNullOrWhiteSpace(wlp.IP)) return false;
            return true;
        }

        public GUIComponent CreateWhiteListFrame()
        {
            whitelistFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f);

            GUIFrame innerFrame = new GUIFrame(new Rectangle(0, 0, 500, 430), null, Alignment.Center, GUI.Style, whitelistFrame);
            innerFrame.Padding = new Vector4(20.0f, 50.0f, 20.0f, 100.0f);

            var closeButton = new GUIButton(new Rectangle(0, 85, 100, 20), "Close", Alignment.BottomRight, GUI.Style, innerFrame);
            closeButton.OnClicked = GameMain.Server.ToggleWhiteListFrame;

            new GUITextBlock(new Rectangle(0, -35, 200, 20), "Whitelist", GUI.Style, innerFrame, GUI.LargeFont);
            var enabledTick = new GUITickBox(new Rectangle(200, -30, 20, 20), "Enabled", Alignment.Left, innerFrame);
            enabledTick.Selected = enabled;
            enabledTick.OnSelected = (GUITickBox box) =>
            {
                enabled = !enabled;
                if (enabled)
                {
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        if (!IsWhiteListed(c.name,c.Connection.RemoteEndPoint.Address.ToString()))
                        {
                            whitelistedPlayers.Add(new WhiteListedPlayer(c.name, c.Connection.RemoteEndPoint.Address.ToString()));
                            CloseFrame(); CreateWhiteListFrame();
                        }
                    }
                }
                Save();
                return true;
            };

            new GUITextBlock(new Rectangle(0, 35, 90, 25), "Name:", GUI.Style, Alignment.BottomLeft, Alignment.TopLeft, innerFrame, false, GUI.Font);
            nameBox = new GUITextBox(new Rectangle(100, 30, 170, 25), Alignment.BottomLeft, GUI.Style, innerFrame);
            nameBox.Font = GUI.Font;

            new GUITextBlock(new Rectangle(0, 65, 90, 25), "IP Address:", GUI.Style, Alignment.BottomLeft, Alignment.TopLeft, innerFrame, false, GUI.Font);
            ipBox = new GUITextBox(new Rectangle(100, 60, 170, 25), Alignment.BottomLeft, GUI.Style, innerFrame);
            ipBox.Font = GUI.Font;

            var addnewButton = new GUIButton(new Rectangle(300, 45, 150, 20), "Add to whitelist", Alignment.BottomLeft, GUI.Style, innerFrame);
            addnewButton.OnClicked = AddToWhiteList;

            innerlistFrame = new GUIListBox(new Rectangle(0, 0, 0, 0), GUI.Style, innerFrame);

            foreach (WhiteListedPlayer wlp in whitelistedPlayers)
            {
                string blockText = wlp.Name;
                if (!string.IsNullOrWhiteSpace(wlp.IP)) blockText += " (" + wlp.IP + ")";
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    blockText,
                    GUI.Style,
                    Alignment.Left, Alignment.Left, innerlistFrame);
                textBlock.Padding = new Vector4(10.0f, 10.0f, 0.0f, 0.0f);
                textBlock.UserData = wlp;

                var removeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Remove", Alignment.Right | Alignment.CenterY, GUI.Style, textBlock);
                removeButton.UserData = wlp;
                removeButton.OnClicked = RemoveFromWhiteList;
            }
            
            return whitelistFrame;
        }

        private bool RemoveFromWhiteList(GUIButton button, object obj)
        {
            WhiteListedPlayer wlp = obj as WhiteListedPlayer;
            if (wlp == null) return false;

            DebugConsole.Log("Removing " + wlp.Name + " from whitelist");
            GameServer.Log("Removing " + wlp.Name + " from whitelist", null);

            whitelistedPlayers.Remove(wlp);
            Save();
            CloseFrame(); CreateWhiteListFrame();

            return true;
        }

        private bool AddToWhiteList(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return false;
            if (whitelistedPlayers.Find(x => x.Name.ToLower() == nameBox.Text.ToLower() && x.IP == ipBox.Text) != null) return false;
            whitelistedPlayers.Add(new WhiteListedPlayer(nameBox.Text,ipBox.Text));
            Save();
            CloseFrame(); CreateWhiteListFrame();
            return true;
        }

        public bool CloseFrame(GUIButton button=null, object obj=null)
        {
            whitelistFrame = null;

            return true;
        }
    }
}
