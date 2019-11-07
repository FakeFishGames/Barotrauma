using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent, IClientSerializable, IServerSerializable
    {
        private GUIListBox historyBox;
        private GUITextBlock fillerBlock;

        partial void InitProjSpecific(XElement element)
        {
            GUIFrame marginFrame = new GUIFrame(new RectTransform(new Vector2(.9f, .8f), GuiFrame.RectTransform, anchor: Anchor.Center));

            historyBox = new GUIListBox(new RectTransform(new Vector2(1, .85f), marginFrame.RectTransform, Anchor.TopCenter));

            // Creating fillerBlock which covers the whole historyBox allows new values to appear at the bottom of historyBox
            fillerBlock = new GUITextBlock(new RectTransform(new Vector2(1, 1), historyBox.Content.RectTransform, anchor: Anchor.Center), string.Empty)
            {
                CanBeFocused = false
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

            GUITextBlock newBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0), historyBox.Content.RectTransform, anchor: Anchor.TopCenter),
                    "> " + newValue,
                    textColor: Color.LimeGreen)
            {
                CanBeFocused = false
            };

            if (fillerBlock != null)
            {
                float y = fillerBlock.RectTransform.RelativeSize.Y - newBlock.RectTransform.RelativeSize.Y;
                if (y > 0)
                {
                    fillerBlock.RectTransform.RelativeSize = new Vector2(1, y);
                }
                else
                {
                    historyBox.RemoveChild(fillerBlock);
                    fillerBlock = null;
                }
            }

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