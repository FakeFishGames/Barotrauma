using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent, IClientSerializable, IServerSerializable
    {
        private GUIListBox historyBox;

        partial void InitProjSpecific(XElement element)
        {
            GUIFrame marginFrame = new GUIFrame(new RectTransform(new Vector2(.9f, .9f), GuiFrame.RectTransform, anchor: Anchor.Center));

            historyBox = new GUIListBox(
                new RectTransform(new Vector2(1, .8f), marginFrame.RectTransform, Anchor.BottomCenter)
                {
                    RelativeOffset = new Vector2(0, .05f),
                },
                style: "InnerFrame")
            {
                AutoHideScrollBar = true
            };

            new GUITextBox(new RectTransform(new Vector2(1, .1f), marginFrame.RectTransform, anchor: Anchor.BottomCenter),
                textColor: Color.LimeGreen)
            {
                OnEnterPressed = (GUITextBox textBox, string text) =>
                {
                    if (GameMain.NetworkMember == null)
                    {
                        AddNewValue(text);
                    }
                    else
                    {
                        item.CreateClientEvent(this, new object[] { text });
                    }
                    textBox.Text = string.Empty;
                    textBox.Deselect();
                    return true;
                }
            };
        }

        private void AddNewValue(string newValue)
        {
            OutputValue = newValue;

            while (historyBox.Content.CountChildren > 60)
            {
                historyBox.RemoveChild(historyBox.Content.Children.First());
            }

            new GUITextBlock(
                    new RectTransform(new Vector2(1, 0), historyBox.Content.RectTransform, anchor: Anchor.TopCenter),
                    "> " + newValue,
                    textColor: Color.LimeGreen)
            {
                CanBeFocused = false
            };

            historyBox.ScrollBar.BarScrollValue = 1;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write((string)extraData[2]);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            AddNewValue(msg.ReadString());
        }
    }
}