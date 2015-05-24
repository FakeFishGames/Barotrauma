using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    class GUITextBlock : GUIComponent
    {
        protected string text;

        protected Alignment textAlignment;

        protected Vector2 textPos;
        protected Vector2 origin;

        protected Vector2 caretPos;

        protected Color textColor;

        public delegate string TextGetterHandler();
        public TextGetterHandler TextGetter;

        private bool wrap;

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
                SetTextPos();
            }
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
        }

        public Vector2 CaretPos
        {
            get { return caretPos; }
        }

        public GUITextBlock(Rectangle rect, string text, GUIStyle style, Alignment alignment, Alignment textAlignment, GUIComponent parent = null, bool wrap = false)
            : this (rect, text, style.foreGroundColor, style.textColor, alignment, textAlignment, parent, wrap)
        {
            hoverColor = style.hoverColor;
            selectedColor = style.selectedColor;
        }

        public GUITextBlock(Rectangle rect, string text, Color color, Color textColor, Alignment textAlignment = Alignment.Left, GUIComponent parent = null, bool wrap = false)
            : this(rect, text,color, textColor, (Alignment.Left | Alignment.Top), textAlignment, parent, wrap)
        {
        }

        protected override void UpdateDimensions(GUIComponent parent)
        {
            base.UpdateDimensions(parent);

            SetTextPos();
        }


        public GUITextBlock(Rectangle rect, string text, Color color, Color textColor, Alignment alignment, Alignment textAlignment = Alignment.Left, GUIComponent parent = null, bool wrap = false)
        {
            this.rect = rect;

            this.color = color;
            this.textColor = textColor;
            this.text = text;

            this.alignment = alignment;
            
            this.textAlignment = textAlignment;
                      
            if (parent != null)
                parent.AddChild(this);

            //if (wrap)
            //{
                this.wrap = wrap;
            //    this.text = ToolBox.WrapText(this.text, rect.Width);
            //}

            SetTextPos();
        }

        private void SetTextPos()
        {
            if (text==null) return;






           Vector2 size = MeasureText();
            
            if (wrap && rect.Width>0)
            {
                text = text.Replace("\n"," ");
                this.text = ToolBox.WrapText(this.text, rect.Width);

                Vector2 newSize = MeasureText();

                Rectangle newRect = rect;

                //newRect.Width += (int)(newSize.X-size.X);
                newRect.Height += (int)(newSize.Y - size.Y);

                Rect = newRect;
                size = newSize;
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

            caretPos = new Vector2(rect.X + size.X, rect.Y) + textPos - origin;
        }

        private Vector2 MeasureText()
        {
            Vector2 size = Vector2.Zero;
            while (size == Vector2.Zero)
            {
                try { size = GUI.font.MeasureString((text == "") ? " " : text); }
                catch { text = text.Substring(0, text.Length - 1); }
            }

            return size;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            Color currColor = color;
            if (state == ComponentState.Hover) currColor = hoverColor;
            if (state == ComponentState.Selected) currColor = selectedColor;
            
            GUI.DrawRectangle(spriteBatch, rect, currColor*alpha, true);

            if (TextGetter != null) text = TextGetter();

            if (!string.IsNullOrEmpty(text))
            {
                spriteBatch.DrawString(GUI.font,
                    text,
                    new Vector2(rect.X, rect.Y) + textPos,
                    textColor * alpha,
                    0.0f, origin, 1.0f,
                    SpriteEffects.None, 0.0f);
            }

            DrawChildren(spriteBatch);
        }
    }
}
