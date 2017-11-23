using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        private NetStats netStats;

        private GUIButton showLogButton;

        private GUIScrollBar clientListScrollBar;

        void InitProjSpecific()
        {
            //----------------------------------------

            var endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170, 20, 150, 20), "End round", Alignment.TopLeft, "", inGameHUD);
            endRoundButton.OnClicked = (btn, userdata) => { EndGame(); return true; };

            showLogButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170, 20, 150, 20), "Server Log", Alignment.TopLeft, "", inGameHUD);
            showLogButton.OnClicked = (GUIButton button, object userData) =>
            {
                if (log.LogFrame == null)
                {
                    log.CreateLogFrame();
                }
                else
                {
                    log.LogFrame = null;
                    GUIComponent.KeyboardDispatcher.Subscriber = null;
                }
                return true;
            };

            GUIButton settingsButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170 - 170, 20, 150, 20), "Settings", Alignment.TopLeft, "", inGameHUD);
            settingsButton.OnClicked = ToggleSettingsFrame;
            settingsButton.UserData = "settingsButton";

            //----------------------------------------
        }

        public override void AddToGUIUpdateList()
        {
            if (started) base.AddToGUIUpdateList();

            if (settingsFrame != null) settingsFrame.AddToGUIUpdateList();
            if (log.LogFrame != null) log.LogFrame.AddToGUIUpdateList();
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            if (settingsFrame != null)
            {
                settingsFrame.Draw(spriteBatch);
            }
            else if (log.LogFrame != null)
            {
                log.LogFrame.Draw(spriteBatch);
            }

            if (!ShowNetStats) return;

            GUI.Font.DrawString(spriteBatch, "Unique Events: " + entityEventManager.UniqueEvents.Count, new Vector2(10, 50), Color.White);

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);


            if (clientListScrollBar == null)
            {
                clientListScrollBar = new GUIScrollBar(new Rectangle(x + width - 10, y, 10, height), "", 1.0f);
            }


            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            GUI.Font.DrawString(spriteBatch, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);

            GUI.SmallFont.DrawString(spriteBatch, "Connections: " + server.ConnectionsCount, new Vector2(x + 10, y + 30), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Received bytes: " + MathUtils.GetBytesReadable(server.Statistics.ReceivedBytes), new Vector2(x + 10, y + 45), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Received packets: " + server.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

            GUI.SmallFont.DrawString(spriteBatch, "Sent bytes: " + MathUtils.GetBytesReadable(server.Statistics.SentBytes), new Vector2(x + 10, y + 75), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Sent packets: " + server.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);

            int resentMessages = 0;

            int clientListHeight = connectedClients.Count * 40;
            float scrollBarHeight = (height - 110) / (float)Math.Max(clientListHeight, 110);

            if (clientListScrollBar.BarSize != scrollBarHeight)
            {
                clientListScrollBar.BarSize = scrollBarHeight;
            }

            int startY = y + 110;
            y = (startY - (int)(clientListScrollBar.BarScroll * (clientListHeight - (height - 110))));
            foreach (Client c in connectedClients)
            {
                Color clientColor = c.Connection.AverageRoundtripTime > 0.3f ? Color.Red : Color.White;

                if (y >= startY && y < startY + height - 120)
                {
                    GUI.SmallFont.DrawString(spriteBatch, c.Name + " (" + c.Connection.RemoteEndPoint.Address.ToString() + ")", new Vector2(x + 10, y), clientColor);
                    GUI.SmallFont.DrawString(spriteBatch, "Ping: " + (int)(c.Connection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x + 20, y + 10), clientColor);
                }
                if (y + 25 >= startY && y < startY + height - 130) GUI.SmallFont.DrawString(spriteBatch, "Resent messages: " + c.Connection.Statistics.ResentMessages, new Vector2(x + 20, y + 20), clientColor);

                resentMessages += (int)c.Connection.Statistics.ResentMessages;

                y += 40;
            }

            clientListScrollBar.Update(1.0f / 60.0f);
            clientListScrollBar.Draw(spriteBatch);

            netStats.AddValue(NetStats.NetStatType.ResentMessages, Math.Max(resentMessages, 0));
            netStats.AddValue(NetStats.NetStatType.SentBytes, server.Statistics.SentBytes);
            netStats.AddValue(NetStats.NetStatType.ReceivedBytes, server.Statistics.ReceivedBytes);

            netStats.Draw(spriteBatch, new Rectangle(200, 0, 800, 200), this);
        }


        private void UpdateFileTransferIndicator(Client client)
        {
            var transfers = fileSender.ActiveTransfers.FindAll(t => t.Connection == client.Connection);

            var clientNameBox = GameMain.NetLobbyScreen.PlayerList.FindChild(client.Name);

            var clientInfo = clientNameBox.FindChild("filetransfer");
            if (clientInfo == null)
            {
                clientNameBox.ClearChildren();
                clientInfo = new GUIFrame(new Rectangle(0, 0, 180, 0), Color.Transparent, Alignment.TopRight, null, clientNameBox);
                clientInfo.UserData = "filetransfer";
            }
            else if (transfers.Count == 0)
            {
                clientInfo.Parent.RemoveChild(clientInfo);
            }

            clientInfo.ClearChildren();

            var progressBar = new GUIProgressBar(new Rectangle(0, 4, 160, clientInfo.Rect.Height - 8), Color.Green, "", 0.0f, Alignment.Left, clientInfo);
            progressBar.IsHorizontal = true;
            progressBar.ProgressGetter = () => { return transfers.Sum(t => t.Progress) / transfers.Count; };

            var textBlock = new GUITextBlock(new Rectangle(0, 2, 160, 0), "", "", Alignment.TopLeft, Alignment.Left | Alignment.CenterY, clientInfo, true, GUI.SmallFont);
            textBlock.TextGetter = () =>
            { return MathUtils.GetBytesReadable(transfers.Sum(t => t.SentOffset)) + " / " + MathUtils.GetBytesReadable(transfers.Sum(t => t.Data.Length)); };

            var cancelButton = new GUIButton(new Rectangle(-5, 0, 14, 0), "X", Alignment.Right, "", clientInfo);
            cancelButton.OnClicked = (GUIButton button, object userdata) =>
            {
                transfers.ForEach(t => fileSender.CancelTransfer(t));
                return true;
            };
        }

        public override bool SelectCrewCharacter(Character character, GUIComponent characterFrame)
        {
            if (character == null) return false;
            
            if (character != myCharacter)
            {
                var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomRight, "", characterFrame);
                banButton.UserData = character.Name;
                banButton.OnClicked += GameMain.NetLobbyScreen.BanPlayer;

                var rangebanButton = new GUIButton(new Rectangle(0, -25, 100, 20), "Ban range", Alignment.BottomRight, "", characterFrame);
                rangebanButton.UserData = character.Name;
                rangebanButton.OnClicked += GameMain.NetLobbyScreen.BanPlayerRange;

                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, "", characterFrame);
                kickButton.UserData = character.Name;
                kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;
            }

            return true;
        }

        private GUIMessageBox upnpBox;
        void InitUPnP()
        {
            server.UPnP.ForwardPort(config.Port, "barotrauma");

            upnpBox = new GUIMessageBox("Please wait...", "Attempting UPnP port forwarding", new string[] { "Cancel" });
            upnpBox.Buttons[0].OnClicked = upnpBox.Close;
        }

        bool DiscoveringUPnP()
        {
            return server.UPnP.Status == UPnPStatus.Discovering && GUIMessageBox.VisibleBox == upnpBox;
        }

        void FinishUPnP()
        {
            upnpBox.Close(null, null);

            if (server.UPnP.Status == UPnPStatus.NotAvailable)
            {
                new GUIMessageBox("Error", "UPnP not available");
            }
            else if (server.UPnP.Status == UPnPStatus.Discovering)
            {
                new GUIMessageBox("Error", "UPnP discovery timed out");
            }
        }

        public bool StartGameClicked(GUIButton button, object obj)
        {
            return StartGame();
        }
    }
}
