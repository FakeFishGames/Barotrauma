#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public class StringSetting : BaseSetting<string>
    {
        public LocalizedString Label { get; set; }
        public LocalizedString? ToolTip { get; set; }

        public int MaxSize { get; set; } = 128;

#if CLIENT
        protected GUITextBox? TextBox;
#endif

        public StringSetting(Identifier identifier, string defaultValue, LocalizedString label) : base(identifier, defaultValue)
        {
            Label = label;
        }

        public override void Load(XElement element)
        {
            Value = element.GetAttributeString("value", DefaultValue);
        }

        public override void Save(XElement element)
        {
            element.SetAttributeValue("value", Value);
        }

        public override void WriteMessage(IWriteMessage writeMessage)
        {
            writeMessage.WriteString(Value);
        }

        public override void ReadMessage(IReadMessage readMessage)
        {
            Value = readMessage.ReadString();
            if (Value.Length > MaxSize)
            {
                Value = Value.Substring(0, MaxSize);
            }
        }

#if CLIENT
        public override void CreateUI()
        {
            MainUI = new GUIFrame(new RectTransform((1.0f, 0.12f), null), style: null);

            var vertical = new GUILayoutGroup(new RectTransform(Vector2.One, MainUI.RectTransform), isHorizontal: false);
            new GUITextBlock(new RectTransform((1.0f, 0.35f), vertical.RectTransform), Label);
            TextBox = new GUITextBox(new RectTransform((1.0f, 0.65f), vertical.RectTransform), PendingValue)
            {
                MaxTextLength = MaxSize
            };

            TextBox.OnAddedToGUIUpdateList = textBox =>
            {
                TextBox.Text = PendingValue;
                TextBox.Enabled = CanClientEdit;
            };

            TextBox.OnTextChanged += (GUITextBox textBox, string text) =>
            {
                PendingValue = text;
                return true;
            };

            if (ToolTip != null)
            {
                TextBox.ToolTip = ToolTip;
            }

            TextBox.OnTextChanged += (GUITextBox textBox, string text) =>
            {
                PendingValue = text;
                return true;
            };
        }
#endif

    }
}