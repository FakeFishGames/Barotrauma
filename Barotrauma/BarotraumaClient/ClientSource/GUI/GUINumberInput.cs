﻿using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class GUINumberInput : GUIComponent
    {
        public delegate void OnValueEnteredHandler(GUINumberInput numberInput);
        public OnValueEnteredHandler OnValueEntered;

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
            get
            {
                return floatValue;
            }
            set
            {
                if (MathUtils.NearlyEqual(value, floatValue)) { return; }
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
            get 
            {
                return intValue; 
            }
            set
            {
                if (value == intValue) { return; }
                intValue = value;
                ClampIntValue();
                UpdateText();
            }
        }

        public override bool Enabled
        {
            get => base.Enabled;
            set
            {
                PlusButton.Enabled = true;
                MinusButton.Enabled = true;
                if (InputType == NumberType.Int) { ClampIntValue(); } else { ClampFloatValue(); }
                TextBox.Enabled = value;
                if (!value)
                {
                    PlusButton.Enabled = false;
                    MinusButton.Enabled = false;
                }
            }
        }

        public override GUIFont Font
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

        /// <summary>
        /// If enabled, the value wraps around to Max when you go below Min, and vice versa
        /// </summary>
        public bool WrapAround;

        public float valueStep;

        private float pressedTimer;
        private readonly float pressedDelay = 0.5f;
        private bool IsPressedTimerRunning { get { return pressedTimer > 0; } }

        public GUINumberInput(RectTransform rectT, NumberType inputType, string style = "", Alignment textAlignment = Alignment.Center, float? relativeButtonAreaWidth = null, bool hidePlusMinusButtons = false) : base(style, rectT)
        {
            LayoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, rectT), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };

            float _relativeButtonAreaWidth = relativeButtonAreaWidth ?? MathHelper.Clamp(Rect.Height / (float)Rect.Width, 0.1f, 0.25f);

            TextBox = new GUITextBox(new RectTransform(new Vector2(1.0f - _relativeButtonAreaWidth, 1.0f), LayoutGroup.RectTransform), textAlignment: textAlignment, style: "GUITextBoxNoIcon")
            {
                ClampText = false
            };
            TextBox.CaretColor = TextBox.TextColor;
            TextBox.OnTextChanged += TextChanged;
            TextBox.OnDeselected += (sender, key) =>
            {
                if (inputType == NumberType.Int)
                {
                    ClampIntValue();
                }
                else
                {
                    ClampFloatValue();
                }

                OnValueEntered?.Invoke(this);
            };
            TextBox.OnEnterPressed += (textBox, text) =>
            {
                if (inputType == NumberType.Int)
                {
                    ClampIntValue();
                }
                else
                {
                    ClampFloatValue();
                }

                OnValueEntered?.Invoke(this);
                return true;
            };

            var buttonArea = new GUIFrame(new RectTransform(new Vector2(_relativeButtonAreaWidth, 1.0f), LayoutGroup.RectTransform, Anchor.CenterRight), style: null);
            PlusButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.5f), buttonArea.RectTransform), style: null);
            GUIStyle.Apply(PlusButton, "PlusButton", this);
            PlusButton.ClickSound = GUISoundType.Increase;
            PlusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            PlusButton.OnClicked += (button, data) =>
            {
                IncreaseValue();
                return true;
            };
            PlusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    IncreaseValue();
                }
                return true;
            };

            MinusButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.5f), buttonArea.RectTransform, Anchor.BottomRight), style: null);
            GUIStyle.Apply(MinusButton, "MinusButton", this);
            MinusButton.ClickSound = GUISoundType.Decrease;
            MinusButton.OnButtonDown += () =>
            {
                pressedTimer = pressedDelay;
                return true;
            };
            MinusButton.OnClicked += (button, data) =>
            {
                ReduceValue();
                return true;
            };
            MinusButton.OnPressed += () =>
            {
                if (!IsPressedTimerRunning)
                {
                    ReduceValue();
                }
                return true;
            };

            if (inputType != NumberType.Int || hidePlusMinusButtons)
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

            RectTransform.MinSize = new Point(
                Math.Max(rectT.MinSize.X, TextBox.RectTransform.MinSize.X), 
                Math.Max(rectT.MinSize.Y, TextBox.RectTransform.MinSize.Y));
            LayoutGroup.Recalculate();
        }

        private void HidePlusMinusButtons()
        {
            PlusButton.Parent.Visible = false;
            PlusButton.Parent.IgnoreLayoutGroups = true;
            TextBox.RectTransform.RelativeSize = Vector2.One;
            LayoutGroup.Recalculate();
        }

        private void ShowPlusMinusButtons()
        {
            PlusButton.Parent.Visible = true;
            PlusButton.Parent.IgnoreLayoutGroups = false;
            TextBox.RectTransform.RelativeSize = new Vector2(1.0f - PlusButton.Parent.RectTransform.RelativeSize.X, 1.0f);
            LayoutGroup.Recalculate();
        }

        private void ReduceValue()
        {
            if (inputType == NumberType.Int)
            {
                IntValue -= valueStep > 0 ? (int)valueStep : 1;
                ClampIntValue();
            }
            else if (maxValueFloat.HasValue && minValueFloat.HasValue)
            {
                FloatValue -= valueStep > 0 ? valueStep : Round();
                ClampFloatValue();
            }
        }

        private void IncreaseValue()
        {
            if (inputType == NumberType.Int)
            {
                IntValue += valueStep > 0 ? (int)valueStep : 1;
                ClampIntValue();
            }
            else if (inputType == NumberType.Float)
            {
                FloatValue += valueStep > 0 ? valueStep : Round();
                ClampFloatValue();
            }
        }

        /// <summary>
        /// Calculates one percent between the range as the increment/decrement.
        /// This value is rounded so that the bigger it is, the less decimals are used (min 0, max 3).
        /// Return value is clamped between 0.1f and 1000.
        /// </summary>
        private float Round()
        {
            if (!maxValueFloat.HasValue || !minValueFloat.HasValue) { return 0; }
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
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        intValue = 0;
                    }
                    else if (int.TryParse(text, out int newIntValue))
                    {
                        intValue = newIntValue;
                    }
                    break;
                case NumberType.Float:
                    if (string.IsNullOrWhiteSpace(text) || text == "-")
                    {
                        floatValue = 0;
                    }
                    else if (float.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out float newFloatValue))
                    {
                        floatValue = newFloatValue;
                    }
                    break;
            }
            OnValueChanged?.Invoke(this);
            return true;
        }

        private void ClampFloatValue()
        {
            if (MinValueFloat != null)
            {
                floatValue = 
                    WrapAround && MinValueFloat.HasValue && floatValue < MinValueFloat.Value ? 
                    MaxValueFloat.Value : 
                    Math.Max(floatValue, MinValueFloat.Value);
                MinusButton.Enabled = WrapAround || floatValue > MinValueFloat;
            }
            if (MaxValueFloat != null)
            {
                floatValue =
                    WrapAround && MaxValueFloat.HasValue && floatValue > MaxValueFloat.Value ?
                    MinValueFloat.Value : 
                    Math.Min(floatValue, MaxValueFloat.Value);
                PlusButton.Enabled = WrapAround || floatValue < MaxValueFloat;
            }
        }

        private void ClampIntValue()
        {
            if (MinValueInt != null && intValue < MinValueInt.Value)
            {
                intValue = WrapAround && MaxValueInt.HasValue ? MaxValueInt.Value : Math.Max(intValue, MinValueInt.Value);
                UpdateText();
            }
            if (MaxValueInt != null && intValue > MaxValueInt.Value)
            {
                intValue = WrapAround && MinValueInt.HasValue ? MinValueInt.Value : Math.Min(intValue, MaxValueInt.Value);
                UpdateText();
            }
            PlusButton.Enabled = WrapAround || MaxValueInt == null || intValue < MaxValueInt;
            MinusButton.Enabled = WrapAround || MinValueInt == null || intValue > MinValueInt;
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
