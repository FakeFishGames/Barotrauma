using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    class CompareSegmentPointCW : IComparer<Lights.SegmentPoint>
    {
        private Vector2 center;

        public CompareSegmentPointCW(Vector2 center)
        {
            this.center = center;
        }
        public int Compare(Lights.SegmentPoint a, Lights.SegmentPoint b)
        {
            return -CompareCCW.Compare(a.WorldPos, b.WorldPos, center);
        }
    }

    public class CompareVertexPositionColorCCW : IComparer<VertexPositionColor>
    {
        private Vector2 center;

        public CompareVertexPositionColorCCW(Vector2 center)
        {
            this.center = center;
        }
        public int Compare(VertexPositionColor a, VertexPositionColor b)
        {
            return -CompareCW.Compare(new Vector2(a.Position.X, a.Position.Y), new Vector2(b.Position.X, b.Position.Y), center);
        }
        public static int Compare(VertexPositionColor a, VertexPositionColor b, Vector2 center)
        {
            return -CompareCW.Compare(new Vector2(a.Position.X, a.Position.Y), new Vector2(b.Position.X, b.Position.Y), center);
        }
    }
}
