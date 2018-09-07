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
        
        public GUITextBox TextBox { get; private set; }
        public GUIButton PlusButton { get; private set; }
        public GUIButton MinusButton { get; private set; }

        private NumberType inputType;
        public NumberType InputType
        {
            get { return inputType; }
            set
            {
                inputType = value;
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }
        }

        private float? minValueFloat, maxValueFloat;
        public float? MinValueFloat
        {
            get { return minValueFloat; }
            set
            {
                minValueFloat = value;
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }                
        }
        public float? MaxValueFloat
        {
            get { return maxValueFloat; }
            set
            {
                maxValueFloat = value;
                PlusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                MinusButton.Visible = PlusButton.Visible;
            }
        }

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
                    MinusButton.Enabled = floatValue > MinValueFloat;
                }
                if (MaxValueFloat != null)
                {
                    floatValue = Math.Min(floatValue, MaxValueFloat.Value);
                    PlusButton.Enabled = floatValue < MaxValueFloat;
                }
                TextBox.Text = floatValue.ToString("G", CultureInfo.InvariantCulture);

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
                    MinusButton.Enabled = intValue > MinValueInt;
                }
                if (MaxValueInt != null)
                {
                    intValue = Math.Min(intValue, MaxValueInt.Value);
                    PlusButton.Enabled = intValue < MaxValueInt;
                }
                TextBox.Text = this.intValue.ToString();

                OnValueChanged?.Invoke(this);
            }
        }

        private float pressedTimer;
        private float pressedDelay = 0.5f;
        private bool IsPressedTimerRunning { get { return pressedTimer > 0; } }

        public GUINumberInput(RectTransform rectT, NumberType inputType, string style = "", Alignment textAlignment = Alignment.Center) : base(style, rectT)
        {
            int buttonHeight = Rect.Height / 2;
            int margin = 2;
            Point buttonSize = new Point(buttonHeight - margin, buttonHeight - margin);
            TextBox = new GUITextBox(new RectTransform(new Point(Rect.Width, Rect.Height), rectT), textAlignment: textAlignment, style: style)
            {
                ClampText = false,
                OnTextChanged = TextChanged
            };
            var buttonArea = new GUIFrame(new RectTransform(new Point(buttonSize.X, buttonSize.Y * 2), rectT, Anchor.CenterRight), style: null);
            PlusButton = new GUIButton(new RectTransform(buttonSize, buttonArea.RectTransform), "+");
            PlusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            PlusButton.OnClicked += PlusButtonClicked;
            PlusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    if (inputType == NumberType.Int)
                    {
                        IntValue++;
                    }
                    else if (maxValueFloat.HasValue && minValueFloat.HasValue)
                    {
                        FloatValue += (MaxValueFloat.Value - minValueFloat.Value) / 100.0f;
                    }
                }
                return true;
            };
            PlusButton.Visible = inputType == NumberType.Int;

            MinusButton = new GUIButton(new RectTransform(buttonSize, buttonArea.RectTransform, Anchor.BottomRight), "-");
            MinusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            MinusButton.OnClicked += MinusButtonClicked;
            MinusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    if (inputType == NumberType.Int)
                    {
                        IntValue--;
                    }
                    else if (maxValueFloat.HasValue && minValueFloat.HasValue)
                    {                        
                        FloatValue -= (MaxValueFloat.Value - minValueFloat.Value) / 100.0f;
                    }
                }
                return true;
            };
            MinusButton.Visible = inputType == NumberType.Int;

            if (inputType == NumberType.Int)
            {
                TextBox.Text = "0";
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    TextBox.Text = IntValue.ToString();
                    TextBox.Deselect();
                    return true;
                };
                TextBox.OnDeselected += (txtBox, key) =>
                {
                    TextBox.Text = IntValue.ToString();
                };
            }
            else if (inputType == NumberType.Float)
            {
                TextBox.Text = "0.0";
                TextBox.OnDeselected += (txtBox, key) =>
                {
                    TextBox.Text = FloatValue.ToString("G", CultureInfo.InvariantCulture);
                };
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    TextBox.Text = FloatValue.ToString("G", CultureInfo.InvariantCulture);
                    TextBox.Deselect();
                    return true;
                };
            }

            InputType = inputType;
        }

        private bool PlusButtonClicked(GUIButton button, object userData)
        {
            if (inputType == NumberType.Int)
            {
                IntValue++;
            }
            else if (inputType == NumberType.Float)
            {
                if (!maxValueFloat.HasValue || !minValueFloat.HasValue) return false;
                FloatValue += (MaxValueFloat.Value - minValueFloat.Value) / 10.0f;
            }
            return false;
        }

        private bool MinusButtonClicked(GUIButton button, object userData)
        {
            if (inputType == NumberType.Int)
            {
                IntValue--;
            }
            else if (inputType == NumberType.Float)
            {
                if (!maxValueFloat.HasValue || !minValueFloat.HasValue) return false;
                FloatValue -= (MaxValueFloat.Value - minValueFloat.Value) / 10.0f;
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

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IsPressedTimerRunning)
            {
                pressedTimer -= deltaTime;
            }
        }
    }
}
