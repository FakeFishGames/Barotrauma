using Microsoft.Xna.Framework;
using Lidgren.Network;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public BannedPlayer(string name, UInt16 uniqueIdentifier, bool isRangeBan, string ip, ulong steamID)
        {
            this.Name = name;
            this.SteamID = steamID;
            this.IsRangeBan = isRangeBan;
            this.IP = ip;
            this.UniqueIdentifier = uniqueIdentifier;
        }
    }

    partial class BanList
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
                if (localRemovedBans.Contains(bannedPlayer.UniqueIdentifier)) continue;

                var playerFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), ((GUIListBox)banFrame).Content.RectTransform) { MinSize = new Point(0, 70) }, style: null)
                {
                    UserData = banFrame
                };

                var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), playerFrame.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                string ip = bannedPlayer.IP;
                if (localRangeBans.Contains(bannedPlayer.UniqueIdentifier)) ip = ToRange(ip);
                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), paddedPlayerFrame.RectTransform),
                    bannedPlayer.Name + " (" + ip + ")");

                var removeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.4f), paddedPlayerFrame.RectTransform, Anchor.TopRight), TextManager.Get("BanListRemove"))
                {
                    UserData = bannedPlayer,
                    IgnoreLayoutGroups = true,
                    OnClicked = RemoveBan
                };
                if (bannedPlayer.IP.IndexOf(".x") <= -1)
                {
                    var rangeBanButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.4f), paddedPlayerFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.22f, 0.0f) }, TextManager.Get("BanRange"))
                    {
                        UserData = bannedPlayer,
                        IgnoreLayoutGroups = true,
                        OnClicked = RangeBan
                    };
                }

                new GUITextBlock(new RectTransform(new Vector2(0.6f, 0.0f), paddedPlayerFrame.RectTransform),
                    bannedPlayer.ExpirationTime == null ? 
                        TextManager.Get("BanPermanent") :  TextManager.GetWithVariable("BanExpires", "[time]", bannedPlayer.ExpirationTime.Value.ToString()),
                    font: GUI.SmallFont);

                var reasonText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 0.0f), paddedPlayerFrame.RectTransform),
                    TextManager.Get("BanReason") + 
                        (string.IsNullOrEmpty(bannedPlayer.Reason) ? TextManager.Get("None") : ToolBox.LimitString(bannedPlayer.Reason, GUI.SmallFont, 170)),
                    font: GUI.SmallFont, wrap: true)
                {
                    ToolTip = bannedPlayer.Reason
                };


            }

            return banFrame;
        }

        private bool RemoveBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            localRemovedBans.Add(banned.UniqueIdentifier);

            RecreateBanFrame();

            return true;
        }

        private bool RangeBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            localRangeBans.Add(banned.UniqueIdentifier);

            RecreateBanFrame();

            return true;
        }

        private bool CloseFrame(GUIButton button, object obj)
        {
            banFrame = null;

            return true;
        }

        public void ClientAdminRead(NetBuffer incMsg)
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
            Int32 bannedPlayerCount = incMsg.ReadVariableInt32();
            for (int i = 0; i < bannedPlayerCount; i++)
            {
                string name = incMsg.ReadString();
                UInt16 uniqueIdentifier = incMsg.ReadUInt16();
                bool isRangeBan = incMsg.ReadBoolean(); incMsg.ReadPadBits();
                
                string ip = "";
                UInt64 steamID = 0;
                if (isOwner)
                {
                    ip = incMsg.ReadString();
                    steamID = incMsg.ReadUInt64();
                }
                else
                {
                    ip = "IP concealed by host";
                    steamID = 0;
                }
                bannedPlayers.Add(new BannedPlayer(name, uniqueIdentifier, isRangeBan, ip, steamID));
            }

            if (banFrame != null)
            {
                var parent = banFrame.Parent;
                parent.RemoveChild(banFrame);
                CreateBanFrame(parent);
            }
        }

        public void ClientAdminWrite(NetBuffer outMsg)
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
