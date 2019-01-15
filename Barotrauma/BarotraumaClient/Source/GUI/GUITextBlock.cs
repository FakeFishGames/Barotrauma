using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUITextBlock : GUIComponent
    {
        protected string text;

        protected Alignment textAlignment;

        private float textScale = 1;

        protected Vector2 textPos;
        protected Vector2 origin;
        
        protected Color textColor;

        private string wrappedText;

        public delegate string TextGetterHandler();
        public TextGetterHandler TextGetter;

        public bool Wrap;

        private bool overflowClipActive;
        public bool OverflowClip;

        private float textDepth;

        public Vector2 TextOffset { get; set; }

        private Vector4 padding;
        public Vector4 Padding
        {
            get { return padding; }
            set 
            { 
                padding = value;
                SetTextPos();
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
                if (base.Font == value) return;
                base.Font = value;
                SetTextPos();
            }
        }

        public string Text
        {
            get { return text; }
            set
            {
                if (Text == value) return;

                //reset scale, it gets recalculated in SetTextPos
                if (autoScale) textScale = 1.0f;

                text = value;
                wrappedText = value;
                SetTextPos();
            }
        }

        public string WrappedText
        {
            get { return wrappedText; }
        }
        
        public float TextDepth
        {
            get { return textDepth; }
            set { textDepth = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }
        
        public Vector2 TextPos
        {
            get { return textPos; }
        }

        public float TextScale
        {
            get { return textScale; }
            set
            {
                if (value != textScale)
                {
                    textScale = value;
                    SetTextPos();
                }
            }
        }

        private bool autoScale;

        /// <summary>
        /// When enabled, the text is automatically scaled down to fit the textblock.
        /// </summary>
        public bool AutoScale
        {
            get { return autoScale; }
            set
            {
                if (autoScale == value) return;
                autoScale = value;
                if (autoScale)
                {
                    SetTextPos();
                }
            }
        }

        public Vector2 Origin
        {
            get { return origin; }
        }

        public Vector2 TextSize
        {
            get;
            private set;
        }

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; }
        }

        public Alignment TextAlignment
        {
            get { return textAlignment; }
            set
            {
                if (textAlignment == value) return;
                textAlignment = value;
                SetTextPos();
            }
        }
                
        /// <summary>
        /// This is the new constructor.
        /// If the rectT height is set 0, the height is calculated from the text.
        /// </summary>
        public GUITextBlock(RectTransform rectT, string text, Color? textColor = null, ScalableFont font = null, 
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null) 
            : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            if (textColor.HasValue)
            {
                this.textColor = textColor.Value;
            }
            this.Font = font ?? GUI.Font;
            this.textAlignment = textAlignment;
            this.Wrap = wrap;
            this.Text = text ?? "";
            if (rectT.Rect.Height == 0 && !string.IsNullOrEmpty(text))
            {
                CalculateHeightFromText();
            }
            SetTextPos();

            RectTransform.ScaleChanged += SetTextPos;
            RectTransform.SizeChanged += SetTextPos;
        }

        public void CalculateHeightFromText()
        {
            if (wrappedText == null) { return; }
            RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(wrappedText).Y));
        }
        
        public override void ApplyStyle(GUIComponentStyle style)
        {
            if (style == null) return;
            base.ApplyStyle(style);
            padding = style.Padding;

            textColor = style.textColor;
        }
        
        public void SetTextPos()
        {
            if (text == null) return;

            var rect = Rect;

            overflowClipActive = false;
            wrappedText = text;
            
            TextSize = MeasureText(text);           

            if (Wrap && rect.Width > 0)
            {
                wrappedText = ToolBox.WrapText(text, rect.Width - padding.X - padding.Z, Font, textScale);
                TextSize = MeasureText(wrappedText);
            }
            else if (OverflowClip)
            {
                overflowClipActive = TextSize.X > rect.Width - padding.X - padding.Z;
            }

            if (autoScale && textScale > 0.1f &&
                (TextSize.X * textScale > rect.Width - padding.X - padding.Z || TextSize.Y * textScale > rect.Height - padding.Y - padding.W))
            {
                TextScale -= 0.05f;
                return;
            }

            textPos = new Vector2(rect.Width / 2.0f, rect.Height / 2.0f);
            origin = TextSize / textScale * 0.5f;

            if (textAlignment.HasFlag(Alignment.Left) && !overflowClipActive)
                origin.X += (rect.Width / 2.0f - TextSize.X / 2) / textScale - padding.X;
            
            if (textAlignment.HasFlag(Alignment.Right) || overflowClipActive)
                origin.X -= (rect.Width / 2.0f - TextSize.X / 2) / textScale - padding.Z;

            if (textAlignment.HasFlag(Alignment.Top))
                origin.Y += (rect.Height / 2.0f - TextSize.Y / 2) / textScale - padding.Y;

            if (textAlignment.HasFlag(Alignment.Bottom))
                origin.Y -= (rect.Height / 2.0f - TextSize.Y / 2) / textScale - padding.W;
            
            origin.X = (int)(origin.X);
            origin.Y = (int)(origin.Y);

            textPos.X = (int)textPos.X;
            textPos.Y = (int)textPos.Y;
        }

        private Vector2 MeasureText(string text) 
        {
            if (Font == null) return Vector2.Zero;

            Vector2 size = Vector2.Zero;
            while (size == Vector2.Zero)
            {
                try { size = Font.MeasureString((text == "") ? " " : text); }
                catch { text = text.Substring(0, text.Length - 1); }
            }

            return size;
        }

        protected override void SetAlpha(float a)
        {
            base.SetAlpha(a);
            textColor = new Color(textColor.R, textColor.G, textColor.B, a);
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Color currColor = GetCurrentColor(state);

            var rect = Rect;

            base.Draw(spriteBatch);

            if (TextGetter != null) Text = TextGetter();

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (overflowClipActive)
            {
                spriteBatch.End();
                Rectangle scissorRect = new Rectangle(rect.X + (int)padding.X, rect.Y, rect.Width - (int)padding.X - (int)padding.Z, rect.Height);
                spriteBatch.GraphicsDevice.ScissorRectangle = scissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            }

            if (!string.IsNullOrEmpty(text))
            {
                Font.DrawString(spriteBatch,
                    Wrap ? wrappedText : text,
                    rect.Location.ToVector2() + textPos + TextOffset,
                    textColor * (textColor.A / 255.0f),
                    0.0f, origin, TextScale,
                    SpriteEffects.None, textDepth);
            }

            if (overflowClipActive)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
            }

            if (OutlineColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (currColor.A / 255.0f), false);
        }
    }
}
