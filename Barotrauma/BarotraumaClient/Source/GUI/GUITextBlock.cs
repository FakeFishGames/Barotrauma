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

        public string Text
        {
            get { return text; }
            set
            {
                if (Text == value) return;

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

        public Vector2 Origin
        {
            get { return origin; }
        }

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; }
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
                rectT.Resize(new Point(rectT.Rect.Width, (int)Font.MeasureString(wrappedText).Y));
            }
            SetTextPos();

            RectTransform.ScaleChanged += SetTextPos;
            RectTransform.SizeChanged += SetTextPos;
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

            Vector2 size = MeasureText(text);           

            if (Wrap && rect.Width > 0)
            {
                wrappedText = ToolBox.WrapText(text, rect.Width - padding.X - padding.Z, Font, textScale);
                size = MeasureText(wrappedText);
            }
            else if (OverflowClip)
            {
                overflowClipActive = size.X > rect.Width - padding.X - padding.Z;
            }
                     
            textPos = new Vector2(rect.Width / 2.0f, rect.Height / 2.0f);
            origin = size * 0.5f;

            if (textAlignment.HasFlag(Alignment.Left) && !overflowClipActive)
                origin.X += (rect.Width / 2.0f - padding.X) - size.X / 2;
            
            if (textAlignment.HasFlag(Alignment.Right) || overflowClipActive)
                origin.X -= (rect.Width / 2.0f - padding.Z) - size.X / 2;

            if (textAlignment.HasFlag(Alignment.Top))
                origin.Y += (rect.Height / 2.0f - padding.Y) - size.Y / 2;

            if (textAlignment.HasFlag(Alignment.Bottom))
                origin.Y -= (rect.Height / 2.0f - padding.W) - size.Y / 2;
            
            origin.X = (int)origin.X;
            origin.Y = (int)origin.Y;

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
                Rectangle scissorRect = new Rectangle(rect.X + (int)padding.X, rect.Y, rect.Width - (int)padding.X - (int)padding.Z, rect.Height);
                spriteBatch.GraphicsDevice.ScissorRectangle = scissorRect;
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
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            }

            if (OutlineColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (currColor.A / 255.0f), false);
        }
    }
}
