using System;
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
                
        public override bool Enabled 
        { 
            get
            {
                return enabled;
            }

            set
            {
                if (value == enabled) { return; }
                enabled = frame.Enabled = textBlock.Enabled = value;
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

        public Color HoverTextColor
        {
            get { return textBlock.HoverTextColor; }
            set { textBlock.HoverTextColor = value; }
        }

        public override float FlashTimer
        {
            get { return Frame.FlashTimer; }
        }

        public override ScalableFont Font
        {
            get
            {
                return (textBlock == null) ? GUI.Font : textBlock.Font;
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

        public bool Pulse { get; set; }
        private float pulseTimer;
        private float pulseExpand;
        private bool flashed;
        
        public GUIButton(RectTransform rectT, string text = "", Alignment textAlignment = Alignment.Center, string style = "", Color? color = null) : base(style, rectT)
        {
            CanBeFocused = true;
            HoverCursor = CursorState.Hand;

            frame = new GUIFrame(new RectTransform(Vector2.One, rectT), style) { CanBeFocused = false };
            if (style != null) { GUI.Style.Apply(frame, style == "" ? "GUIButton" : style); }
            if (color.HasValue)
            {
                this.color = frame.Color = color.Value;
            }
            textBlock = new GUITextBlock(new RectTransform(Vector2.One, rectT, Anchor.Center), text, textAlignment: textAlignment, style: null)
            {
                TextColor = this.style == null ? Color.Black : this.style.TextColor,
                HoverTextColor = this.style == null ? Color.Black : this.style.HoverTextColor,
                SelectedTextColor = this.style == null ? Color.Black : this.style.SelectedTextColor,
                CanBeFocused = false
            };
            if (rectT.Rect.Height == 0 && !string.IsNullOrEmpty(text))
            {
                RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(textBlock.Text).Y));
                RectTransform.MinSize = textBlock.RectTransform.MinSize = new Point(0, System.Math.Max(rectT.MinSize.Y, Rect.Height));
                TextBlock.SetTextPos();
            }
            GUI.Style.Apply(textBlock, "", this);

            //if the text is in chinese/korean/japanese and we're not using a CJK-compatible font,
            //use the default CJK font as a fallback
            if (TextManager.IsCJK(textBlock.Text) && !textBlock.Font.IsCJK)
            {
                textBlock.Font = GUI.CJKFont;
            }

            Enabled = true;
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            base.ApplyStyle(style);

            if (frame != null) { frame.ApplyStyle(style); }
        }

        public override void Flash(Color? color = null, float flashDuration = 1.5f, bool useRectangleFlash = false, bool useCircularFlash = false, Vector2? flashRectInflate = null)
        {
            Frame.Flash(color, flashDuration, useRectangleFlash, useCircularFlash, flashRectInflate);
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (Pulse && pulseTimer > 1.0f)
            {
                Rectangle expandRect = Rect;
                float expand = (pulseExpand * 20.0f) * GUI.Scale;
                expandRect.Inflate(expand, expand);
                
                GUI.Style.ButtonPulse.Draw(spriteBatch, expandRect, ToolBox.GradientLerp(pulseExpand, Color.White, Color.White, Color.Transparent));
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;
            base.Update(deltaTime);
            if (Rect.Contains(PlayerInput.MousePosition) && CanBeSelected && CanBeFocused && Enabled && GUI.IsMouseOn(this))
            {
                State = Selected ?
                    ComponentState.HoverSelected :
                    ComponentState.Hover;
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
                            State = ComponentState.Pressed;
                        }
                    }
                    else
                    {
                        State = ComponentState.Pressed;
                    }
                }
                else if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    GUI.PlayUISound(GUISoundType.Click);
                    if (OnClicked != null)
                    {
                        if (OnClicked(this, UserData))
                        {
                            State = ComponentState.Selected;
                        }
                    }
                    else
                    {
                        Selected = !Selected;
                    }  
                }
            }
            else
            {
                if (!ExternalHighlight)
                {
                    State = Selected ? ComponentState.Selected : ComponentState.None;
                }
                else
                {
                    State = ComponentState.Hover;
                }
            }

            foreach (GUIComponent child in Children)
            {
                child.State = State;
            }

            if (Pulse)
            {
                pulseTimer += deltaTime;
                if (pulseTimer > 1.0f)
                {
                    if (!flashed)
                    {
                        flashed = true;
                        Frame.Flash(Color.White * 0.2f, 0.8f, true);
                    }

                    pulseExpand += deltaTime;
                    if (pulseExpand > 1.0f)
                    {
                        pulseTimer = 0.0f;
                        pulseExpand = 0.0f;
                        flashed = false;
                    }
                }
            }
        }
    }
}
