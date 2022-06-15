using System;
using System.Linq;
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

        public Color SelectedTextColor
        {
            get { return textBlock.SelectedTextColor; }
            set { textBlock.SelectedTextColor = value; }
        }

        public Color DisabledTextColor
        {
            get { return textBlock.DisabledTextColor; }
        }

        public override float FlashTimer
        {
            get { return Frame.FlashTimer; }
        }

        public override GUIFont Font
        {
            get
            {
                return (textBlock == null) ? GUIStyle.Font : textBlock.Font;
            }
            set
            {
                base.Font = value;
                if (textBlock != null) { textBlock.Font = value; }
            }
        }
        
        public LocalizedString Text
        {
            get { return textBlock.Text; }
            set { textBlock.Text = value; }
        }

        public ForceUpperCase ForceUpperCase
        {
            get { return textBlock.ForceUpperCase; }
            set { textBlock.ForceUpperCase = value; }
        }

        public override RichString ToolTip
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

        public GUISoundType ClickSound { get; set; } = GUISoundType.Select;

        public override bool PlaySoundOnSelect { get; set; } = true;

        public GUIButton(RectTransform rectT, Alignment textAlignment = Alignment.Center, string style = "", Color? color = null) : this(rectT, new RawLString(""), textAlignment, style, color) { }

        public GUIButton(RectTransform rectT, LocalizedString text, Alignment textAlignment = Alignment.Center, string style = "", Color? color = null) : base(style, rectT)
        {
            CanBeFocused = true;
            HoverCursor = CursorState.Hand;

            frame = new GUIFrame(new RectTransform(Vector2.One, rectT), style) { CanBeFocused = false };
            if (style != null) { GUIStyle.Apply(frame, style == "" ? "GUIButton" : style); }
            if (color.HasValue)
            {
                this.color = frame.Color = color.Value;
            }

            var selfStyle = Style;
            textBlock = new GUITextBlock(new RectTransform(Vector2.One, rectT, Anchor.Center), text, textAlignment: textAlignment, style: null)
            {
                TextColor = selfStyle?.TextColor ?? Color.Black,
                HoverTextColor = selfStyle?.HoverTextColor ?? Color.Black,
                SelectedTextColor = selfStyle?.SelectedTextColor ?? Color.Black,
                CanBeFocused = false
            };
            if (rectT.Rect.Height == 0 && !text.IsNullOrEmpty())
            {
                RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(textBlock.Text).Y));
                RectTransform.MinSize = textBlock.RectTransform.MinSize = new Point(0, System.Math.Max(rectT.MinSize.Y, Rect.Height));
                TextBlock.SetTextPos();
            }
            GUIStyle.Apply(textBlock, "", this);

            Enabled = true;
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            base.ApplyStyle(style);

            frame?.ApplyStyle(style);
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
                
                GUIStyle.EndRoundButtonPulse.Draw(spriteBatch, expandRect, ToolBox.GradientLerp(pulseExpand, Color.White, Color.White, Color.Transparent));
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
                    if (PlaySoundOnSelect)
                    {
                        SoundPlayer.PlayUISound(ClickSound);
                    }
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
