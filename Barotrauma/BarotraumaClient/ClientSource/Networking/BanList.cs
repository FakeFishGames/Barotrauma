using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public BannedPlayer(string name, UInt16 uniqueIdentifier, bool isRangeBan, string endPoint, ulong steamID, string reason, DateTime? expiration)
        {
            this.Name = name;
            this.EndPoint = endPoint;
            this.SteamID = steamID;
            ParseEndPointAsSteamId();
            this.IsRangeBan = isRangeBan;
            this.UniqueIdentifier = uniqueIdentifier;
            this.Reason = reason;
            this.ExpirationTime = expiration;
        }
    }

    public partial class BanList
    {
        private GUIComponent banFrame;

        public GUIComponent BanFrame
        {
            get { return banFrame; }
        }

        public List<UInt16> localRemovedBans = new List<UInt16>();
        public List<UInt16> localRangeBans = new List<UInt16>();

        private void RecreateBanFrame()
        {
            if (banFrame != null)
            {
                var parent = banFrame.Parent;
                parent.RemoveChild(banFrame);
                CreateBanFrame(parent);
            }
        }

        public GUIComponent CreateBanFrame(GUIComponent parent)
        {
            banFrame = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center));

            foreach (BannedPlayer bannedPlayer in bannedPlayers)
            {
                if (localRemovedBans.Contains(bannedPlayer.UniqueIdentifier)) { continue; }

                var playerFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), ((GUIListBox)banFrame).Content.RectTransform) { MinSize = new Point(0, 70) })
                {
                    UserData = banFrame
                };

                var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), playerFrame.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f,
                    CanBeFocused = true
                };

                var topArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), paddedPlayerFrame.RectTransform), 
                    isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };

                string endPoint = bannedPlayer.EndPoint;
                if (localRangeBans.Contains(bannedPlayer.UniqueIdentifier)) endPoint = ToRange(endPoint);
                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), topArea.RectTransform),
                    bannedPlayer.Name + " (" + endPoint + ")");
                textBlock.RectTransform.MinSize = new Point(textBlock.Rect.Width, 0);

                if (bannedPlayer.EndPoint.IndexOf(".x") <= -1)
                {
                    var rangeBanButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.4f), topArea.RectTransform), 
                        TextManager.Get("BanRange"), style: "GUIButtonSmall")
                    {
                        UserData = bannedPlayer,
                        OnClicked = RangeBan
                    };
                }
                var removeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.4f), topArea.RectTransform), 
                    TextManager.Get("BanListRemove"), style: "GUIButtonSmall")
                {
                    UserData = bannedPlayer,
                    OnClicked = RemoveBan
                };
                topArea.RectTransform.MinSize = new Point(0, (int)topArea.RectTransform.Children.Max(c => c.Rect.Height * 1.25f));

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedPlayerFrame.RectTransform),
                    bannedPlayer.ExpirationTime == null ? 
                        TextManager.Get("BanPermanent") :  TextManager.GetWithVariable("BanExpires", "[time]", bannedPlayer.ExpirationTime.Value.ToString()),
                    font: GUI.SmallFont);

                var reasonText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedPlayerFrame.RectTransform),
                    TextManager.Get("BanReason") + " " +
                        (string.IsNullOrEmpty(bannedPlayer.Reason) ? TextManager.Get("None") : bannedPlayer.Reason),
                    font: GUI.SmallFont, wrap: true)
                {
                    ToolTip = bannedPlayer.Reason
                };

                paddedPlayerFrame.Recalculate();

                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), ((GUIListBox)banFrame).Content.RectTransform), style: "HorizontalLine");
            }

            return banFrame;
        }

        private bool RemoveBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) { return false; }

            localRemovedBans.Add(banned.UniqueIdentifier);
            RecreateBanFrame();

            GameMain.Client?.ServerSettings?.ClientAdminWrite(ServerSettings.NetFlags.Properties);

            return true;
        }

        private bool RangeBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) { return false; }

            localRangeBans.Add(banned.UniqueIdentifier);
            RecreateBanFrame();

            GameMain.Client?.ServerSettings?.ClientAdminWrite(ServerSettings.NetFlags.Properties);

            return true;
        }
        
        public void ClientAdminRead(IReadMessage incMsg)
        {
            bool hasPermission = incMsg.ReadBoolean();
            if (!hasPermission)
            {
                incMsg.ReadPadBits();
                return;
            }

            bool isOwner = incMsg.ReadBoolean();
            incMsg.ReadPadBits();

            bannedPlayers.Clear();
            UInt32 bannedPlayerCount = incMsg.ReadVariableUInt32();

            for (int i = 0; i < (int)bannedPlayerCount; i++)
            {
                string name = incMsg.ReadString();
                UInt16 uniqueIdentifier = incMsg.ReadUInt16();
                bool isRangeBan = incMsg.ReadBoolean();
                bool includesExpiration = incMsg.ReadBoolean();
                incMsg.ReadPadBits();

                DateTime? expiration = null;
                if (includesExpiration)
                {
                    double hoursFromNow = incMsg.ReadDouble();
                    expiration = DateTime.Now + TimeSpan.FromHours(hoursFromNow);
                }

                string reason = incMsg.ReadString();

                string endPoint = "";
                UInt64 steamID = 0;
                if (isOwner)
                {
                    endPoint = incMsg.ReadString();
                    steamID = incMsg.ReadUInt64();
                }
                else
                {
                    endPoint = "Endpoint concealed by host";
                    steamID = 0;
                }
                bannedPlayers.Add(new BannedPlayer(name, uniqueIdentifier, isRangeBan, endPoint, steamID, reason, expiration));
            }

            if (banFrame != null)
            {
                var parent = banFrame.Parent;
                parent.RemoveChild(banFrame);
                CreateBanFrame(parent);
            }
        }

        public void ClientAdminWrite(IWriteMessage outMsg)
        {
            outMsg.Write((UInt16)localRemovedBans.Count);
            foreach (UInt16 uniqueId in localRemovedBans)
            {
                outMsg.Write(uniqueId);
            }

            outMsg.Write((UInt16)localRangeBans.Count);
            foreach (UInt16 uniqueId in localRangeBans)
            {
                outMsg.Write(uniqueId);
            }

            localRemovedBans.Clear();
            localRangeBans.Clear();
        }
    }
}
