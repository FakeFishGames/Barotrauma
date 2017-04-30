using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUITextBlock : GUIComponent
    {
        protected string text;

        protected Alignment textAlignment;

        private float textScale;

        protected Vector2 textPos;
        protected Vector2 origin;

        protected Vector2 caretPos;

        protected Color textColor;

        private string wrappedText;

        public delegate string TextGetterHandler();
        public TextGetterHandler TextGetter;

        public bool Wrap;

        private bool overflowClipActive;
        public bool OverflowClip;

        private float textDepth;

        public override Vector4 Padding
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

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                if (base.Rect == value) return;
                foreach (GUIComponent child in children)
                {
                    child.Rect = new Rectangle(child.Rect.X + value.X - rect.X, child.Rect.Y + value.Y - rect.Y, child.Rect.Width, child.Rect.Height);
                }

                if (value.Width != rect.Width || value.Height != rect.Height)
                {
                    SetTextPos();
                }

                rect = value;
            }
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

        public Vector2 CaretPos
        {
            get { return caretPos; }
        }
        
        public GUITextBlock(Rectangle rect, string text, string style, GUIComponent parent, ScalableFont font)
            : this(rect, text, style, Alignment.TopLeft, Alignment.TopLeft, parent, false, font)
        {
        }


        public GUITextBlock(Rectangle rect, string text, string style, GUIComponent parent = null, bool wrap = false)
            : this(rect, text, style, Alignment.TopLeft, Alignment.TopLeft, parent, wrap)
        {
        }

        public GUITextBlock(Rectangle rect, string text, Color? color, Color? textColor, Alignment textAlignment = Alignment.Left, string style = null, GUIComponent parent = null, bool wrap = false)
            : this(rect, text,color, textColor, Alignment.TopLeft, textAlignment, style, parent, wrap)
        {
        }

        protected override void UpdateDimensions(GUIComponent parent = null)
        {
            base.UpdateDimensions(parent);

            SetTextPos();
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            if (style == null) return;
            base.ApplyStyle(style);

            textColor = style.textColor;
        }


        public GUITextBlock(Rectangle rect, string text, Color? color, Color? textColor, Alignment alignment, Alignment textAlignment = Alignment.Left, string style = null, GUIComponent parent = null, bool wrap = false, ScalableFont font = null)
            : this (rect, text, style, alignment, textAlignment, parent, wrap, font)
        {
            if (color != null) this.color = (Color)color;
            if (textColor != null) this.textColor = (Color)textColor;
        }

        public GUITextBlock(Rectangle rect, string text, string style, Alignment alignment = Alignment.TopLeft, Alignment textAlignment = Alignment.TopLeft, GUIComponent parent = null, bool wrap = false, ScalableFont font = null)
            : base(style)        
        {
            this.Font = font == null ? GUI.Font : font;

            this.rect = rect;

            this.text = text;

            this.alignment = alignment;

            this.padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);

            this.textAlignment = textAlignment;
            
            if (parent != null)
                parent.AddChild(this);

            this.Wrap = wrap;

            SetTextPos();

            TextScale = 1.0f;

            if (rect.Height == 0 && !string.IsNullOrEmpty(Text))
            {
                this.rect.Height = (int)Font.MeasureString(wrappedText).Y;
            }
        }

        public void SetTextPos()
        {
            if (text == null) return;

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
                overflowClipActive = size.X > rect.Width;
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

            if (wrappedText.Contains("\n"))
            {
                string[] lines = wrappedText.Split('\n');
                Vector2 lastLineSize = MeasureText(lines[lines.Length-1]);
                caretPos = new Vector2(rect.X + lastLineSize.X, rect.Y + size.Y - lastLineSize.Y) + textPos - origin;
            }
            else
            {
                caretPos = new Vector2(rect.X + size.X, rect.Y) + textPos - origin;
            }

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

        public override void Draw(SpriteBatch spriteBatch)
        {
            Draw(spriteBatch, Vector2.Zero);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 offset)
        {
            if (!Visible) return;

            Color currColor = color;
            if (state == ComponentState.Hover) currColor = hoverColor;
            if (state == ComponentState.Selected) currColor = selectedColor;

            Rectangle drawRect = rect;
            if (offset != Vector2.Zero) drawRect.Location += offset.ToPoint();
            
            base.Draw(spriteBatch);

            if (TextGetter != null) text = TextGetter();

            if (overflowClipActive) GameMain.CurrGraphicsDevice.ScissorRectangle = rect;

            if (!string.IsNullOrEmpty(text))
            {
                Font.DrawString(spriteBatch,
                    Wrap ? wrappedText : text,
                    new Vector2(rect.X, rect.Y) + textPos + offset,
                    textColor * (textColor.A / 255.0f),
                    0.0f, origin, TextScale,
                    SpriteEffects.None, textDepth);
            }

            if (overflowClipActive) GameMain.CurrGraphicsDevice.ScissorRectangle = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            DrawChildren(spriteBatch);

            if (OutlineColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (currColor.A / 255.0f), false);
        }
    }
}
