using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public partial class Sprite
    {
        static List<Sprite> list = new List<Sprite>();
        //the file from which the texture is loaded
        //if two sprites use the same file, they share the same texture
        string file;

        //the area in the texture that is supposed to be drawn
        Rectangle sourceRect;

        //the offset used when drawing the sprite
        protected Vector2 offset;

        protected Vector2 origin;

        //the size of the drawn sprite, if larger than the source,
        //the sprite is tiled to fill the target size
        public Vector2 size;

        public float rotation;

        public SpriteEffects effects;

        protected float depth;

        public Rectangle SourceRect
        {
            get { return sourceRect; }
            set { sourceRect = value; }
        }

        public float Depth
        {
            get { return depth; }
            set { depth = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public Vector2 Origin
        {
            get { return origin; }
            set { origin = value; }
        }
        
        public string FilePath
        {
            get { return file; }
        }

        public override string ToString()
        {
            return FilePath + ": " + sourceRect;
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
            
            Vector4 sourceVector = ToolBox.GetAttributeVector4(element, "sourcerect", Vector4.Zero);

            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn);
            if (shouldReturn) return;

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

            list.Add(this);
        }

        public Sprite(string newFile, Vector2 newOrigin)
        {
            file = newFile;

            Vector4 sourceVector = Vector4.Zero;
            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn);
            if (shouldReturn) return;

            CalculateSourceRect();

            size = new Vector2(sourceRect.Width, sourceRect.Height);

            origin = new Vector2((float)sourceRect.Width * newOrigin.X, (float)sourceRect.Height * newOrigin.Y);

            effects = SpriteEffects.None;

            list.Add(this);
        }
        
        public Sprite(string newFile, Rectangle? sourceRectangle, Vector2? newOffset, float newRotation = 0.0f)
        {
            file = newFile;
            Vector4 sourceVector = Vector4.Zero;
            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn);
            if (shouldReturn) return;

            if (sourceRectangle != null)
            {
                sourceRect = (Rectangle)sourceRectangle;
            }
            else
            {
                CalculateSourceRect();
            }
            
            offset = newOffset ?? Vector2.Zero; 
            
            size = new Vector2(sourceRect.Width, sourceRect.Height);

            origin = Vector2.Zero;
                        
            rotation = newRotation;

            list.Add(this);
        }
        
        public void Remove()
        {
            list.Remove(this);

            DisposeTexture();
        }
    }
}

