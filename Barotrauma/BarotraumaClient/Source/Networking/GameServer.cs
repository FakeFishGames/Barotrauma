using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        private NetStats netStats;

        private GUIScrollBar clientListScrollBar;

        void InitProjSpecific()
        {
            var buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, inGameHUD.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                CanBeFocused = false
            };

            var endRoundButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                OnClicked = (btn, userdata) => { EndGame(); return true; }
            };

            showLogButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (ServerLog.LogFrame == null)
                    {
                        ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };

            GUIButton settingsButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("ServerSettingsButton"))
            {
                OnClicked = ToggleSettingsFrame,
                UserData = "settingsButton"
            };
        }

        public override void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            base.AddToGUIUpdateList();
            settingsFrame?.AddToGUIUpdateList();
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            if (!ShowNetStats) return;

            GUI.Font.DrawString(spriteBatch, "Unique Events: " + entityEventManager.UniqueEvents.Count, new Vector2(10, 50), Color.White);

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);

            if (clientListScrollBar == null)
            {
                clientListScrollBar = new GUIScrollBar(new RectTransform(new Point(10, height), GUI.Canvas) { AbsoluteOffset = new Point(x + width - 10, y) });
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

            clientListScrollBar.UpdateManually(1.0f / 60.0f);
            clientListScrollBar.DrawManually(spriteBatch);

            netStats.AddValue(NetStats.NetStatType.ResentMessages, Math.Max(resentMessages, 0));
            netStats.AddValue(NetStats.NetStatType.SentBytes, server.Statistics.SentBytes);
            netStats.AddValue(NetStats.NetStatType.ReceivedBytes, server.Statistics.ReceivedBytes);

            netStats.Draw(spriteBatch, new Rectangle(200, 0, 800, 200), this);
        }


        private void UpdateFileTransferIndicator(Client client)
        {
            var transfers = fileSender.ActiveTransfers.FindAll(t => t.Connection == client.Connection);

            var clientNameBox = GameMain.NetLobbyScreen.PlayerList.Content.FindChild(client.Name);

            var clientInfo = clientNameBox.FindChild("filetransfer");
            if (clientInfo == null)
            {
                clientInfo = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.9f), clientNameBox.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.1f, 0.0f) }, style: null)
                {
                    UserData = "filetransfer"
                };
                var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), clientInfo.RectTransform),
                    "", textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                {
                    TextScale = 0.8f,
                    TextGetter = () =>
                    {
                        string txt = "";
                        if (transfers.Count > 0)
                        {
                            txt += transfers[0].FileName + " ";
                            if (transfers.Count > 1) txt += "+ " + (transfers.Count - 1) + " others ";
                        }
                        txt += "(" + MathUtils.GetBytesReadable(transfers.Sum(t => t.SentOffset)) + " / " + MathUtils.GetBytesReadable(transfers.Sum(t => t.Data.Length)) + ")";
                        return txt;
                    }
                };

                var progressBar = new GUIProgressBar(new RectTransform(new Vector2(0.8f, 0.5f), clientInfo.RectTransform, Anchor.BottomLeft),
                    barSize: 0.0f, color: Color.Green)
                {
                    IsHorizontal = true,
                    ProgressGetter = () => { return transfers.Sum(t => t.Progress) / transfers.Count; }
                };

                var cancelButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), clientInfo.RectTransform, Anchor.BottomRight), "X")
                {
                    OnClicked = (GUIButton button, object userdata) =>
                    {
                        transfers.ForEach(t => fileSender.CancelTransfer(t));
                        return true;
                    }
                };
            }
            else if (transfers.Count == 0)
            {
                clientInfo.Parent.RemoveChild(clientInfo);
            }
        }

        public override bool SelectCrewCharacter(Character character, GUIComponent characterFrame)
        {
            if (character == null) return false;
            
            if (character != myCharacter)
            {
                var banButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomRight),
                    TextManager.Get("Ban"))
                {
                    UserData = character.Name,
                    OnClicked = GameMain.NetLobbyScreen.BanPlayer
                };
                var rangebanButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.0f, 0.16f) },
                    TextManager.Get("BanRange"))
                {
                    UserData = character.Name,
                    OnClicked = GameMain.NetLobbyScreen.BanPlayerRange
                };
                var kickButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomLeft),
                    TextManager.Get("Kick"))
                {
                    UserData = character.Name,
                    OnClicked = GameMain.NetLobbyScreen.KickPlayer
                };
            }

            return true;
        }

        private GUIMessageBox upnpBox;
        void InitUPnP()
        {
            server.UPnP.ForwardPort(NetPeerConfiguration.Port, "barotrauma");
            if (Steam.SteamManager.USE_STEAM)
            {
                server.UPnP.ForwardPort(QueryPort, "barotrauma");
            }

            upnpBox = new GUIMessageBox(TextManager.Get("PleaseWaitUPnP"), TextManager.Get("AttemptingUPnP"), new string[] { TextManager.Get("Cancel") });
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
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("UPnPUnavailable"));
            }
            else if (server.UPnP.Status == UPnPStatus.Discovering)
            {
                new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("UPnPTimedOut"));
            }
        }

        public bool StartGameClicked(GUIButton button, object obj)
        {
            return StartGame();
        }
    }
}
