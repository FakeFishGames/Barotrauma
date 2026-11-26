#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public class FloatSetting : BaseSetting<float>
    {
        public Vector2 Range { get; set; } = new Vector2(0f, 1f);
        public float StepValue { get; set; } = 0.1f;
        public LocalizedString Label { get; set; }
        public LocalizedString? ToolTip { get; set; }
        public Func<float, string> LabelFunc { get; set; } = static x => Math.Round(x, 2).ToString();
        public bool UseSlider { get; set; }

#if CLIENT
        protected GUIScrollBar? ScrollBar;
        protected GUINumberInput? NumberInput;
#endif

        public FloatSetting(Identifier identifier, float defaultValue, LocalizedString label) : base(identifier, defaultValue)
        {
            Label = label;
        }

        public override void Load(XElement element)
        {
            Value = element.GetAttributeFloat("value", DefaultValue);
        }

        public override void Save(XElement element)
        {
            element.SetAttributeValue("value", Value);
        }

        public override void WriteMessage(IWriteMessage writeMessage)
        {
            writeMessage.WriteSingle(Value);
        }

        public override void ReadMessage(IReadMessage readMessage)
        {
            Value = readMessage.ReadSingle();
        }

#if CLIENT
        public override void CreateUI()
        {
            MainUI = new GUIFrame(new RectTransform((1.0f, 0.12f), null), style: null);

            var vertical = new GUILayoutGroup(new RectTransform(Vector2.One, MainUI.RectTransform), isHorizontal: false);

            new GUITextBlock(new RectTransform((1.0f, 0.35f), vertical.RectTransform), Label);

            if (UseSlider)
            {
                var horizontal = new GUILayoutGroup(new RectTransform((1.0f, 0.65f), vertical.RectTransform), isHorizontal: true);
                ScrollBar = new GUIScrollBar(new RectTransform((0.72f, 1.0f), horizontal.RectTransform), style: "GUISlider")
                {
                    Range = Range,
                    StepValue = StepValue,
                    BarSize = 0.1f,
                    BarScrollValue = PendingValue
                };

                if (ToolTip != null)
                {
                    ScrollBar.ToolTip = ToolTip;
                }

                var label = new GUITextBlock(new RectTransform((0.28f, 1.0f), horizontal.RectTransform),
                    LabelFunc(PendingValue), wrap: false, textAlignment: Alignment.Center);

                ScrollBar.OnMoved = (sb, val) =>
                {
                    label.Text = LabelFunc(sb.BarScrollValue);
                    PendingValue = sb.BarScrollValue;
                    return true;
                };

                ScrollBar.OnAddedToGUIUpdateList = scrollBar =>
                {
                    ScrollBar.Enabled = CanClientEdit;
                    ScrollBar.BarScrollValue = PendingValue;
                    label.Text = LabelFunc(ScrollBar.BarScrollValue);
                };
            }
            else
            {
                NumberInput = new GUINumberInput(new RectTransform((1f, 0.3f), vertical.RectTransform), NumberType.Float)
                {
                    MinValueFloat = Range.X,
                    MaxValueFloat = Range.Y,
                    ValueStep = StepValue,
                    OnValueChanged = numberInput =>
                    {
                        PendingValue = numberInput.FloatValue;
                    },
                    OnAddedToGUIUpdateList = numberInput =>
                    {
                        ((GUINumberInput)numberInput).FloatValue = PendingValue;
                        numberInput.Enabled = CanClientEdit;
                    }
                };
            }
        }
#endif
    }
}