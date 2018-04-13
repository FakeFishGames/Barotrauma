using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Xml.Linq;

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

        public SpriteEffects effects = SpriteEffects.None;

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

        partial void LoadTexture(ref Vector4 sourceVector, ref bool shouldReturn);
        partial void CalculateSourceRect();

        // TODO: use the Init method below?
        public Sprite(XElement element, string path = "", string file = "")
        {
            if (file == "")
            {
                file = element.GetAttributeString("texture", "");
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
            
            Vector4 sourceVector = element.GetAttributeVector4("sourcerect", Vector4.Zero);

            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn);
            if (shouldReturn) return;

            sourceRect = new Rectangle(
                (int)sourceVector.X, (int)sourceVector.Y,
                (int)sourceVector.Z, (int)sourceVector.W);

            origin = element.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
            origin.X = origin.X * sourceRect.Width;
            origin.Y = origin.Y * sourceRect.Height;

            size = element.GetAttributeVector2("size", Vector2.One);
            size.X *= sourceRect.Width;
            size.Y *= sourceRect.Height;

            Depth = element.GetAttributeFloat("depth", 0.0f);

            list.Add(this);
        }

        public Sprite(string newFile, Vector2 newOrigin)
        {
            Init(newFile, newOrigin: newOrigin);
        }
        
        public Sprite(string newFile, Rectangle? sourceRectangle, Vector2? newOffset = null, float newRotation = 0)
        {
            Init(newFile, sourceRectangle: sourceRectangle, newOffset: newOffset, newRotation: newRotation);
        }
        
        private void Init(string newFile, Rectangle? sourceRectangle = null, Vector2? newOrigin = null, Vector2? newOffset = null, float newRotation = 0)
        {
            file = newFile;
            Vector4 sourceVector = Vector4.Zero;
            bool shouldReturn = false;
            LoadTexture(ref sourceVector, ref shouldReturn);
            if (shouldReturn) return;
            if (sourceRectangle.HasValue)
            {
                sourceRect = sourceRectangle.Value;
            }
            else
            {
                CalculateSourceRect();
            }
            offset = newOffset ?? Vector2.Zero;
            if (newOrigin.HasValue)
            {
                origin = new Vector2(sourceRect.Width * newOrigin.Value.X, sourceRect.Height * newOrigin.Value.Y);
            }
            size = new Vector2(sourceRect.Width, sourceRect.Height);
            rotation = newRotation;
            if (!list.Contains(this))
            {
                list.Add(this);
            }
        }
        
        public void Remove()
        {
            list.Remove(this);

            DisposeTexture();
        }

        partial void DisposeTexture();
    }
}

