using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    public class UISprite
    {
        public Sprite Sprite
        {
            get;
            private set;
        }

        public bool Tile
        {
            get;
            private set;
        }

        public bool Slice
        {
            get;
            set;
        }

        public Rectangle[] Slices
        {
            get;
            set;
        }

        public bool MaintainAspectRatio
        {
            get;
            private set;
        }

        /// <summary>
        /// How much the borders of a sliced sprite are allowed to scale
        /// You may for example want to prevent a 1-pixel border from scaling down (and disappearing) on small resolutions
        /// </summary>
        private float minBorderScale = 0.1f, maxBorderScale = 10.0f;

        public bool CrossFadeIn { get; private set; } = true;
        public bool CrossFadeOut { get; private set; } = true;

        public TransitionMode TransitionMode { get; private set; }

        public UISprite(XElement element)
        {
            Sprite = new Sprite(element);
            MaintainAspectRatio = element.GetAttributeBool("maintainaspectratio", false);
            Tile = element.GetAttributeBool("tile", true);
            CrossFadeIn = element.GetAttributeBool("crossfadein", CrossFadeIn);
            CrossFadeOut = element.GetAttributeBool("crossfadeout", CrossFadeOut);
            string transitionMode = element.GetAttributeString("transition", string.Empty);
            if (Enum.TryParse(transitionMode, ignoreCase: true, out TransitionMode transition))
            {
                TransitionMode = transition;
            }

            Vector4 sliceVec = element.GetAttributeVector4("slice", Vector4.Zero);
            if (sliceVec != Vector4.Zero)
            {
                minBorderScale = element.GetAttributeFloat("minborderscale", 0.1f);
                maxBorderScale = element.GetAttributeFloat("minborderscale", 10.0f);

                Rectangle slice = new Rectangle((int)sliceVec.X, (int)sliceVec.Y, (int)(sliceVec.Z - sliceVec.X), (int)(sliceVec.W - sliceVec.Y));

                Slice = true;
                Slices = new Rectangle[9];

                //top-left
                Slices[0] = new Rectangle(Sprite.SourceRect.Location, slice.Location - Sprite.SourceRect.Location);
                //top-mid
                Slices[1] = new Rectangle(slice.Location.X, Slices[0].Y, slice.Width, Slices[0].Height);
                //top-right
                Slices[2] = new Rectangle(slice.Right, Slices[0].Y, Sprite.SourceRect.Right - slice.Right, Slices[0].Height);

                //mid-left
                Slices[3] = new Rectangle(Slices[0].X, slice.Y, Slices[0].Width, slice.Height);
                //center
                Slices[4] = slice;
                //mid-right
                Slices[5] = new Rectangle(Slices[2].X, slice.Y, Slices[2].Width, slice.Height);

                //bottom-left
                Slices[6] = new Rectangle(Slices[0].X, slice.Bottom, Slices[0].Width, Sprite.SourceRect.Bottom - slice.Bottom);
                //bottom-mid
                Slices[7] = new Rectangle(Slices[1].X, slice.Bottom, Slices[1].Width, Sprite.SourceRect.Bottom - slice.Bottom);
                //bottom-right
                Slices[8] = new Rectangle(Slices[2].X, slice.Bottom, Slices[2].Width, Sprite.SourceRect.Bottom - slice.Bottom);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect, Color color, SpriteEffects spriteEffects = SpriteEffects.None)
        {
            if (Sprite.Texture == null)
            {
                GUI.DrawRectangle(spriteBatch, rect, Color.Magenta);
                return;
            }

            if (Slice)
            {
                Vector2 pos = new Vector2(rect.X, rect.Y);

                Vector2 scale = Vector2.One;

                scale.Y = MathHelper.Clamp((float)rect.Height / (Slices[0].Height + Slices[6].Height), 0, 1);
                scale.X = MathHelper.Clamp((float)rect.Width / (Slices[0].Width + Slices[2].Width), 0, 1);

                scale.X = scale.Y =
                    MathHelper.Clamp(Math.Min(Math.Min(scale.X, scale.Y), GUI.SlicedSpriteScale), minBorderScale, maxBorderScale);
                int centerHeight = rect.Height - (int)((Slices[0].Height + Slices[6].Height) * scale.Y);
                int centerWidth = rect.Width - (int)((Slices[0].Width + Slices[2].Width) * scale.X);

                for (int x = 0; x < 3; x++)
                {
                    int width = (int)(x == 1 ? centerWidth : Slices[x].Width * scale.X);
                    if (width <= 0) { continue; }
                    for (int y = 0; y < 3; y++)
                    {
                        int height = (int)(y == 1 ? centerHeight : Slices[x + y * 3].Height * scale.Y);
                        if (height <= 0) { continue; }

                        spriteBatch.Draw(Sprite.Texture,
                            new Rectangle((int)pos.X, (int)pos.Y, width, height),
                            Slices[x + y * 3],
                            color);

                        pos.Y += height;
                    }
                    pos.X += width;
                    pos.Y = rect.Y;
                }
            }
            else if (Tile)
            {
                Vector2 startPos = new Vector2(rect.X, rect.Y);
                Sprite.DrawTiled(spriteBatch, startPos, new Vector2(rect.Width, rect.Height), null, color);
            }
            else
            {
                if (MaintainAspectRatio)
                {
                    float scale = Math.Min((float)rect.Width / Sprite.SourceRect.Width, (float)rect.Height / Sprite.SourceRect.Height);

                    spriteBatch.Draw(Sprite.Texture, rect.Center.ToVector2(),
                        Sprite.SourceRect,
                        color, 
                        rotation: 0.0f, 
                        origin: Sprite.size / 2.0f,
                        scale: scale,
                        effects: spriteEffects, layerDepth: 0.0f);
                }
                else
                {
                    spriteBatch.Draw(Sprite.Texture, rect, Sprite.SourceRect, color, 0, Vector2.Zero, spriteEffects, 0);
                }
            }
        }
    }
}
