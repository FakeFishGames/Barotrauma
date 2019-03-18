using Microsoft.Xna.Framework;

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
            banFrame = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center));

            foreach (BannedPlayer bannedPlayer in bannedPlayers)
            {
                var playerFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), ((GUIListBox)banFrame).Content.RectTransform) { MinSize = new Point(0, 70) }, style: null)
                {
                    UserData = banFrame
                };

                var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), playerFrame.RectTransform, Anchor.Center))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), paddedPlayerFrame.RectTransform),
                    bannedPlayer.IP + " (" + bannedPlayer.Name + ")");

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
                        TextManager.Get("BanPermanent") :  TextManager.Get("BanExpires").Replace("[time]", bannedPlayer.ExpirationTime.Value.ToString()),
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

            RemoveBan(banned);

            if (banFrame != null)
            {
                var parent = banFrame.Parent;
                parent.RemoveChild(banFrame);
                CreateBanFrame(parent);
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
                var parent = banFrame.Parent;
                parent.RemoveChild(banFrame);
                CreateBanFrame(parent);
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
