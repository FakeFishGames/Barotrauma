using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    class GUINumberInput : GUIComponent
    {
        public delegate void OnValueChangedHandler(GUINumberInput numberInput, int number);
        public OnValueChangedHandler OnValueChanged;
        
        private GUITextBox textBox;
        private GUIButton plusButton, minusButton;

        public int? MinValue, MaxValue;

        private int value;
        public int Value
        {
            get { return value; }
            set
            {
                if (value == this.value) return;

                this.value = value;
                if (MinValue != null)
                {
                    this.value = Math.Max(this.value, (int)MinValue);
                    minusButton.Enabled = this.value > MinValue;
                }
                if (MaxValue != null)
                {
                    this.value = Math.Min(this.value, (int)MaxValue);
                    plusButton.Enabled = this.value < MaxValue;
                }
                textBox.Text = this.value.ToString();

                if (OnValueChanged != null) OnValueChanged(this, this.value);
            }
        }

        public GUINumberInput(Rectangle rect, string style, int? minValue = null, int? maxValue = null, GUIComponent parent = null)
            : this(rect, style, Alignment.TopLeft, minValue, maxValue, parent)
        {
        }

        public GUINumberInput(Rectangle rect, string style, Alignment alignment, int? minValue = null, int? maxValue = null, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;

            this.alignment = alignment;

            if (parent != null)
                parent.AddChild(this);

            textBox = new GUITextBox(Rectangle.Empty, style, this);

            textBox.OnTextChanged += TextChanged;

            plusButton = new GUIButton(new Rectangle(0, 0, 15, rect.Height / 2), "+", null, Alignment.TopRight, Alignment.Center, style, this);
            plusButton.OnClicked += ChangeValue;
            minusButton = new GUIButton(new Rectangle(0, 0, 15, rect.Height / 2), "-", null, Alignment.BottomRight, Alignment.Center, style, this);
            minusButton.OnClicked += ChangeValue;

            MinValue = minValue;
            MaxValue = maxValue;

            value = int.MaxValue;
            Value = minValue != null ? (int)minValue : 0;
        }

        private bool ChangeValue(GUIButton button, object userData)
        {
            if (button == plusButton)
            {
                Value++;
            }
            else
            {
                Value--;
            }

            return false;
        }

        private bool TextChanged(GUITextBox textBox, string text)
        {
            int newValue = Value;
            if (text == "" || text == "-")
            {
                Value = 0;
                textBox.Text = text;
            }
            else if (int.TryParse(text, out newValue))
            {
                Value = newValue;
            }
            else
            {
                textBox.Text = Value.ToString();
            }

            return true;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            DrawChildren(spriteBatch);
        }
    }
}
