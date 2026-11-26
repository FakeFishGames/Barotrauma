#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public class IntSetting : BaseSetting<int>
    {
        public Point Range { get; set; } = new Point(Int32.MinValue, Int32.MaxValue);
        public int StepValue { get; set; } = 1;
        public LocalizedString Label { get; set; }
        public LocalizedString? ToolTip { get; set; }

#if CLIENT
        protected GUINumberInput? NumberInput;
#endif

        public IntSetting(Identifier identifier, int defaultValue, LocalizedString label) : base(identifier, defaultValue)
        {
            Label = label;
        }

        public override void Load(XElement element)
        {
            Value = element.GetAttributeInt("value", DefaultValue);
        }

        public override void Save(XElement element)
        {
            element.SetAttributeValue("value", Value);
        }

        public override void WriteMessage(IWriteMessage writeMessage)
        {
            writeMessage.WriteInt32(Value);
        }

        public override void ReadMessage(IReadMessage readMessage)
        {
            Value = readMessage.ReadInt32();
        }

#if CLIENT
        public override void CreateUI()
        {
            MainUI = new GUIFrame(new RectTransform((1.0f, 0.12f), null), style: null);

            var vertical = new GUILayoutGroup(new RectTransform(Vector2.One, MainUI.RectTransform), isHorizontal: false);

            new GUITextBlock(new RectTransform((1.0f, 0.4f), vertical.RectTransform), Label);

            NumberInput = new GUINumberInput(new RectTransform((1f, 0.6f), vertical.RectTransform), NumberType.Int)
            {
                MinValueInt = Range.X,
                MaxValueInt = Range.Y,
                ValueStep = StepValue,
                OnValueChanged = numberInput =>
                {
                    PendingValue = numberInput.IntValue;
                },
                OnAddedToGUIUpdateList = numberInput =>
                {
                    ((GUINumberInput)numberInput).IntValue = PendingValue;
                    numberInput.Enabled = CanClientEdit;
                }
            };
        }
#endif
    }
}