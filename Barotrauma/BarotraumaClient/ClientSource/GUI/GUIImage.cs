using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

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
                if (scaleToFit) RecalculateScale();                
            }
        }

        public BlendState BlendState;

        public ComponentState? OverrideState = null;

        public GUIImage(RectTransform rectT, string style, bool scaleToFit = false)
            : this(rectT, null, null, scaleToFit, style)
        {
        }

        public GUIImage(RectTransform rectT, Sprite sprite, Rectangle? sourceRect = null, bool scaleToFit = false) 
            : this(rectT, sprite, sourceRect, scaleToFit, null)
        {
        }

        private GUIImage(RectTransform rectT, Sprite sprite, Rectangle? sourceRect, bool scaleToFit, string style) : base(style, rectT)
        {
            this.scaleToFit = scaleToFit;
            sprite?.EnsureLazyLoaded();
            Sprite = sprite;
            if (sourceRect.HasValue)
            {
                this.sourceRect = sourceRect.Value;
            }
            else
            {
                this.sourceRect = sprite == null ? Rectangle.Empty : sprite.SourceRect;
            }
            if (style == null)
            {
                color = hoverColor = selectedColor = pressedColor = disabledColor = Color.White;                
            }
            if (!scaleToFit)
            {
                Scale = 1.0f;
            }
            else
            {
                rectT.SizeChanged += RecalculateScale;
            }
            Enabled = true;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            if (Parent != null) { State = Parent.State; }
            if (OverrideState != null) { State = OverrideState.Value; }

            Color currentColor = GetColor(State);

            if (BlendState != null)
            {
                spriteBatch.End();
                spriteBatch.Begin(blendState: BlendState, samplerState: GUI.SamplerState);
            }

            if (style != null)
            {
                foreach (UISprite uiSprite in style.Sprites[State])
                {
                    if (Math.Abs(Rotation) > float.Epsilon)
                    {
                        float scale = Math.Min(Rect.Width / uiSprite.Sprite.size.X, Rect.Height / uiSprite.Sprite.size.Y);
                        spriteBatch.Draw(uiSprite.Sprite.Texture, Rect.Center.ToVector2(), uiSprite.Sprite.SourceRect, currentColor * (currentColor.A / 255.0f), Rotation, uiSprite.Sprite.size / 2,
                            Scale * scale, SpriteEffects, 0.0f);
                    }
                    else
                    {
                        uiSprite.Draw(spriteBatch, Rect, currentColor * (currentColor.A / 255.0f), SpriteEffects);
                    }
                }
            }
            else if (sprite?.Texture != null)
            {
                spriteBatch.Draw(sprite.Texture, Rect.Center.ToVector2(), sourceRect, currentColor * (currentColor.A / 255.0f), Rotation, sprite.size / 2,
                    Scale, SpriteEffects, 0.0f);
            }

            if (BlendState != null)
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }
        }

        private void RecalculateScale()
        {
            Scale = sprite == null || sprite.SourceRect.Width == 0 || sprite.SourceRect.Height == 0 ?
                1.0f :
                Math.Min(RectTransform.Rect.Width / (float)sprite.SourceRect.Width, RectTransform.Rect.Height / (float)sprite.SourceRect.Height);
        }
    }
}
