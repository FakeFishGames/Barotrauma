using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class GUINumberInput : GUIComponent
    {
        public enum NumberType
        {
            Int, Float
        }

        public delegate void OnValueChangedHandler(GUINumberInput numberInput);
        public OnValueChangedHandler OnValueChanged;
        
        private GUITextBox textBox;
        private GUIButton plusButton, minusButton;

        private NumberType inputType;
        public NumberType InputType
        {
            get { return inputType; }
            set
            {
                inputType = value;
                plusButton.Visible = inputType == NumberType.Int;
                minusButton.Visible = inputType == NumberType.Int;
            }
        }

        public float? MinValueFloat, MaxValueFloat;

        private float floatValue;
        public float FloatValue
        {
            get { return floatValue; }
            set
            {
                if (value == floatValue) return;

                floatValue = value;
                if (MinValueFloat != null)
                {
                    floatValue = Math.Max(floatValue, MinValueFloat.Value);
                    minusButton.Enabled = floatValue > MinValueFloat;
                }
                if (MaxValueFloat != null)
                {
                    floatValue = Math.Min(floatValue, MaxValueFloat.Value);
                    plusButton.Enabled = floatValue < MaxValueFloat;
                }
                textBox.Text = floatValue.ToString("G", CultureInfo.InvariantCulture);

                OnValueChanged?.Invoke(this);
            }
        }

        public int? MinValueInt, MaxValueInt;

        private int intValue;
        public int IntValue
        {
            get { return intValue; }
            set
            {
                if (value == intValue) return;

                intValue = value;
                if (MinValueInt != null)
                {
                    intValue = Math.Max(intValue, MinValueInt.Value);
                    minusButton.Enabled = intValue > MinValueInt;
                }
                if (MaxValueInt != null)
                {
                    intValue = Math.Min(intValue, MaxValueInt.Value);
                    plusButton.Enabled = intValue < MaxValueInt;
                }
                textBox.Text = this.intValue.ToString();

                OnValueChanged?.Invoke(this);
            }
        }

        public GUINumberInput(Rectangle rect, string style, NumberType inputType, GUIComponent parent = null)
            : this(rect, style, inputType, Alignment.TopLeft, parent)
        {
        }

        public GUINumberInput(Rectangle rect, string style, NumberType inputType, Alignment alignment, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;

            this.alignment = alignment;

            if (parent != null)
                parent.AddChild(this);

            textBox = new GUITextBox(Rectangle.Empty, style, this);
            textBox.OnTextChanged += TextChanged;
            
            plusButton = new GUIButton(new Rectangle(0, 0, 15, rect.Height / 2), "+", null, Alignment.TopRight, Alignment.Center, style, this);
            plusButton.OnClicked += ChangeIntValue;
            plusButton.Visible = inputType == NumberType.Int;
            minusButton = new GUIButton(new Rectangle(0, 0, 15, rect.Height / 2), "-", null, Alignment.BottomRight, Alignment.Center, style, this);
            minusButton.OnClicked += ChangeIntValue;
            minusButton.Visible = inputType == NumberType.Int;

            if (inputType == NumberType.Int)
            {
                textBox.Text = "0";
                textBox.OnDeselected += (txtBox, key) =>
                {
                    textBox.Text = IntValue.ToString();
                };
            }
            else if (inputType == NumberType.Float)
            {
                textBox.Text = "0.0";
                textBox.OnDeselected += (txtBox, key) =>
                {
                    textBox.Text = FloatValue.ToString("G", CultureInfo.InvariantCulture);
                };
            }



            InputType = inputType;
        }
        
        private bool ChangeIntValue(GUIButton button, object userData)
        {
            if (button == plusButton)
            {
                IntValue++;
            }
            else
            {
                IntValue--;
            }

            return false;
        }

        private bool TextChanged(GUITextBox textBox, string text)
        {
            switch (InputType)
            {
                case NumberType.Int:
                    int newIntValue = IntValue;
                    if (text == "" || text == "-") 
                    {
                        IntValue = 0;
                        textBox.Text = text;
                    }
                    else if (int.TryParse(text, out newIntValue))
                    {
                        IntValue = newIntValue;
                    }
                    else
                    {
                        textBox.Text = IntValue.ToString();
                    }
                    break;
                case NumberType.Float:
                    float newFloatValue = FloatValue;

                    text = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());

                    if (text == "" || text == "-")
                    {
                        FloatValue = 0;
                        textBox.Text = text;
                    }
                    else if (float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out newFloatValue))
                    {
                        FloatValue = newFloatValue;
                        textBox.Text = text;
                    }
                    /*else
                    {
                        textBox.Text = FloatValue.ToString("G", CultureInfo.InvariantCulture);
                    }*/
                    break;
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
