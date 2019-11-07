using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent, IClientSerializable, IServerSerializable
    {
        private GUIListBox historyListBox;

        partial void InitProjSpecific(XElement element)
        {
            GUIFrame marginFrame = new GUIFrame(new RectTransform(new Vector2(.9f, .9f), GuiFrame.RectTransform, Anchor.Center));
            historyListBox = new GUIListBox(
                new RectTransform(new Vector2(1, .8f), marginFrame.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0, .05f)
                },
                false, null, "InnerFrame");
            for (int i = 0; i < history.Count; i++)
            {
                new GUITextBlock(
                    new RectTransform(new Vector2(1, .1f), historyListBox.RectTransform, Anchor.BottomCenter)
                    {
                        RelativeOffset = new Vector2(0, i * .1f)
                    },
                    string.Empty,
                    Color.LimeGreen);
            }
            new GUITextBox(new RectTransform(new Vector2(1, .1f), marginFrame.RectTransform, Anchor.BottomCenter), string.Empty, Color.LimeGreen)
            {
                OnEnterPressed = (GUITextBox textBox, string text) =>
                {
                    AddToHistory(text);
                    textBox.Text = string.Empty;
                    textBox.Deselect();
                    item.CreateClientEvent(this);
                    return true;
                }
            };
        }

        private void AddToHistory(string text)
        {
            List<string> updatedHistory = new List<string> { text };
            for (int i = 0; i < maxHistoryCount - 1; i++)
            {
                updatedHistory.Add(history[i]);
            }
            history = updatedHistory;
            UpdateListBoxContent();
        }

        private void UpdateListBoxContent()
        {
            int historyIndex = 0;
            for (int i = 0; i < historyListBox.CountChildren; i++)
            {
                GUIComponent component = historyListBox.GetChild(i);
                if (component == null || !(component is GUITextBlock)) { continue; }
                (component as GUITextBlock).Text = history[historyIndex] != null ? "> " + history[historyIndex] : string.Empty;
                if (++historyIndex >= history.Count) { break; }
            }
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            foreach (string entry in history)
            {
                msg.Write(entry);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            // if (correctionTimer > 0) ?
            // if (Game.Client.MidRoundSyncing) ?

            List<string> updatedHistory = new List<string>();
            for (int i = 0; i < maxHistoryCount; i++)
            {
                updatedHistory.Add(msg.ReadString());
            }
            history = updatedHistory;
            UpdateListBoxContent();
        }
    }
}