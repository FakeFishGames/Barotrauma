#if FALSE
//TODO: fix
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class WhiteList
    {
        private GUIComponent whitelistFrame;

        private GUITextBox nameBox;
        private GUITextBox ipBox;
        private GUIButton addNewButton;
        
        public GUIComponent CreateWhiteListFrame(GUIComponent parent)
        {
            if (whitelistFrame != null)
            {
                whitelistFrame.Parent.ClearChildren();
                whitelistFrame = null;
            }

            whitelistFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), parent.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var enabledTick = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.1f), whitelistFrame.RectTransform), TextManager.Get("WhiteListEnabled"))
            {
                Selected = Enabled,
                UpdateOrder = 1,
                OnSelected = (GUITickBox box) =>
                {
                    Enabled = !Enabled;

                    nameBox.Text = "";
                    nameBox.Enabled = Enabled;
                    ipBox.Text = "";
                    ipBox.Enabled = Enabled;
                    addNewButton.Enabled = false;

                    if (Enabled)
                    {
                        /*TODO: fix
                        foreach (Client c in GameMain.Server.ConnectedClients)
                        {
                            if (!IsWhiteListed(c.Name, c.Connection.RemoteEndPoint.Address.ToString()))
                            {
                                whitelistedPlayers.Add(new WhiteListedPlayer(c.Name, c.Connection.RemoteEndPoint.Address.ToString()));
                                if (whitelistFrame != null) CreateWhiteListFrame(whitelistFrame.Parent);
                            }
                        }*/
                    }

                    Save();
                    return true;
                }
            };

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), whitelistFrame.RectTransform));
            foreach (WhiteListedPlayer wlp in whitelistedPlayers)
            {
                string blockText = wlp.Name;
                if (!string.IsNullOrWhiteSpace(wlp.IP)) blockText += " (" + wlp.IP + ")";
                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), listBox.Content.RectTransform),
                    blockText)
                {
                    UserData = wlp
                };

                var removeButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.8f), textBlock.RectTransform, Anchor.CenterRight), TextManager.Get("WhiteListRemove"))
                {
                    UserData = wlp,
                    OnClicked = RemoveFromWhiteList
                };
            }

            var nameArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), whitelistFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), nameArea.RectTransform), TextManager.Get("WhiteListName"));
            nameBox = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), nameArea.RectTransform), "");
            nameBox.OnTextChanged += (textBox, text) =>
            {
                addNewButton.Enabled = !string.IsNullOrEmpty(ipBox.Text) && !string.IsNullOrEmpty(nameBox.Text);
                return true;
            };

            var ipArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), whitelistFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), ipArea.RectTransform), TextManager.Get("WhiteListIP"));
            ipBox = new GUITextBox(new RectTransform(new Vector2(0.7f, 1.0f), ipArea.RectTransform), "");
            ipBox.OnTextChanged += (textBox, text) =>
            {
                addNewButton.Enabled = !string.IsNullOrEmpty(ipBox.Text) && !string.IsNullOrEmpty(nameBox.Text);
                return true;
            };

            addNewButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.1f), whitelistFrame.RectTransform), TextManager.Get("WhiteListAdd"))
            {
                OnClicked = AddToWhiteList
            };

            nameBox.Enabled = Enabled;
            ipBox.Enabled = Enabled;
            addNewButton.Enabled = false;

            return parent;
        }

        private bool RemoveFromWhiteList(GUIButton button, object obj)
        {
            WhiteListedPlayer wlp = obj as WhiteListedPlayer;
            if (wlp == null) return false;

            RemoveFromWhiteList(wlp);

            if (whitelistFrame != null)
            {
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
#endif
