using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.Seidel
{
    internal class TrapezoidalMap
    {
        // Trapezoid container
        public HashSet<Trapezoid> Map;

        // Bottom segment that spans multiple trapezoids
        private Edge _bCross;

        // Top segment that spans multiple trapezoids
        private Edge _cross;

        // AABB margin
        private float _margin;

        public TrapezoidalMap()
        {
            Map = new HashSet<Trapezoid>();
            _margin = 50.0f;
            _bCross = null;
            _cross = null;
        }

        public void Clear()
        {
            _bCross = null;
            _cross = null;
        }

        // Case 1: segment completely enclosed by trapezoid
        //         break trapezoid into 4 smaller trapezoids
        public Trapezoid[] Case1(Trapezoid t, Edge e)
        {
            Trapezoid[] trapezoids = new Trapezoid[4];
            trapezoids[0] = new Trapezoid(t.LeftPoint, e.P, t.Top, t.Bottom);
            trapezoids[1] = new Trapezoid(e.P, e.Q, t.Top, e);
            trapezoids[2] = new Trapezoid(e.P, e.Q, e, t.Bottom);
            trapezoids[3] = new Trapezoid(e.Q, t.RightPoint, t.Top, t.Bottom);

            trapezoids[0].UpdateLeft(t.UpperLeft, t.LowerLeft);
            trapezoids[1].UpdateLeftRight(trapezoids[0], null, trapezoids[3], null);
            trapezoids[2].UpdateLeftRight(null, trapezoids[0], null, trapezoids[3]);
            trapezoids[3].UpdateRight(t.UpperRight, t.LowerRight);

            return trapezoids;
        }

        // Case 2: Trapezoid contains point p, q lies outside
        //         break trapezoid into 3 smaller trapezoids
        public Trapezoid[] Case2(Trapezoid t, Edge e)
        {
            Point rp;
            if (e.Q.X == t.RightPoint.X)
                rp = e.Q;
            else
                rp = t.RightPoint;

            Trapezoid[] trapezoids = new Trapezoid[3];
            trapezoids[0] = new Trapezoid(t.LeftPoint, e.P, t.Top, t.Bottom);
            trapezoids[1] = new Trapezoid(e.P, rp, t.Top, e);
            trapezoids[2] = new Trapezoid(e.P, rp, e, t.Bottom);

            trapezoids[0].UpdateLeft(t.UpperLeft, t.LowerLeft);
            trapezoids[1].UpdateLeftRight(trapezoids[0], null, t.UpperRight, null);
            trapezoids[2].UpdateLeftRight(null, trapezoids[0], null, t.LowerRight);

            _bCross = t.Bottom;
            _cross = t.Top;

            e.Above = trapezoids[1];
            e.Below = trapezoids[2];

            return trapezoids;
        }

        // Case 3: Trapezoid is bisected
        public Trapezoid[] Case3(Trapezoid t, Edge e)
        {
            Point lp;
            if (e.P.X == t.LeftPoint.X)
                lp = e.P;
            else
                lp = t.LeftPoint;

            Point rp;
            if (e.Q.X == t.RightPoint.X)
                rp = e.Q;
            else
                rp = t.RightPoint;

            Trapezoid[] trapezoids = new Trapezoid[2];

            if (_cross == t.Top)
            {
                trapezoids[0] = t.UpperLeft;
                trapezoids[0].UpdateRight(t.UpperRight, null);
                trapezoids[0].RightPoint = rp;
            }
            else
            {
                trapezoids[0] = new Trapezoid(lp, rp, t.Top, e);
                trapezoids[0].UpdateLeftRight(t.UpperLeft, e.Above, t.UpperRight, null);
            }

            if (_bCross == t.Bottom)
            {
                trapezoids[1] = t.LowerLeft;
                trapezoids[1].UpdateRight(null, t.LowerRight);
                trapezoids[1].RightPoint = rp;
            }
            else
            {
                trapezoids[1] = new Trapezoid(lp, rp, e, t.Bottom);
                trapezoids[1].UpdateLeftRight(e.Below, t.LowerLeft, null, t.LowerRight);
            }

            _bCross = t.Bottom;
            _cross = t.Top;

            e.Above = trapezoids[0];
            e.Below = trapezoids[1];

            return trapezoids;
        }

        // Case 4: Trapezoid contains point q, p lies outside
        //         break trapezoid into 3 smaller trapezoids
        public Trapezoid[] Case4(Trapezoid t, Edge e)
        {
            Point lp;
            if (e.P.X == t.LeftPoint.X)
                lp = e.P;
            else
                lp = t.LeftPoint;

            Trapezoid[] trapezoids = new Trapezoid[3];

            if (_cross == t.Top)
            {
                trapezoids[0] = t.UpperLeft;
                trapezoids[0].RightPoint = e.Q;
            }
            else
            {
                trapezoids[0] = new Trapezoid(lp, e.Q, t.Top, e);
                trapezoids[0].UpdateLeft(t.UpperLeft, e.Above);
            }

            if (_bCross == t.Bottom)
            {
                trapezoids[1] = t.LowerLeft;
                trapezoids[1].RightPoint = e.Q;
            }
            else
            {
                trapezoids[1] = new Trapezoid(lp, e.Q, e, t.Bottom);
                trapezoids[1].UpdateLeft(e.Below, t.LowerLeft);
            }

            trapezoids[2] = new Trapezoid(e.Q, t.RightPoint, t.Top, t.Bottom);
            trapezoids[2].UpdateLeftRight(trapezoids[0], trapezoids[1], t.UpperRight, t.LowerRight);

            return trapezoids;
        }

        // Create an AABB around segments
        public Trapezoid BoundingBox(List<Edge> edges)
        {
            Point max = edges[0].P + _margin;
            Point min = edges[0].Q - _margin;

            foreach (Edge e in edges)
            {
                if (e.P.X > max.X) max = new Point(e.P.X + _margin, max.Y);
                if (e.P.Y > max.Y) max = new Point(max.X, e.P.Y + _margin);
                if (e.Q.X > max.X) max = new Point(e.Q.X + _margin, max.Y);
                if (e.Q.Y > max.Y) max = new Point(max.X, e.Q.Y + _margin);
                if (e.P.X < min.X) min = new Point(e.P.X - _margin, min.Y);
                if (e.P.Y < min.Y) min = new Point(min.X, e.P.Y - _margin);
                if (e.Q.X < min.X) min = new Point(e.Q.X - _margin, min.Y);
                if (e.Q.Y < min.Y) min = new Point(min.X, e.Q.Y - _margin);
            }

            Edge top = new Edge(new Point(min.X, max.Y), new Point(max.X, max.Y));
            Edge bottom = new Edge(new Point(min.X, min.Y), new Point(max.X, min.Y));
            Point left = bottom.P;
            Point right = top.Q;

            return new Trapezoid(left, right, top, bottom);
        }
    }
}