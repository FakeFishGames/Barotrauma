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
                plusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                minusButton.Visible = plusButton.Visible;
            }
        }

        private float? minValueFloat, maxValueFloat;
        public float? MinValueFloat
        {
            get { return minValueFloat; }
            set
            {
                minValueFloat = value;
                plusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                minusButton.Visible = plusButton.Visible;
            }                
        }
        public float? MaxValueFloat
        {
            get { return maxValueFloat; }
            set
            {
                maxValueFloat = value;
                plusButton.Visible = inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue);
                minusButton.Visible = plusButton.Visible;
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

        private float pressedTimer;
        private float pressedDelay = 0.5f;
        private bool IsPressedTimerRunning { get { return pressedTimer > 0; } }

        public GUINumberInput(RectTransform rectT, NumberType inputType, string style = "", Alignment textAlignment = Alignment.Center) : base(style, rectT)
        {
            textBox = new GUITextBox(new RectTransform(Vector2.One, rectT), textAlignment: textAlignment, style: style)
            {
                ClampText = false,
                OnTextChanged = TextChanged
            };
            

            int height = Rect.Height / 2;
            var buttonSize = new Point(height, height);

            plusButton = new GUIButton(new RectTransform(buttonSize, rectT, Anchor.TopRight)
            {
                IsFixedSize = false
            }, "+");
            plusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            plusButton.OnClicked += PlusButtonClicked;
            plusButton.OnPressed += () =>
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
            plusButton.Visible = inputType == NumberType.Int;
            plusButton.ClampMouseRectToParent = false;

            minusButton = new GUIButton(new RectTransform(buttonSize, rectT, Anchor.BottomRight)
            {
                IsFixedSize = false
            }, "-");
            minusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            minusButton.OnClicked += MinusButtonClicked;
            minusButton.OnPressed += () =>
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
            minusButton.Visible = inputType == NumberType.Int;
            minusButton.ClampMouseRectToParent = false;

            if (inputType == NumberType.Int)
            {
                textBox.Text = "0";
                textBox.OnEnterPressed += (txtBox, txt) =>
                {
                    textBox.Text = IntValue.ToString();
                    textBox.Deselect();
                    return true;
                };
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
                textBox.OnEnterPressed += (txtBox, txt) =>
                {
                    textBox.Text = FloatValue.ToString("G", CultureInfo.InvariantCulture);
                    textBox.Deselect();
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
