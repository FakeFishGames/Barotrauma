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
            GUILayoutGroup layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(.9f, .8f), GuiFrame.RectTransform, anchor: Anchor.Center));

            historyBox = new GUIListBox(new RectTransform(new Vector2(1, .9f), layoutGroup.RectTransform));

            // Create fillerBlock to cover historyBox so new values appear at the bottom of historyBox
            // This could be removed if GUIListBox supported aligning its children
            fillerBlock = new GUITextBlock(new RectTransform(new Vector2(1, 1), historyBox.Content.RectTransform, anchor: Anchor.TopCenter), string.Empty)
            {
                CanBeFocused = false
            };

            new GUITextBox(new RectTransform(new Vector2(1, .1f), layoutGroup.RectTransform), textColor: Color.LimeGreen)
            {
                OnEnterPressed = (GUITextBox textBox, string text) =>
                {
                    if (GameMain.NetworkMember == null)
                    {
                        SendOutput(text);
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

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            ShowOnDisplay(WelcomeMessage);
        }

        private void SendOutput(string input)
        {
            OutputValue = input;
            item.SendSignal(0, input, "signal_out", null);
            ShowOnDisplay(input);
        }

        partial void ShowOnDisplay(string input)
        {
            while (historyBox.Content.CountChildren > 60)
            {
                historyBox.RemoveChild(historyBox.Content.Children.First());
            }

            GUITextBlock newBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0), historyBox.Content.RectTransform, anchor: Anchor.TopCenter),
                    "> " + input,
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

            historyBox.RecalculateChildren();
            historyBox.UpdateScrollBarSize();
            historyBox.ScrollBar.BarScrollValue = 1;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write((string)extraData[2]);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            SendOutput(msg.ReadString());
        }
    }
}