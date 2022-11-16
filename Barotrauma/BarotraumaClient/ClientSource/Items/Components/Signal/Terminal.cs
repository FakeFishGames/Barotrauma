using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        private readonly struct ClientEventData : IEventData
        {
            public readonly string Text;
            
            public ClientEventData(string text)
            {
                Text = text;
            }
        }
        
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

            CreateFillerBlock();

            new GUIFrame(new RectTransform(new Vector2(0.9f, 0.01f), layoutGroup.RectTransform), style: "HorizontalLine");

            inputBox = new GUITextBox(new RectTransform(new Vector2(1, .1f), layoutGroup.RectTransform), textColor: TextColor)
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
                        item.CreateClientEvent(this, new ClientEventData(text));
                    }
                    textBox.Text = string.Empty;
                    return true;
                }
            };

            layoutGroup.Recalculate();
        }

        // Create fillerBlock to cover historyBox so new values appear at the bottom of historyBox
        // This could be removed if GUIListBox supported aligning its children
        public void CreateFillerBlock()
        {
            fillerBlock = new GUITextBlock(new RectTransform(new Vector2(1, 1), historyBox.Content.RectTransform, anchor: Anchor.TopCenter), string.Empty)
            {
                CanBeFocused = false
            };
        }

        private void SendOutput(string input)
        {
            if (input.Length > MaxMessageLength)
            {
                input = input.Substring(0, MaxMessageLength);
            }

            OutputValue = input;
            ShowOnDisplay(input, addToHistory: true, TextColor);
            item.SendSignal(input, "signal_out");
        }

        partial void ShowOnDisplay(string input, bool addToHistory, Color color)
        {
            if (addToHistory)
            {
                messageHistory.Add(new TerminalMessage(input, color));
                while (messageHistory.Count > MaxMessages)
                {
                    messageHistory.RemoveAt(0);
                }
                while (historyBox.Content.CountChildren > MaxMessages)
                {
                    historyBox.RemoveChild(historyBox.Content.Children.First());
                }
            }

            GUITextBlock newBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0), historyBox.Content.RectTransform, anchor: Anchor.TopCenter),
                    "> " + input,
                    textColor: color, wrap: true, font: UseMonospaceFont ? GUIStyle.MonospacedFont : GUIStyle.Font)
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
        public override void AddToGUIUpdateList(int order = 0)
        {
            base.AddToGUIUpdateList(order: order);
            if (shouldSelectInputBox)
            {
                inputBox.Select();
                shouldSelectInputBox = false;
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            if (TryExtractEventData(extraData, out ClientEventData eventData))
            {
                msg.WriteString(eventData.Text);
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            SendOutput(msg.ReadString());
        }
    }
}