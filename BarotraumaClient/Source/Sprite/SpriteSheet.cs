using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class SpriteSheet : Sprite
    {
        private Rectangle[] sourceRects;

        public int FrameCount
        {
            get { return sourceRects.Length; }
        }
        
        public SpriteSheet(XElement element, string path = "", string file = "")
            : base(element, path, file)
        {
            int columnCount = Math.Max(ToolBox.GetAttributeInt(element, "columns", 1), 1);
            int rowCount = Math.Max(ToolBox.GetAttributeInt(element, "rows", 1), 1);

            sourceRects = new Rectangle[rowCount * columnCount];

            int cellWidth = SourceRect.Width / columnCount;
            int cellHeight = SourceRect.Height / rowCount;

            for (int x = 0; x < columnCount; x++)
            {
                for (int y = 0; y < rowCount; y++)
                {
                    sourceRects[x + y * columnCount] = new Rectangle(x * cellWidth, y * cellHeight, cellWidth, cellHeight);
                }
            }

            origin = ToolBox.GetAttributeVector2(element, "origin", new Vector2(0.5f, 0.5f));
            origin.X = origin.X * cellWidth;
            origin.Y = origin.Y * cellHeight;
        }
        
        public override void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[0], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        public void Draw(SpriteBatch spriteBatch, int spriteIndex, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[spriteIndex], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }
    }
}
