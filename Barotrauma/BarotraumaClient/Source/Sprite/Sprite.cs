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
            // Creates a silhouette for the sprite (or outline if the sprite is rendered on top of it) -> don't remove
            //for (int x = -1; x <= 1; x += 2)
            //{
            //    for (int y = -1; y <= 1; y += 2)
            //    {
            //        spriteBatch.Draw(texture, pos + offset + new Vector2(x, y) * 1.0f, sourceRect, Color.Black, rotation + rotate, origin, scale, spriteEffect, (depth == null ? this.depth : (float)depth) + 0.0001f);
            //    }
            //}
            if (texture == null) return;
            spriteBatch.Draw(texture, pos + offset, sourceRect, color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        public void DrawTiled(SpriteBatch spriteBatch, Vector2 pos, Vector2 targetSize,
            Rectangle? sourceRect = null, Color? color = null, Point? startOffset = null, Vector2? textureScale = null, float? depth = null)
        {
            // Init optional values, if not provided
            Rectangle rect = sourceRect ?? this.sourceRect;
            color = color ?? Color.White;
            startOffset = startOffset ?? Point.Zero;
            Vector2 scale = textureScale ?? Vector2.One;
            depth = depth ?? this.depth;

            targetSize = targetSize / scale;
            //how many times the texture needs to be drawn on the x-axis
            int xTiles = (int)Math.Ceiling(targetSize.X / rect.Width);
            //how many times the texture needs to be drawn on the y-axis
            int yTiles = (int)Math.Ceiling(targetSize.Y / rect.Height);

            Rectangle texPerspective = rect;
            texPerspective.Location += startOffset.Value;
            while (texPerspective.X >= rect.Right)
                texPerspective.X = rect.X + (texPerspective.X - rect.Right);
            while (texPerspective.Y >= rect.Bottom)
                texPerspective.Y = rect.Y + (texPerspective.Y - rect.Bottom);

            float top = pos.Y;
            texPerspective.Height = (int)Math.Min(targetSize.Y, rect.Height);

            for (int y = 0; y < yTiles; y++)
            {
                float movementY = texPerspective.Height * scale.Y;
                texPerspective.Height = Math.Min((int)(targetSize.Y - texPerspective.Height * y), texPerspective.Height);

                float left = pos.X;
                texPerspective.Width = (int)Math.Min(targetSize.X, rect.Width);

                for (int x = 0; x < xTiles; x++)
                {
                    float movementX = texPerspective.Width * scale.X;
                    texPerspective.Width = Math.Min((int)(targetSize.X - texPerspective.Width * x), texPerspective.Width);

                    spriteBatch.Draw(texture, new Vector2(left, top), texPerspective, color.Value, rotation, Vector2.Zero, scale, effects, depth.Value);

                    if (texPerspective.X + movementX >= rect.Right) texPerspective.X = rect.X;
                    left += movementX;
                }

                if (texPerspective.Y + movementY >= rect.Bottom)
                {
                    texPerspective.Y = rect.Y;
                }
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

