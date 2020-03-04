using Microsoft.Xna.Framework;
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

        private GUIButton plusButton, minusButton;

        private NumberType inputType;
        public NumberType InputType
        {
            get { return inputType; }
            set
            {
                if (inputType == value) { return; }
                inputType = value;
                if (inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue))
                {
                    ShowPlusMinusButtons();
                }
                else
                {
                    HidePlusMinusButtons();
                }
            }
        }

        private float? minValueFloat, maxValueFloat;
        public float? MinValueFloat
        {
            get { return minValueFloat; }
            set
            {
                minValueFloat = value;
                ClampFloatValue();
                if (inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue))
                {
                    ShowPlusMinusButtons();
                }
                else
                {
                    HidePlusMinusButtons();
                }
            }                
        }
        public float? MaxValueFloat
        {
            get { return maxValueFloat; }
            set
            {
                maxValueFloat = value;
                ClampFloatValue();
                if (inputType == NumberType.Int ||
                    (inputType == NumberType.Float && MinValueFloat > float.MinValue && MaxValueFloat < float.MaxValue))
                {
                    ShowPlusMinusButtons();
                }
                else
                {
                    HidePlusMinusButtons();
                }
            }
        }

        private float floatValue;
        public float FloatValue
        {
            get { return floatValue; }
            set
            {
                if (MathUtils.NearlyEqual(value, floatValue)) return;
                floatValue = value;
                ClampFloatValue();
                float newValue = floatValue;
                UpdateText();
                //UpdateText may remove decimals from the value, force to full accuracy
                floatValue = newValue; 
                OnValueChanged?.Invoke(this);
            }
        }

        private int decimalsToDisplay = 1;
        public int DecimalsToDisplay
        {
            get { return decimalsToDisplay; }
            set
            {
                decimalsToDisplay = value;
                UpdateText();
            }
        }

        private int? minValueInt, maxValueInt;
        public int? MinValueInt
        {
            get { return minValueInt; }
            set
            {
                minValueInt = value;
                ClampIntValue();
            }
        }
        public int? MaxValueInt
        {
            get { return maxValueInt; }
            set
            {
                maxValueInt = value;
                ClampIntValue();
            }
        }

        private int intValue;
        public int IntValue
        {
            get { return intValue; }
            set
            {
                if (value == intValue) return;
                intValue = value;
                UpdateText();
            }
        }

        public override bool Enabled
        {
            get => base.Enabled;
            set
            {
                plusButton.Enabled = true;
                minusButton.Enabled = true;
                if (InputType == NumberType.Int) { ClampIntValue(); } else { ClampFloatValue(); }
                TextBox.Enabled = value;
                if (!value)
                {
                    plusButton.Enabled = false;
                    minusButton.Enabled = false;
                }
            }
        }

        public override ScalableFont Font
        {
            get
            {
                return base.Font;
            }
            set
            {
                base.Font = value;
                if (TextBox != null) { TextBox.Font = value; }
            }
        }

        public GUILayoutGroup LayoutGroup
        {
            get;
            private set;
        }

        public float valueStep;

        private float pressedTimer;
        private float pressedDelay = 0.5f;
        private bool IsPressedTimerRunning { get { return pressedTimer > 0; } }

        public GUINumberInput(RectTransform rectT, NumberType inputType, string style = "", Alignment textAlignment = Alignment.Center, float? relativeButtonAreaWidth = null) : base(style, rectT)
        {
            LayoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, rectT), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };

            float _relativeButtonAreaWidth = relativeButtonAreaWidth ?? MathHelper.Clamp(Rect.Height / (float)Rect.Width, 0.1f, 0.25f);

            TextBox = new GUITextBox(new RectTransform(new Vector2(1.0f - _relativeButtonAreaWidth, 1.0f), LayoutGroup.RectTransform), textAlignment: textAlignment, style: "GUITextBoxNoIcon")
            {
                ClampText = false
            };
            TextBox.CaretColor = TextBox.TextColor;
            TextBox.OnTextChanged += TextChanged;

            var buttonArea = new GUIFrame(new RectTransform(new Vector2(_relativeButtonAreaWidth, 1.0f), LayoutGroup.RectTransform, Anchor.CenterRight), style: null);
            plusButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.5f), buttonArea.RectTransform), style: null);
            GUI.Style.Apply(plusButton, "PlusButton", this);
            plusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            plusButton.OnClicked += (button, data) =>
            {
                IncreaseValue();
                return true;
            };
            plusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    IncreaseValue();
                }
                return true;
            };

            minusButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.5f), buttonArea.RectTransform, Anchor.BottomRight), style: null);
            GUI.Style.Apply(minusButton, "MinusButton", this);
            minusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            minusButton.OnClicked += (button, data) =>
            {
                ReduceValue();
                return true;
            };
            minusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    ReduceValue();
                }
                return true;
            };

            if (inputType != NumberType.Int)
            {
                HidePlusMinusButtons();
            }

            if (inputType == NumberType.Int)
            {
                UpdateText();
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    UpdateText();
                    TextBox.Deselect();
                    return true;
                };
                TextBox.OnDeselected += (txtBox, key) => UpdateText();
            }
            else if (inputType == NumberType.Float)
            {
                UpdateText();
                TextBox.OnDeselected += (txtBox, key) => UpdateText();
                TextBox.OnEnterPressed += (txtBox, txt) =>
                {
                    UpdateText();
                    TextBox.Deselect();
                    return true;
                };
            }
            InputType = inputType;
            switch (InputType)
            {
                case NumberType.Int:
                    TextBox.textFilterFunction = text => new string(text.Where(c => char.IsNumber(c) || c == '-').ToArray());
                    break;
                case NumberType.Float:
                    TextBox.textFilterFunction = text => new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                    break;
            }

            RectTransform.MinSize = TextBox.RectTransform.MinSize;
            LayoutGroup.Recalculate();
        }

        private void HidePlusMinusButtons()
        {
            plusButton.Parent.Visible = false;
            plusButton.Parent.IgnoreLayoutGroups = true;
            TextBox.RectTransform.RelativeSize = Vector2.One;
            LayoutGroup.Recalculate();
        }

        private void ShowPlusMinusButtons()
        {
            plusButton.Parent.Visible = true;
            plusButton.Parent.IgnoreLayoutGroups = false;
            TextBox.RectTransform.RelativeSize = new Vector2(1.0f - plusButton.Parent.RectTransform.RelativeSize.X, 1.0f);
            LayoutGroup.Recalculate();
        }

        private void ReduceValue()
        {
            if (inputType == NumberType.Int)
            {
                IntValue -= valueStep > 0 ? (int)valueStep : 1;
            }
            else if (maxValueFloat.HasValue && minValueFloat.HasValue)
            {
                FloatValue -= valueStep > 0 ? valueStep : Round();
            }
        }

        private void IncreaseValue()
        {
            if (inputType == NumberType.Int)
            {
                IntValue += valueStep > 0 ? (int)valueStep : 1;
            }
            else if (inputType == NumberType.Float)
            {
                FloatValue += valueStep > 0 ? valueStep : Round();
            }
        }

        /// <summary>
        /// Calculates one percent between the range as the increment/decrement.
        /// This value is rounded so that the bigger it is, the less decimals are used (min 0, max 3).
        /// Return value is clamped between 0.1f and 1000.
        /// </summary>
        private float Round()
        {
            if (!maxValueFloat.HasValue || !minValueFloat.HasValue) return 0;
            float onePercent = MathHelper.Lerp(minValueFloat.Value, maxValueFloat.Value, 0.01f);
            float diff = maxValueFloat.Value - minValueFloat.Value;
            int decimals = (int)MathHelper.Lerp(3, 0, MathUtils.InverseLerp(10, 1000, diff));
            return MathHelper.Clamp((float)Math.Round(onePercent, decimals), 0.1f, 1000);
        }

        private bool TextChanged(GUITextBox textBox, string text)
        {
            switch (InputType)
            {
                case NumberType.Int:
                    int newIntValue = IntValue;
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        intValue = 0;
                    }
                    else if (int.TryParse(text, out newIntValue))
                    {
                        intValue = newIntValue;
                    }
                    ClampIntValue();
                    break;
                case NumberType.Float:
                    float newFloatValue = FloatValue;
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        floatValue = 0;
                    }
                    else if (float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out newFloatValue))
                    {
                        floatValue = newFloatValue;
                    }
                    ClampFloatValue();
                    break;
            }
            OnValueChanged?.Invoke(this);
            return true;
        }

        private void ClampFloatValue()
        {
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
        }

        private void ClampIntValue()
        {
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
        }

        private void UpdateText()
        {
            switch (InputType)
            {
                case NumberType.Float:
                    TextBox.Text = FloatValue.Format(decimalsToDisplay);
                    break;
                case NumberType.Int:
                    TextBox.Text = IntValue.ToString();
                    break;
            }
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
