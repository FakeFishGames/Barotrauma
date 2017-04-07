using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIProgressBar : GUIComponent
    {
        private bool isHorizontal;

        private GUIFrame frame;
        private float barSize;

        private int margin;

        public delegate float ProgressGetterHandler();
        public ProgressGetterHandler ProgressGetter;

        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set { isHorizontal = value; }
        }

        public float BarSize
        {
            get { return barSize; }
            set 
            {
                float oldBarSize = barSize;
                barSize = MathHelper.Clamp(value, 0.0f, 1.0f);
                if (barSize!=oldBarSize) UpdateRect();
            }
        }

        public GUIProgressBar(Rectangle rect, Color color, float barSize, GUIComponent parent = null)
            : this(rect,color,barSize, (Alignment.Left | Alignment.Top), parent)
        {
        }

        public GUIProgressBar(Rectangle rect, Color color, float barSize, Alignment alignment, GUIComponent parent = null)
            : this(rect,color,null, barSize,alignment, parent)
        {
            
        }

        public GUIProgressBar(Rectangle rect, Color color, string style, float barSize, Alignment alignment, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            this.color = color;
            isHorizontal = (rect.Width > rect.Height);

            this.alignment = alignment;

            margin = 5;

            if (parent != null)
                parent.AddChild(this);

            frame = new GUIFrame(new Rectangle(0, 0, 0, 0), Color.Black, null, this);

            this.barSize = barSize;
            UpdateRect();
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            if (frame == null) return;

            frame.Color = style.Color;
            frame.HoverColor = style.HoverColor;
            frame.SelectedColor = style.SelectedColor;

            Padding = style.Padding;

            frame.OutlineColor = style.OutlineColor;

            this.style = style;
        }

        private void UpdateRect()
        {
            rect = new Rectangle(
                (int)(frame.Rect.X + padding.X),
                (int)(frame.Rect.Y + padding.Y),
                isHorizontal ? (int)((frame.Rect.Width - padding.X - padding.Z) * barSize) : (frame.Rect.Width - margin * 2),
                isHorizontal ? (int)(frame.Rect.Height - padding.Y - padding.W) : (int)((frame.Rect.Height - margin * 2) * barSize));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            if (ProgressGetter != null) BarSize = ProgressGetter();

            DrawChildren(spriteBatch);

            GUI.DrawRectangle(spriteBatch, rect, color * (color.A / 255.0f), true);
        }

    }
}
