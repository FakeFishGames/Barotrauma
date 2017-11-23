using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class WhiteList
    {
        private GUIComponent whitelistFrame;

        private GUITextBox nameBox;
        private GUITextBox ipBox;

        public GUIComponent CreateWhiteListFrame(GUIComponent parent)
        {
            if (whitelistFrame != null)
            {
                whitelistFrame.Parent.ClearChildren();
                whitelistFrame = null;
            }

            parent.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            var enabledTick = new GUITickBox(new Rectangle(0, 0, 20, 20), "Enabled", Alignment.TopLeft, parent);
            enabledTick.Selected = Enabled;
            enabledTick.OnSelected = (GUITickBox box) =>
            {
                Enabled = !Enabled;

                if (Enabled)
                {
                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        if (!IsWhiteListed(c.Name, c.Connection.RemoteEndPoint.Address.ToString()))
                        {
                            whitelistedPlayers.Add(new WhiteListedPlayer(c.Name, c.Connection.RemoteEndPoint.Address.ToString()));
                            if (whitelistFrame != null) CreateWhiteListFrame(whitelistFrame.Parent);
                        }
                    }
                }

                Save();
                return true;
            };

            new GUITextBlock(new Rectangle(0, -35, 90, 20), "Name:", "", Alignment.BottomLeft, Alignment.CenterLeft, parent, false, GUI.Font);
            nameBox = new GUITextBox(new Rectangle(100, -35, 170, 20), Alignment.BottomLeft, "", parent);
            nameBox.Font = GUI.Font;

            new GUITextBlock(new Rectangle(0, 0, 90, 20), "IP Address:", "", Alignment.BottomLeft, Alignment.CenterLeft, parent, false, GUI.Font);
            ipBox = new GUITextBox(new Rectangle(100, 0, 170, 20), Alignment.BottomLeft, "", parent);
            ipBox.Font = GUI.Font;

            var addnewButton = new GUIButton(new Rectangle(0, 35, 150, 20), "Add to whitelist", Alignment.BottomLeft, "", parent);
            addnewButton.OnClicked = AddToWhiteList;

            whitelistFrame = new GUIListBox(new Rectangle(0, 30, 0, parent.Rect.Height - 110), "", parent);

            foreach (WhiteListedPlayer wlp in whitelistedPlayers)
            {
                string blockText = wlp.Name;
                if (!string.IsNullOrWhiteSpace(wlp.IP)) blockText += " (" + wlp.IP + ")";
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    blockText,
                    "",
                    Alignment.Left, Alignment.Left, whitelistFrame);
                textBlock.Padding = new Vector4(10.0f, 10.0f, 0.0f, 0.0f);
                textBlock.UserData = wlp;

                var removeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Remove", Alignment.Right | Alignment.CenterY, "", textBlock);
                removeButton.UserData = wlp;
                removeButton.OnClicked = RemoveFromWhiteList;
            }

            return parent;
        }

        private bool RemoveFromWhiteList(GUIButton button, object obj)
        {
            WhiteListedPlayer wlp = obj as WhiteListedPlayer;
            if (wlp == null) return false;

            RemoveFromWhiteList(wlp);

            if (whitelistFrame != null)
            {
                whitelistFrame.Parent.ClearChildren();
                CreateWhiteListFrame(whitelistFrame.Parent);
            }

            return true;
        }

        private bool AddToWhiteList(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return false;
            if (whitelistedPlayers.Any(x => x.Name.ToLower() == nameBox.Text.ToLower() && x.IP == ipBox.Text)) return false;

            AddToWhiteList(nameBox.Text, ipBox.Text);

            if (whitelistFrame != null)
            {
                CreateWhiteListFrame(whitelistFrame.Parent);
            }
            return true;
        }
    }
}
