using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    public class GUITickBox : GUIComponent
    {
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

        public override Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused) return Rectangle.Empty;
                return ClampMouseRectToParent ? ClampRect(box.Rect) : box.Rect;
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

        public GUITickBox(RectTransform rectT, string label, ScalableFont font = null, string style = "") : base(null, rectT)
        {
            box = new GUIFrame(new RectTransform(new Point(rectT.Rect.Height, rectT.Rect.Height), rectT, Anchor.CenterLeft)
            {
                IsFixedSize = false
            }, string.Empty, Color.DarkGray)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            GUI.Style.Apply(box, style == "" ? "GUITickBox" : style);
            text = new GUITextBlock(new RectTransform(Vector2.One, rectT, Anchor.CenterLeft) { AbsoluteOffset = new Point(box.Rect.Width, 0) }, label, font: font, textAlignment: Alignment.CenterLeft);
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
            box.RectTransform.NonScaledSize = new Point(RectTransform.NonScaledSize.Y);
            text.RectTransform.NonScaledSize = new Point(Rect.Width - box.Rect.Width, text.Rect.Height);
            text.RectTransform.AbsoluteOffset = new Point(box.Rect.Width, 0);
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
