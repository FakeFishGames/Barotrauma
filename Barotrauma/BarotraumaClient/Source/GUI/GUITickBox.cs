using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    public class GUITickBox : GUIComponent
    {
        private GUILayoutGroup layoutGroup;
        private GUIFrame box;
        private GUITextBlock text;

        public delegate bool OnSelectedHandler(GUITickBox obj);
        public OnSelectedHandler OnSelected;

        public static int size = 20;

        private GUIRadioButtonGroup radioButtonGroup;

        private bool selected;

        public bool Selected
        {
            get { return selected; }
            set 
            { 
                if (value == selected) return;
                if (radioButtonGroup != null && radioButtonGroup.SelectedRadioButton == this)
                {
                    selected = true;
                    return;
                }
                
                selected = value;
                state = (selected) ? ComponentState.Selected : ComponentState.None;
                box.State = state;
                if (value && radioButtonGroup != null)
                {
                    radioButtonGroup.SelectRadioButton(this);
                }

                OnSelected?.Invoke(this);
            }
        }

        private Color? defaultTextColor;

        public override bool Enabled
        {
            get
            {
                return enabled;
            }

            set
            {
                if (value == enabled) { return; }
                enabled = value;
                if (color.A == 0)
                {
                    if (defaultTextColor == null) { defaultTextColor = TextBlock.TextColor; }
                    TextBlock.TextColor = enabled ? defaultTextColor.Value : defaultTextColor.Value * 0.5f;
                }
            }
        }

        public Color TextColor
        {
            get { return text.TextColor; }
            set { text.TextColor = value; }
        }

        /*public override Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused) return Rectangle.Empty;
                Rectangle union = Rectangle.Union(box.Rect, TextBlock.Rect);
                Vector2 textPos = TextBlock.Rect.Location.ToVector2() + TextBlock.TextPos + TextBlock.TextOffset;
                Vector2 textSize = TextBlock.Font.MeasureString(TextBlock.Text);
                union = Rectangle.Union(union, new Rectangle(textPos.ToPoint(), textSize.ToPoint()));
                union = Rectangle.Union(union, Rect);
                return ClampMouseRectToParent ? ClampRect(union) : union;
            }
        }*/

        public override ScalableFont Font
        {
            get
            {
                return base.Font;
            }

            set
            {
                base.Font = value;
                if (text != null) text.Font = value;
            }
        }

        public GUIFrame Box
        {
            get { return box; }
        }

        public GUITextBlock TextBlock
        {
            get { return text; }
        }

        public override string ToolTip
        {
            get { return base.ToolTip; }
            set
            {
                base.ToolTip = value;
                box.ToolTip = value;
                text.ToolTip = value;
            }
        }

        public string Text
        {
            get { return text.Text; }
            set { text.Text = value; }
        }

        public Color? DefaultTextColor
        {
            get { return defaultTextColor; }
        }

        public GUITickBox(RectTransform rectT, string label, ScalableFont font = null, string style = "") : base(null, rectT)
        {
            CanBeFocused = true;

            layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, rectT), true);

            box = new GUIFrame(new RectTransform(Vector2.One, layoutGroup.RectTransform, scaleBasis: ScaleBasis.BothHeight)
            {
                IsFixedSize = false
            }, string.Empty, Color.DarkGray)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            GUI.Style.Apply(box, style == "" ? "GUITickBox" : style);
            Vector2 textBlockScale = new Vector2((float)(Rect.Width - Rect.Height) / (float)Math.Max(Rect.Width, 1.0), 1.0f);
            text = new GUITextBlock(new RectTransform(textBlockScale, layoutGroup.RectTransform), label, font: font, textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };
            GUI.Style.Apply(text, "GUIButtonHorizontal", this);
            Enabled = true;

            ResizeBox();

            rectT.ScaleChanged += ResizeBox;
            rectT.SizeChanged += ResizeBox;
        }
        
        public void SetRadioButtonGroup(GUIRadioButtonGroup rbg)
        {
            radioButtonGroup = rbg;
        }

        private void ResizeBox()
        {
            Vector2 textBlockScale = new Vector2(Math.Max(Rect.Width - box.Rect.Width, 0.0f) / Math.Max(Rect.Width, 1.0f), 1.0f);
            text.RectTransform.RelativeSize = textBlockScale;
            text.SetTextPos();
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (GUI.MouseOn == this && Enabled)
            {
                box.State = ComponentState.Hover;

                if (PlayerInput.LeftButtonHeld())
                {
                    box.State = ComponentState.Selected;                    
                }

                if (PlayerInput.LeftButtonClicked())
                {
                    if (radioButtonGroup == null)
                    {
                        Selected = !Selected;
                    }
                    else if (!selected)
                    {
                        Selected = true;
                    }
                }
            }
            else
            {
                box.State = ComponentState.None;
            }

            if (selected)
            {
                box.State = ComponentState.Selected;
            }
        }
    }
}
