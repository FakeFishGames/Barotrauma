using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUITextBlock : GUIComponent
    {
        protected string text;

        protected Alignment textAlignment;

        protected Vector2 textPos;
        protected Vector2 origin;

        protected Vector2 caretPos;

        protected Color textColor;

        private string wrappedText;

        public delegate string TextGetterHandler();
        public TextGetterHandler TextGetter;

        public bool Wrap;

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

                rect = value;
                SetTextPos();
            }
        }

        public float TextDepth
        {
            get { return textDepth; }
            set { textDepth = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public bool LimitText
        {
            get;
            set;
        }

        public Vector2 TextPos
        {
            get { return textPos; }
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

        public GUITextBlock(Rectangle rect, string text, GUIStyle style, GUIComponent parent, SpriteFont font)
            : this(rect, text, style, Alignment.TopLeft, Alignment.TopLeft, parent, false, font)
        {
        }


        public GUITextBlock(Rectangle rect, string text, GUIStyle style, GUIComponent parent = null, bool wrap = false)
            : this(rect, text, style, Alignment.TopLeft, Alignment.TopLeft, parent, wrap)
        {
        }

        public GUITextBlock(Rectangle rect, string text, Color? color, Color? textColor, Alignment textAlignment = Alignment.Left, GUIStyle style = null, GUIComponent parent = null, bool wrap = false)
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
            base.ApplyStyle(style);

            textColor = style.textColor;
        }


        public GUITextBlock(Rectangle rect, string text, Color? color, Color? textColor, Alignment alignment, Alignment textAlignment = Alignment.Left, GUIStyle style = null, GUIComponent parent = null, bool wrap = false)
            : this (rect, text, style, alignment, textAlignment, parent, wrap, null)
        {
            if (color != null) this.color = (Color)color;
            if (textColor != null) this.textColor = (Color)textColor;
        }

        public GUITextBlock(Rectangle rect, string text, GUIStyle style, Alignment alignment = Alignment.TopLeft, Alignment textAlignment = Alignment.TopLeft, GUIComponent parent = null, bool wrap = false, SpriteFont font = null)
            :base (style)        
        {
            this.Font = font == null ? GUI.Font : font;

            this.rect = rect;

            this.text = text;

            this.alignment = alignment;

            this.textAlignment = textAlignment;
            
            if (parent != null)
                parent.AddChild(this);

            this.Wrap = wrap;

            SetTextPos();

            if (rect.Height == 0 && !string.IsNullOrEmpty(Text))
            {
                this.rect.Height = (int)Font.MeasureString(wrappedText).Y;
            }
        }

        public void SetTextPos()
        {
            if (text==null) return;

            wrappedText = text;

           Vector2 size = MeasureText(text);
            
            if (Wrap && rect.Width>0)
            {
                wrappedText = ToolBox.WrapText(text, rect.Width - padding.X - padding.Z, Font);

                Vector2 newSize = MeasureText(wrappedText);

                size = newSize;
            }

            if (LimitText && text.Length>1 && size.Y > rect.Height)
            {
                string[] lines = text.Split('\n');
                text = string.Join("\n", lines, 0, lines.Length-1);
            }
         
            textPos = new Vector2(rect.Width / 2.0f, rect.Height / 2.0f);
            origin = size * 0.5f;

            if (textAlignment.HasFlag(Alignment.Left))
                origin.X += (rect.Width / 2.0f - padding.X) - size.X / 2;
            
            if (textAlignment.HasFlag(Alignment.Right))
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
            if (Font==null) return Vector2.Zero;

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

            if (currColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);

            base.Draw(spriteBatch);

            if (TextGetter != null) text = TextGetter();

            if (!string.IsNullOrEmpty(text))
            {
                spriteBatch.DrawString(Font,
                    Wrap ? wrappedText : text,
                    new Vector2(rect.X, rect.Y) + textPos + offset,
                    textColor * (textColor.A / 255.0f),
                    0.0f, origin, 1.0f,
                    SpriteEffects.None, textDepth);
            }

            DrawChildren(spriteBatch);

            if (OutlineColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (currColor.A / 255.0f), false);
        }
    }
}
