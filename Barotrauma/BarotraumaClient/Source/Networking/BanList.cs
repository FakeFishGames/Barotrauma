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
