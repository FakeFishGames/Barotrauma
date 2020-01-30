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

        public override bool Selected
        {
            get { return selected; }
            set 
            {
                if (value == selected) { return; } 
                if (radioButtonGroup != null && radioButtonGroup.SelectedRadioButton == this)
                {
                    selected = true;
                    return;
                }
                
                selected = value;
                State = selected ? ComponentState.Selected : ComponentState.None;
                if (value && radioButtonGroup != null)
                {
                    radioButtonGroup.SelectRadioButton(this);
                }

                OnSelected?.Invoke(this);
            }
        }

        public override ComponentState State 
        {
            get 
            { 
                return base.State; 
            }
            set
            {
                base.State = value;
                box.State = TextBlock.State = value;
            }
        }

        public override bool Enabled
        {
            get
            {
                return enabled;
            }

            set
            {
                if (value == enabled) { return; }
                enabled = box.Enabled = TextBlock.Enabled = value;
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

        public GUITickBox(RectTransform rectT, string label, ScalableFont font = null, string style = "") : base(null, rectT)
        {
            CanBeFocused = true;
            HoverCursor = CursorState.Hand;

            layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, rectT), true);

            box = new GUIFrame(new RectTransform(Vector2.One, layoutGroup.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight)
            {
                IsFixedSize = true
            }, string.Empty, Color.DarkGray)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            GUI.Style.Apply(box, style == "" ? "GUITickBox" : style);
            if (box.RectTransform.MinSize.Y > 0)
            {
                RectTransform.MinSize = box.RectTransform.MinSize;
                RectTransform.MaxSize = box.RectTransform.MaxSize;
                RectTransform.Resize(new Point(RectTransform.NonScaledSize.X, RectTransform.MinSize.Y));
                box.RectTransform.MinSize = new Point(box.RectTransform.MinSize.Y);
                box.RectTransform.Resize(box.RectTransform.MinSize);
            }
            Vector2 textBlockScale = new Vector2((float)(Rect.Width - Rect.Height) / (float)Math.Max(Rect.Width, 1.0), 1.0f);
            text = new GUITextBlock(new RectTransform(textBlockScale, layoutGroup.RectTransform, Anchor.CenterLeft), label, font: font, textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };
            GUI.Style.Apply(text, "GUITextBlock", this);
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
            box.RectTransform.MinSize = new Point(Rect.Height);
            box.RectTransform.Resize(box.RectTransform.MinSize);
            text.SetTextPos();
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) { return; }

            base.Update(deltaTime);

            if (GUI.MouseOn == this && Enabled)
            {
                State = Selected ?
                    ComponentState.HoverSelected :
                    ComponentState.Hover;

                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    State = ComponentState.Selected;                    
                }

                if (PlayerInput.PrimaryMouseButtonClicked())
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
            else if (selected)
            {
                State = ComponentState.Selected;
            }
            else
            {
                State = ComponentState.None;
            }
        }
    }
}
