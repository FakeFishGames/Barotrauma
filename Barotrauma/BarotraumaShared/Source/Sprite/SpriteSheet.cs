using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    public partial class SpriteSheet : Sprite
    {
        private Rectangle[] sourceRects;
        private int emptyFrames;

        public int FrameCount
        {
            get { return sourceRects.Length - emptyFrames; }
        }

        public Point FrameSize
        {
            get;
            private set;
        }
        
        public SpriteSheet(XElement element, string path = "", string file = "", int emptyFrameCount = 0)
            : base(element, path, file)
        {
            int columnCount = Math.Max(element.GetAttributeInt("columns", 1), 1);
            int rowCount = Math.Max(element.GetAttributeInt("rows", 1), 1);
            origin = element.GetAttributeVector2("origin", new Vector2(0.5f, 0.5f));
            emptyFrames = emptyFrameCount;
            Init(columnCount, rowCount);
        }

        public SpriteSheet(string filePath, int columnCount, int rowCount, Vector2 origin)
            : base(filePath, origin)
        {
            this.origin = origin;
            Init(columnCount, rowCount);
        }

        private void Init(int columnCount, int rowCount)
        {
            sourceRects = new Rectangle[rowCount * columnCount];

            float cellWidth = SourceRect.Width / columnCount;
            float cellHeight = SourceRect.Height / rowCount;
            FrameSize = new Point((int)cellWidth, (int)cellHeight);

            for (int x = 0; x < columnCount; x++)
            {
                for (int y = 0; y < rowCount; y++)
                {
                    sourceRects[x + y * columnCount] = new Rectangle((int)(SourceRect.X + x * cellWidth), (int)(SourceRect.Y + y * cellHeight), (int)cellWidth, (int)cellHeight);
                }
            }

            origin.X = origin.X * cellWidth;
            origin.Y = origin.Y * cellHeight;
        }
    }
}
