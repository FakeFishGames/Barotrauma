using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIButton : GUIComponent
    {
        protected GUITextBlock textBlock;
        public GUITextBlock TextBlock { get { return textBlock; } }
        protected GUIFrame frame;
        public GUIFrame Frame { get { return frame; } }

        public delegate bool OnClickedHandler(GUIButton button, object obj);
        public OnClickedHandler OnClicked;

        public delegate bool OnPressedHandler();
        public OnPressedHandler OnPressed;

        public delegate bool OnButtonDownHandler();
        public OnButtonDownHandler OnButtonDown;

        public bool CanBeSelected = true;

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
                frame.Color = enabled ? color : Color.Gray * 0.7f;
            }
        }

        public override Color Color
        {
            get { return base.Color; }
            set
            {
                base.Color = value;
                frame.Color = value;
            }
        }

        public override Color HoverColor
        {
            get { return base.HoverColor; }
            set
            {
                base.HoverColor = value;
                frame.HoverColor = value;
            }
        }

        public override Color SelectedColor
        {
            get
            {
                return base.SelectedColor;
            }
            set
            {
                base.SelectedColor = value;
                frame.SelectedColor = value;
            }
        }

        public override Color PressedColor
        {
            get
            {
                return base.PressedColor;
            }
            set
            {
                base.PressedColor = value;
                frame.PressedColor = value;
            }
        }

        public override Color OutlineColor
        {
            get { return base.OutlineColor; }
            set
            {
                base.OutlineColor = value;
                if (frame != null) frame.OutlineColor = value;
            }
        }

        public Color TextColor
        {
            get { return textBlock.TextColor; }
            set { textBlock.TextColor = value; }
        }


        public override float FlashTimer
        {
            get { return Frame.FlashTimer; }
        }

        public override ScalableFont Font
        {
            get
            {
                return (textBlock==null) ? GUI.Font : textBlock.Font;
            }
            set
            {
                base.Font = value;
                if (textBlock != null) textBlock.Font = value;                
            }
        }
        
        public string Text
        {
            get { return textBlock.Text; }
            set { textBlock.Text = value; }
        }

        public bool ForceUpperCase
        {
            get { return textBlock.ForceUpperCase; }
            set { textBlock.ForceUpperCase = value; }
        }

        public override string ToolTip
        {
            get
            {
                return base.ToolTip;
            }
            set
            {
                base.ToolTip = value;
                textBlock.ToolTip = value;                
            }
        }
        
        public bool Selected { get; set; }
        
        public GUIButton(RectTransform rectT, string text = "", Alignment textAlignment = Alignment.Center, string style = "", Color? color = null, ScalableFont font = null) : base(style, rectT)
        {
            CanBeFocused = true;
            HoverCursor = CursorState.Hand;

            if (color.HasValue)
            {
                this.color = color.Value;
            }
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT), style) { CanBeFocused = false };
            if (style != null) GUI.Style.Apply(frame, style == "" ? "GUIButton" : style);
            textBlock = new GUITextBlock(new RectTransform(Vector2.One, rectT), text, textAlignment: textAlignment, style: null, font: font)
            {
                TextColor = this.style == null ? Color.Black : this.style.textColor,
                CanBeFocused = false
            };
            if (rectT.Rect.Height == 0 && !string.IsNullOrEmpty(text))
            {
                RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(textBlock.Text).Y));
                RectTransform.MinSize = textBlock.RectTransform.MinSize = new Point(0, Rect.Height);
                TextBlock.SetTextPos();
            }
            GUI.Style.Apply(textBlock, "", this);
            Enabled = true;
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            base.ApplyStyle(style);

            if (frame != null) frame.ApplyStyle(style);
        }

        public override void Flash(Color? color = null, float flashDuration = 1.5f, bool useRectangleFlash = false, Vector2? flashRectInflate = null)
        {
            Frame.Flash(color, flashDuration, useRectangleFlash, flashRectInflate);
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            //do nothing
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;
            base.Update(deltaTime);
            if (Rect.Contains(PlayerInput.MousePosition) && CanBeSelected && CanBeFocused && Enabled && GUI.IsMouseOn(this))
            {
                state = ComponentState.Hover;
                if (PlayerInput.PrimaryMouseButtonDown())
                {
                    OnButtonDown?.Invoke();
                }
                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (OnPressed != null)
                    {
                        if (OnPressed())
                        {
                            state = ComponentState.Pressed;
                        }
                    }
                    else
                    {
                        state = ComponentState.Pressed;
                    }
                }
                else if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    GUI.PlayUISound(GUISoundType.Click);
                    if (OnClicked != null)
                    {
                        if (OnClicked(this, UserData) && CanBeSelected)
                        {
                            state = ComponentState.Selected;
                        }
                    }
                    else
                    {
                        Selected = !Selected;
                        // state = state == ComponentState.Selected ? ComponentState.None : ComponentState.Selected;
                    }
                }
            }
            else
            {
                state = Selected ? ComponentState.Selected : ComponentState.None;
            }

            foreach (GUIComponent child in Children)
            {
                child.State = state;
            }
            //frame.State = state;
        }
    }
}
