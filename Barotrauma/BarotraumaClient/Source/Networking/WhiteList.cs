using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class WhiteListedPlayer
    {
        public WhiteListedPlayer(string name, UInt16 identifier, string ip)
        {
            Name = name;
            IP = ip;

            UniqueIdentifier = identifier;
        }
    }

    partial class WhiteList
    {
        private GUIComponent whitelistFrame;

        private GUITextBox nameBox;
        private GUITextBox ipBox;
        private GUIButton addNewButton;

        public struct LocalAdded
        {
            public string name;
            public string ip;
        };

        public bool localEnabled;
        public List<UInt16> localRemoved = new List<UInt16>();
        public List<LocalAdded> localAdded = new List<LocalAdded>();

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
                    localEnabled = box.Selected;

                    return true;
                }
            };

            localEnabled = Enabled;

            var listBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.7f), whitelistFrame.RectTransform));
            foreach (WhiteListedPlayer wlp in whitelistedPlayers)
            {
                if (localRemoved.Contains(wlp.UniqueIdentifier)) continue;
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

            if (!localRemoved.Contains(wlp.UniqueIdentifier)) localRemoved.Add(wlp.UniqueIdentifier);

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
            
            if (!localAdded.Any(p => p.ip == ipBox.Text)) localAdded.Add(new LocalAdded() { name = nameBox.Text, ip = ipBox.Text });

            if (whitelistFrame != null)
            {
                CreateWhiteListFrame(whitelistFrame.Parent);
            }
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

            whitelistedPlayers.Clear();
            Int32 bannedPlayerCount = incMsg.ReadVariableInt32();
            for (int i = 0; i < bannedPlayerCount; i++)
            {
                string name = incMsg.ReadString();
                UInt16 uniqueIdentifier = incMsg.ReadUInt16();
                
                string ip = "";
                if (isOwner)
                {
                    ip = incMsg.ReadString();
                }
                else
                {
                    ip = "IP concealed by host";
                }
                whitelistedPlayers.Add(new WhiteListedPlayer(name, uniqueIdentifier, ip));
            }

            if (whitelistFrame != null)
            {
                CreateWhiteListFrame(whitelistFrame.Parent);
            }
        }

        public void ClientAdminWrite(NetBuffer outMsg)
        {
            outMsg.Write((UInt16)localRemoved.Count);
            foreach (UInt16 uniqueId in localRemoved)
            {
                outMsg.Write(uniqueId);
            }

            outMsg.Write((UInt16)localAdded.Count);
            foreach (LocalAdded la in localAdded)
            {
                outMsg.Write(la.name);
                outMsg.Write(la.ip); //TODO: ENCRYPT
            }

            localRemoved.Clear();
            localAdded.Clear();
        }
    }
}
