using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    partial class BanList
    {
        private GUIComponent banFrame;

        public GUIComponent BanFrame
        {
            get { return banFrame; }
        }

        public GUIComponent CreateBanFrame(GUIComponent parent)
        {
            banFrame = new GUIListBox(new Rectangle(0, 0, 0, 0), "", parent);

            foreach (BannedPlayer bannedPlayer in bannedPlayers)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 55),
                    bannedPlayer.IP + " (" + bannedPlayer.Name + ")",
                    "",
                    Alignment.Left, Alignment.TopLeft, banFrame);
                textBlock.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
                textBlock.UserData = banFrame;

                var removeButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Remove", Alignment.TopRight, "", textBlock);
                removeButton.UserData = bannedPlayer;
                removeButton.OnClicked = RemoveBan;
                if (bannedPlayer.IP.IndexOf(".x") <= -1)
                {
                    var rangeBanButton = new GUIButton(new Rectangle(-85, 0, 90, 20), "Ban range", Alignment.TopRight, "", textBlock);
                    rangeBanButton.UserData = bannedPlayer;
                    rangeBanButton.OnClicked = RangeBan;
                }

                var reasonText = new GUITextBlock(new Rectangle(0, 0, 170, 20), 
                    string.IsNullOrEmpty(bannedPlayer.Reason) ? "Reason: none" : ToolBox.LimitString("Reason: " + bannedPlayer.Reason, GUI.SmallFont, 170),
                    "", Alignment.BottomLeft, Alignment.TopLeft, textBlock, false, GUI.SmallFont);
                reasonText.ToolTip = bannedPlayer.Reason;

                new GUITextBlock(new Rectangle(0, 0, 100, 20),
                    bannedPlayer.ExpirationTime == null ? "Permanent" : "Expires " + bannedPlayer.ExpirationTime.Value.ToString(),
                    "", Alignment.BottomRight, Alignment.TopRight, textBlock, false, GUI.SmallFont);

            }

            return banFrame;
        }

        private bool RemoveBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            RemoveBan(banned);

            if (banFrame != null)
            {
                banFrame.Parent.RemoveChild(banFrame);
                CreateBanFrame(banFrame.Parent);
            }

            return true;
        }

        private bool RangeBan(GUIButton button, object obj)
        {
            BannedPlayer banned = obj as BannedPlayer;
            if (banned == null) return false;

            RangeBan(banned);

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
    }
}
