using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma
{
    public class Sprite
    {
        static List<Sprite> list = new List<Sprite>();
        //the file from which the texture is loaded
        //if two sprites use the same file, they share the same texture
        string file;

        Texture2D texture;

        //the area in the texture that is supposed to be drawn
        Rectangle sourceRect;

        //the offset used when drawing the sprite
        Vector2 offset;

        public Vector2 origin;

        //the size of the drawn sprite, if larger than the source,
        //the sprite is tiled to fill the target size
        public Vector2 size;

        public float rotation;

        public SpriteEffects effects;

        float depth;

        public Rectangle SourceRect
        {
            get { return sourceRect; }
            set { sourceRect = value; }
        }

        public float Depth
        {
            get { return depth; }
            set { depth = Math.Min(Math.Max(value, 0.0f), 1.0f); }
        }

        public Vector2 Origin
        {
            get { return origin; }
            set { origin = value; }
        }

        public Texture2D Texture
        {
            get { return texture; }
        }

        public string FilePath
        {
            get { return file; }
        }

        public Sprite(XElement element, string path = "", string file = "")
        {
            if (file == "")
            {
                file = ToolBox.GetAttributeString(element, "texture", "");
            }

            if (file == "")
            {
                DebugConsole.ThrowError("Sprite " + element + " doesn't have a texture specified!");
                return;
            }

            if (!string.IsNullOrEmpty(path))
            {
                if (!path.EndsWith("/")) path += "/";
            }

            this.file = path + file;
            
            texture = LoadTexture(this.file);

            if (texture == null) return;
            
            Vector4 sourceVector = ToolBox.GetAttributeVector4(element, "sourcerect", Vector4.Zero);

            if (sourceVector.Z == 0.0f) sourceVector.Z = texture.Width;
            if (sourceVector.W == 0.0f) sourceVector.W = texture.Height;

            sourceRect = new Rectangle(
                (int)sourceVector.X, (int)sourceVector.Y,
                (int)sourceVector.Z, (int)sourceVector.W);

            origin = ToolBox.GetAttributeVector2(element, "origin", new Vector2(0.5f, 0.5f));
            origin.X = origin.X * sourceRect.Width;
            origin.Y = origin.Y * sourceRect.Height;

            size = ToolBox.GetAttributeVector2(element, "size", Vector2.One);
            size.X *= sourceRect.Width;
            size.Y *= sourceRect.Height;

            Depth = ToolBox.GetAttributeFloat(element, "depth", 0.0f);
        }

        public Sprite(string newFile, Vector2 newOrigin)
        {
            file = newFile;
            texture = LoadTexture(file);

            if (texture == null) return;
            
            sourceRect = new Rectangle(0, 0, texture.Width, texture.Height);

            size = new Vector2(sourceRect.Width, sourceRect.Height);

            origin = new Vector2((float)texture.Width * newOrigin.X, (float)texture.Height * newOrigin.Y);

            effects = SpriteEffects.None;

            list.Add(this);
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

        public Sprite(string newFile, Rectangle? sourceRectangle, Vector2? newOffset, float newRotation = 0.0f)
        {
            file = newFile;
            texture = LoadTexture(file);

            sourceRect = sourceRectangle ?? new Rectangle(0, 0, texture.Width, texture.Height);
            
            offset = newOffset ?? Vector2.Zero; 
            
            size = new Vector2(sourceRect.Width, sourceRect.Height);

            origin = Vector2.Zero;
                        
            rotation = newRotation;

            list.Add(this);
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
                DebugConsole.ThrowError("Sprite ''"+file+"'' not found!");
            }

            return null;
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, float rotate=0.0f, float scale=1.0f, SpriteEffects spriteEffect = SpriteEffects.None)
        {
            spriteBatch.Draw(texture, pos + offset, sourceRect, Color.White, rotation + rotate, origin, scale, spriteEffect, depth);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, float rotate = 0.0f, float scale = 1.0f, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            spriteBatch.Draw(texture, pos + offset, sourceRect, color, rotation + rotate, origin, scale, spriteEffect, depth==null ? this.depth : (float)depth);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate = 0.0f, float scale = 1.0f, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            spriteBatch.Draw(texture, pos + offset, sourceRect, color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = null)
        {
            spriteBatch.Draw(texture, pos + offset, sourceRect, color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }
        
        public void DrawTiled(SpriteBatch spriteBatch, Vector2 pos, Vector2 targetSize, Color color)
        {
            DrawTiled(spriteBatch, pos, targetSize, Vector2.Zero, color);
        }

        public void DrawTiled(SpriteBatch spriteBatch, Vector2 pos, Vector2 targetSize, Vector2 startOffset, Color color)
        {
            pos.X = (int)pos.X;
            pos.Y = (int)pos.Y;

            //how many times the texture needs to be drawn on the x-axis
            int xTiles = (int)Math.Ceiling((targetSize.X+startOffset.X) / sourceRect.Width);
            //how many times the texture needs to be drawn on the y-axis
            int yTiles = (int)Math.Ceiling((targetSize.Y+startOffset.Y) / sourceRect.Height);
            
            Vector2 position = pos-startOffset;
            Rectangle drawRect = sourceRect;

            position.X = pos.X;

            for (int x = 0 ; x<xTiles ; x++)
            {                
                drawRect.X = sourceRect.X;
                drawRect.Height = sourceRect.Height;

                if (x == xTiles - 1)
                {
                    drawRect.Width -= (int)((position.X + sourceRect.Width) - (pos.X + targetSize.X));
                }
                else
                {
                    drawRect.Width = sourceRect.Width;
                }

                if (position.X < pos.X)
                {
                    float diff = pos.X - position.X;
                    position.X += diff;
                    drawRect.Width -= (int)diff;
                    drawRect.X += (int)diff;
                }

                position.Y = pos.Y;
                
                for (int y = 0 ; y<yTiles ; y++)
                {
                    drawRect.Y = sourceRect.Y;
                    
                    if (y == yTiles - 1)
                    {
                        drawRect.Height -= (int)((position.Y + sourceRect.Height) - (pos.Y + targetSize.Y));
                    }
                    else
                    {
                        drawRect.Height = sourceRect.Height;
                    }

                    if (position.Y < pos.Y)
                    {
                        int diff = (int)(pos.Y - position.Y);
                        position.Y += diff;
                        drawRect.Height -= diff;
                        drawRect.Y += diff;
                    }
                    
                    spriteBatch.Draw(texture, position,
                        drawRect, color, rotation, Vector2.Zero, 1.0f, effects, depth);
                    
                    position.Y += sourceRect.Height;
                }

                position.X += sourceRect.Width;
            }

        }

        public void Remove()
        {
            list.Remove(this);

            //check if another sprite is using the same texture
            foreach (Sprite s in list)
            {
                if (s.file == file) return;
            }

            //if not, free the texture
            texture.Dispose();
        }
        
    }

}

