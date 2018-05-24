using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUIImage : GUIComponent
    {
        public float Rotation;

        private Sprite sprite;

        private Rectangle sourceRect;

        private bool crop;

        private bool scaleToFit;
        
        public bool Crop
        {
            get
            { 
                return crop;
            }
            set
            {
                crop = value;
                if (crop)
                {                                
                    sourceRect.Width = Math.Min(sprite.SourceRect.Width, Rect.Width);
                    sourceRect.Height = Math.Min(sprite.SourceRect.Height, Rect.Height);
                }
            }
        }

        public float Scale
        {
            get;
            set;
        }

        public Rectangle SourceRect
        {
            get { return sourceRect; }
            set { sourceRect = value; }
        }

        public Sprite Sprite
        {
            get { return sprite; }
            set
            {
                if (sprite == value) return;
                sprite = value;
                sourceRect = sprite.SourceRect;
                if (scaleToFit)
                {
                    Scale = sprite.SourceRect.Width == 0 || sprite.SourceRect.Height == 0 ?
                        1.0f :
                        Math.Min(RectTransform.Rect.Width / (float)sprite.SourceRect.Width, RectTransform.Rect.Height / (float)sprite.SourceRect.Height);
                }
            }
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIImage(Rectangle rect, string spritePath, Alignment alignment, GUIComponent parent = null)
            : this(rect, new Sprite(spritePath, Vector2.Zero), alignment, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIImage(Rectangle rect, Sprite sprite, Alignment alignment, GUIComponent parent = null)
            : this(rect, sprite == null ? Rectangle.Empty : sprite.SourceRect, sprite, alignment, 1.0f, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIImage(Rectangle rect, Rectangle sourceRect, Sprite sprite, Alignment alignment, float spriteScale, GUIComponent parent = null)
            : base(null)
        {
            this.rect = rect;
            this.alignment = alignment;
            this.sprite = sprite;

            color = Color.White;            
            Scale = spriteScale;

            if (rect.Width == 0) this.rect.Width = (int)(sprite.size.X * spriteScale);
            if (rect.Height == 0) this.rect.Height = (int)(Math.Min(sprite.size.Y, sprite.size.Y * (this.rect.Width / sprite.size.X)) * spriteScale);

            this.sourceRect = sourceRect;

            if (parent != null) parent.AddChild(this);
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIImage(RectTransform rectT, Sprite sprite, Rectangle? sourceRect = null, bool scaleToFit = false) : base(null, rectT)
        {
            this.scaleToFit = scaleToFit;
            Sprite = sprite;
            if (sourceRect.HasValue)
            {
                this.sourceRect = sourceRect.Value;
            }
            else
            {
                this.sourceRect = sprite == null ? Rectangle.Empty : sprite.SourceRect;
            }
            color = Color.White;
            hoverColor = Color.White;
            selectedColor = Color.White;
            if (!scaleToFit)
            {
                Scale = 1.0f;
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Color currColor = color;
            if (state == ComponentState.Hover) currColor = hoverColor;
            if (state == ComponentState.Selected) currColor = selectedColor;

            if (sprite != null && sprite.Texture != null)
            {
                spriteBatch.Draw(sprite.Texture, Rect.Center.ToVector2(), sourceRect, currColor * (currColor.A / 255.0f), Rotation, sprite.size / 2,
                    Scale, SpriteEffects.None, 0.0f);
            }          
        }
    }
}
