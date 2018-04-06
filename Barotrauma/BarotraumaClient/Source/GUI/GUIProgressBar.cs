using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIProgressBar : GUIComponent
    {
        private bool isHorizontal;

        private GUIFrame frame, slider;
        private float barSize;
                
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
                if (barSize != oldBarSize) UpdateRect();
            }
        }

        public GUIProgressBar(Rectangle rect, Color color, float barSize, GUIComponent parent = null)
            : this(rect, color, barSize, (Alignment.Left | Alignment.Top), parent)
        {
        }

        public GUIProgressBar(Rectangle rect, Color color, float barSize, Alignment alignment, GUIComponent parent = null)
            : this(rect, color, null, barSize, alignment, parent)
        {

        }

        public GUIProgressBar(Rectangle rect, Color color, string style, float barSize, Alignment alignment, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            this.color = color;
            isHorizontal = (rect.Width > rect.Height);

            this.alignment = alignment;
            
            if (parent != null)
                parent.AddChild(this);

            frame = new GUIFrame(new Rectangle(0, 0, 0, 0), null, this);
            GUI.Style.Apply(frame, "", this);

            slider = new GUIFrame(new Rectangle(0, 0, 0, 0), null);
            GUI.Style.Apply(slider, "Slider", this);

            this.barSize = barSize;
            UpdateRect();
        }

        /*public override void ApplyStyle(GUIComponentStyle style)
        {
            if (frame == null) return;

            frame.Color = style.Color;
            frame.HoverColor = style.HoverColor;
            frame.SelectedColor = style.SelectedColor;

            Padding = style.Padding;

            frame.OutlineColor = style.OutlineColor;

            this.style = style;
        }*/

        private void UpdateRect()
        {
            slider.Rect = new Rectangle(
                (int)(frame.Rect.X + padding.X),
                (int)(frame.Rect.Y + padding.Y),
                isHorizontal ? (int)((frame.Rect.Width - padding.X - padding.Z) * barSize) : frame.Rect.Width,
                isHorizontal ? (int)(frame.Rect.Height - padding.Y - padding.W) : (int)(frame.Rect.Height * barSize));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            if (ProgressGetter != null) BarSize = ProgressGetter();

            DrawChildren(spriteBatch);

            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (slider.sprites != null && slider.sprites[state].Count > 0)
            {
                foreach (UISprite uiSprite in slider.sprites[state])
                {
                    if (uiSprite.Tile)
                    {
                        uiSprite.Sprite.DrawTiled(spriteBatch, slider.Rect.Location.ToVector2(), slider.Rect.Size.ToVector2(), color: currColor);
                    }
                    else
                    {
                        spriteBatch.Draw(uiSprite.Sprite.Texture,
                            slider.Rect, new Rectangle(
                                uiSprite.Sprite.SourceRect.X, 
                                uiSprite.Sprite.SourceRect.Y, 
                                (int)(uiSprite.Sprite.SourceRect.Width * (isHorizontal ? barSize : 1.0f)),
                                (int)(uiSprite.Sprite.SourceRect.Height * (isHorizontal ? 1.0f : barSize))), 
                            currColor);
                    }
                }
            }
        }

    }
}
