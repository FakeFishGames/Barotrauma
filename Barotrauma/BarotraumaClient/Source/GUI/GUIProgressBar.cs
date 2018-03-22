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

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;
                UpdateRect();
            }
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
            if (IsHorizontal)
            {
                slider.Rect = new Rectangle(
                    (int)(frame.Rect.X + padding.X),
                    (int)(frame.Rect.Y + padding.Y),
                    (int)((frame.Rect.Width - padding.X - padding.Z) * barSize),
                    (int)(frame.Rect.Height - padding.Y - padding.W));
            }
            else
            {
                slider.Rect = new Rectangle(
                    (int)(frame.Rect.X + padding.X),
                    (int)(frame.Rect.Y + padding.Y + (frame.Rect.Height * (1.0f - barSize))),
                    (int)(frame.Rect.Width - padding.X - padding.Z),
                    (int)(frame.Rect.Height * barSize));
            }
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
                        uiSprite.Sprite.DrawTiled(spriteBatch, slider.Rect.Location.ToVector2(), slider.Rect.Size.ToVector2(), currColor);
                    }
                    else if (uiSprite.Slice)
                    {
                        Vector2 pos = new Vector2(slider.Rect.X, slider.Rect.Y);

                        int centerWidth = System.Math.Max(slider.Rect.Width - uiSprite.Slices[0].Width - uiSprite.Slices[2].Width, 0);
                        int centerHeight = System.Math.Max(slider.Rect.Height - uiSprite.Slices[0].Height - uiSprite.Slices[8].Height, 0);

                        Vector2 scale = new Vector2(
                            MathHelper.Clamp((float)slider.Rect.Width / (uiSprite.Slices[0].Width + uiSprite.Slices[2].Width), 0, 1),
                            MathHelper.Clamp((float)slider.Rect.Height / (uiSprite.Slices[0].Height + uiSprite.Slices[6].Height), 0, 1));

                        for (int x = 0; x < 3; x++)
                        {
                            float width = (x == 1 ? centerWidth : uiSprite.Slices[x].Width) * scale.X;
                            for (int y = 0; y < 3; y++)
                            {
                                float height = (y == 1 ? centerHeight : uiSprite.Slices[x + y * 3].Height) * scale.Y;

                                spriteBatch.Draw(uiSprite.Sprite.Texture,
                                    new Rectangle((int)pos.X, (int)pos.Y, (int)width, (int)height),
                                    uiSprite.Slices[x + y * 3],
                                    currColor * (currColor.A / 255.0f));
                                
                                pos.Y += height;
                            }
                            pos.X += width;
                            pos.Y = slider.Rect.Y;
                        }                        
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
