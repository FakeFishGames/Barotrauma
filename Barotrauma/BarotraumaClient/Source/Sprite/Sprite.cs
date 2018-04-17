using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace Barotrauma
{
    public partial class Sprite
    {
        protected Texture2D texture;

        public Texture2D Texture
        {
            get { return texture; }
        }

        public Sprite(Texture2D texture, Rectangle? sourceRectangle, Vector2? newOffset, float newRotation = 0.0f)
        {
            this.texture = texture;

            sourceRect = sourceRectangle ?? new Rectangle(0, 0, texture.Width, texture.Height);

            offset = newOffset ?? Vector2.Zero;

            size = new Vector2(sourceRect.Width, sourceRect.Height);

            origin = Vector2.Zero;

            effects = SpriteEffects.None;

            rotation = newRotation;

            list.Add(this);
        }

        partial void LoadTexture(ref Vector4 sourceVector,ref bool shouldReturn)
        {
            texture = LoadTexture(this.file);

            if (texture == null)
            {
                shouldReturn = true;
                return;
            }

            if (sourceVector.Z == 0.0f) sourceVector.Z = texture.Width;
            if (sourceVector.W == 0.0f) sourceVector.W = texture.Height;
        }

        partial void CalculateSourceRect()
        {
            sourceRect = new Rectangle(0, 0, texture.Width, texture.Height);
        }


        public static Texture2D LoadTexture(string file)
        {
            foreach (Sprite s in list)
            {
                if (s.file == file) return s.texture;
            }

            if (File.Exists(file))
            {
                return TextureLoader.FromFile(file);
            }
            else
            {
                DebugConsole.ThrowError("Sprite \"" + file + "\" not found!");
            }

            return null;
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, float rotate = 0.0f, float scale = 1.0f, SpriteEffects spriteEffect = SpriteEffects.None)
        {
            this.Draw(spriteBatch, pos, Color.White, rotate, scale, spriteEffect);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, float rotate = 0.0f, float scale = 1.0f, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            this.Draw(spriteBatch, pos, color, this.origin, rotate, new Vector2(scale, scale), spriteEffect, depth);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate = 0.0f, float scale = 1.0f, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            this.Draw(spriteBatch, pos, color, origin, rotate, new Vector2(scale, scale), spriteEffect, depth);
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            if (texture == null) return;
            //DrawSilhouette(spriteBatch, pos, origin, rotate, scale, spriteEffect, depth);
            spriteBatch.Draw(texture, pos + offset, sourceRect, color, rotation + rotate, origin, scale, spriteEffect, depth ?? this.depth);
        }

        /// <summary>
        /// Creates a silhouette for the sprite (or outline if the sprite is rendered on top of it)
        /// </summary>
        public void DrawSilhouette(SpriteBatch spriteBatch, Vector2 pos, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            if (texture == null) return;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    spriteBatch.Draw(texture, pos + offset + new Vector2(x, y), sourceRect, Color.Black, rotation + rotate, origin, scale, spriteEffect, (depth ?? this.depth) + 0.01f);
                }
            }
        }

        public void DrawTiled(SpriteBatch spriteBatch, Vector2 pos, Vector2 targetSize,
            Rectangle? rect = null, Color? color = null, Point? startOffset = null, Vector2? textureScale = null, float? depth = null)
        {
            if (texture == null) return;
            // Init optional values, if not provided
            if (rect.HasValue)
            {
                //TODO: this probably shouldn't be modifying the sourceRect of the sprite?
                sourceRect = rect.Value;
            }
            color = color ?? Color.White;
            startOffset = startOffset ?? Point.Zero;
            Vector2 scale = textureScale ?? Vector2.One;

            //which area of the texture to draw
            Rectangle texPerspective = sourceRect;
            texPerspective.Location += startOffset.Value;
            targetSize = targetSize / scale;

            //how many times the texture needs to be drawn on the x-axis
            int xTiles = (int)Math.Ceiling(targetSize.X / sourceRect.Width);
            //how many times the texture needs to be drawn on the y-axis
            int yTiles = (int)Math.Ceiling(targetSize.Y / sourceRect.Height);

            //wrap texPerspective inside the source rectangle
            while (texPerspective.X >= sourceRect.Right)
                texPerspective.X = sourceRect.X + (texPerspective.X - sourceRect.Right);
            while (texPerspective.Y >= sourceRect.Bottom)
                texPerspective.Y = sourceRect.Y + (texPerspective.Y - sourceRect.Bottom);

            float top = pos.Y;
            texPerspective.Height = (int)Math.Min(Math.Ceiling(targetSize.Y), sourceRect.Height);

            for (int y = 0; y < yTiles; y++)
            {
                float movementY = texPerspective.Height * scale.Y;
                texPerspective.Height = Math.Min((int)Math.Ceiling(targetSize.Y - texPerspective.Height * y), texPerspective.Height);

                float left = pos.X;
                texPerspective.Width = Math.Min((int)Math.Ceiling(targetSize.X), sourceRect.Width);

                for (int x = 0; x < xTiles; x++)
                {
                    float movementX = texPerspective.Width * scale.X;
                    texPerspective.Width = Math.Min((int)Math.Ceiling(targetSize.X - texPerspective.Width * x), texPerspective.Width);

                    //the edge of this tile would go over the right edge of the source rectangle, 
                    //we need to wrap back and draw a slice from the left side
                    if (texPerspective.Right > sourceRect.Right)
                    {
                        int diff = texPerspective.Right - sourceRect.Right;
                        if (effects.HasFlag(SpriteEffects.FlipHorizontally))
                        {
                            spriteBatch.Draw(texture,
                                new Vector2(left, top),
                                new Rectangle(sourceRect.X, texPerspective.Y, diff, texPerspective.Height),
                                color.Value, rotation, Vector2.Zero, scale, effects, depth ?? this.depth);

                            texPerspective.Width -= diff;
                            left += diff;
                        }
                        else
                        {
                            texPerspective.Width -= diff;
                            spriteBatch.Draw(texture,
                                new Vector2(left + texPerspective.Width * scale.X, top),
                                new Rectangle(sourceRect.X, texPerspective.Y, diff, texPerspective.Height),
                                color.Value, rotation, Vector2.Zero, scale, effects, depth ?? this.depth);
                        }
                    }
                    else if (texPerspective.Bottom > sourceRect.Bottom)
                    {
                        //TODO: make this work correctly on vertically flipped sprites
                        int diff = texPerspective.Bottom - sourceRect.Bottom;
                        texPerspective.Height -= diff;
                        spriteBatch.Draw(texture,
                            new Vector2(left, top + texPerspective.Height * scale.Y),
                            new Rectangle(texPerspective.X, sourceRect.Y, texPerspective.Width, diff),
                            color.Value, rotation, Vector2.Zero, scale, effects, depth ?? this.depth);
                    }

                    spriteBatch.Draw(texture, new Vector2(left, top), texPerspective,
                        color.Value, rotation, Vector2.Zero, scale, effects, depth ?? this.depth);

                    if (texPerspective.X + movementX >= sourceRect.Right && x < xTiles - 1) texPerspective.X = sourceRect.X;
                    left += movementX;
                }
                if (texPerspective.Y + movementY >= sourceRect.Bottom && y < yTiles - 1) texPerspective.Y = sourceRect.Y;
                top += movementY;
            }
        }

        partial void DisposeTexture()
        {
            //check if another sprite is using the same texture
            foreach (Sprite s in list)
            {
                if (s.file == file) return;
            }

            //if not, free the texture
            if (texture != null)
            {
                texture.Dispose();
                texture = null;
            }
        }
    }

}

