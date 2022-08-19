using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        public BannedPlayer(
            UInt32 uniqueIdentifier,
            string name,
            Either<Address, AccountId> addressOrAccountId,
            string reason,
            DateTime? expiration)
        {
            this.Name = name;
            this.AddressOrAccountId = addressOrAccountId;
            this.UniqueIdentifier = uniqueIdentifier;
            this.Reason = reason;
            this.ExpirationTime = expiration;
        }
    }

    partial class BanList
    {
        private GUIComponent banFrame;

        public GUIComponent BanFrame
        {
            get { return banFrame; }
        }

        public List<UInt32> localRemovedBans = new List<UInt32>();

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

                var addressOrAccountId = bannedPlayer.AddressOrAccountId;
                GUITextBlock textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(0.5f, 1.0f), topArea.RectTransform),
                    bannedPlayer.Name + " (" + addressOrAccountId + ")") { CanBeFocused = true };
                textBlock.RectTransform.MinSize = new Point(
                    (int)textBlock.Font.MeasureString(textBlock.Text.SanitizedValue).X, 0);

                var removeButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.4f), topArea.RectTransform), 
                    TextManager.Get("BanListRemove"), style: "GUIButtonSmall")
                {
                    UserData = bannedPlayer,
                    OnClicked = RemoveBan
                };
                topArea.RectTransform.MinSize = new Point(0, (int)(removeButton.Rect.Height * 1.25f));
                
                topArea.ForceLayoutRecalculation();

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedPlayerFrame.RectTransform),
                    bannedPlayer.ExpirationTime == null ? 
                        TextManager.Get("BanPermanent") :  TextManager.GetWithVariable("BanExpires", "[time]", bannedPlayer.ExpirationTime.Value.ToString()),
                    font: GUIStyle.SmallFont);

                var reasonText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedPlayerFrame.RectTransform),
                    TextManager.Get("BanReason") + " " +
                        (string.IsNullOrEmpty(bannedPlayer.Reason) ? TextManager.Get("None") : bannedPlayer.Reason),
                    font: GUIStyle.SmallFont, wrap: true)
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
                UInt32 uniqueIdentifier = incMsg.ReadUInt32();
                bool includesExpiration = incMsg.ReadBoolean();
                incMsg.ReadPadBits();

                DateTime? expiration = null;
                if (includesExpiration)
                {
                    double hoursFromNow = incMsg.ReadDouble();
                    expiration = DateTime.Now + TimeSpan.FromHours(hoursFromNow);
                }

                string reason = incMsg.ReadString();

                Either<Address, AccountId> addressOrAccountId;
                if (isOwner)
                {
                    bool isAddress = incMsg.ReadBoolean();
                    incMsg.ReadPadBits();
                    string str = incMsg.ReadString();
                    if (isAddress && Address.Parse(str).TryUnwrap(out var address))
                    {
                        addressOrAccountId = address;
                    }
                    else if (AccountId.Parse(str).TryUnwrap(out var accountId))
                    {
                        addressOrAccountId = accountId;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    addressOrAccountId = new UnknownAddress();
                }
                bannedPlayers.Add(new BannedPlayer(uniqueIdentifier, name, addressOrAccountId, reason, expiration));
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
            outMsg.WriteVariableUInt32((UInt32)localRemovedBans.Count);
            foreach (UInt32 uniqueId in localRemovedBans)
            {
                outMsg.Write(uniqueId);
            }

            localRemovedBans.Clear();
        }
    }
}
