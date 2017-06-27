using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
}
