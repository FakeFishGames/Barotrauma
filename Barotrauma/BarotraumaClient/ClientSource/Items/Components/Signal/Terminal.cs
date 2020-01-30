using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        private GUIListBox historyBox;
        private GUITextBlock fillerBlock;
        private GUITextBox inputBox;
        private bool shouldSelectInputBox;

        partial void InitProjSpecific(XElement element)
        {
            var layoutGroup = new GUILayoutGroup(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset })
            {
                ChildAnchor = Anchor.TopCenter,
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            historyBox = new GUIListBox(new RectTransform(new Vector2(1, .9f), layoutGroup.RectTransform), style: null)
            {
                AutoHideScrollBar = false
            };

            // Create fillerBlock to cover historyBox so new values appear at the bottom of historyBox
            // This could be removed if GUIListBox supported aligning its children
            fillerBlock = new GUITextBlock(new RectTransform(new Vector2(1, 1), historyBox.Content.RectTransform, anchor: Anchor.TopCenter), string.Empty)
            {
                CanBeFocused = false
            };

            new GUIFrame(new RectTransform(new Vector2(0.9f, 0.01f), layoutGroup.RectTransform), style: "HorizontalLine");

            inputBox = new GUITextBox(new RectTransform(new Vector2(1, .1f), layoutGroup.RectTransform), textColor: Color.LimeGreen)
            {
                MaxTextLength = MaxMessageLength,
                OverflowClip = true,
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
                    return true;
                }
            };
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            if (!string.IsNullOrEmpty(DisplayedWelcomeMessage))
            {
                ShowOnDisplay(DisplayedWelcomeMessage);
            }
        }

        private void SendOutput(string input)
        {
            if (input.Length > MaxMessageLength)
            {
                input = input.Substring(0, MaxMessageLength);
            }

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
                    textColor: Color.LimeGreen, wrap: true)
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

        public override bool Select(Character character)
        {
            shouldSelectInputBox = true;
            return base.Select(character);
        }

        // This method is overrided instead of the UpdateHUD method because this ensures the input box is selected
        // even when the terminal component is selected for the very first time. Doing the input box selection in the
        // UpdateHUD method only selects the input box on every terminal selection except for the very first time.
        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            if (shouldSelectInputBox)
            {
                inputBox.Select();
                shouldSelectInputBox = false;
            }
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