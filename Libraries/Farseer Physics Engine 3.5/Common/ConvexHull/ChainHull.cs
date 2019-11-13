/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.ConvexHull
{
    /// <summary>
    /// Andrew's Monotone Chain Convex Hull algorithm.
    /// Used to get the convex hull of a point cloud.
    /// 
    /// Source: http://www.softsurfer.com/Archive/algorithm_0109/algorithm_0109.htm
    /// </summary>
    public static class ChainHull
    {
        //Copyright 2001, softSurfer (www.softsurfer.com)

        private static PointComparer _pointComparer = new PointComparer();

        /// <summary>
        /// Returns the convex hull from the given vertices..
        /// </summary>
        public static Vertices GetConvexHull(Vertices vertices)
        {
            if (vertices.Count <= 3)
                return vertices;

            Vertices pointSet = new Vertices(vertices);

            //Sort by X-axis
            pointSet.Sort(_pointComparer);

            Vector2[] h = new Vector2[pointSet.Count];
            Vertices res;

            int top = -1; // indices for bottom and top of the stack
            int i; // array scan index

            // Get the indices of points with min x-coord and min|max y-coord
            const int minmin = 0;
            float xmin = pointSet[0].X;
            for (i = 1; i < pointSet.Count; i++)
            {
                if (pointSet[i].X != xmin)
                    break;
            }

            // degenerate case: all x-coords == xmin
            int minmax = i - 1;
            if (minmax == pointSet.Count - 1)
            {
                h[++top] = pointSet[minmin];

                if (pointSet[minmax].Y != pointSet[minmin].Y) // a nontrivial segment
                    h[++top] = pointSet[minmax];

                h[++top] = pointSet[minmin]; // add polygon endpoint

                res = new Vertices(top + 1);
                for (int j = 0; j < top + 1; j++)
                {
                    res.Add(h[j]);
                }

                return res;
            }

            top = -1;

            // Get the indices of points with max x-coord and min|max y-coord
            int maxmax = pointSet.Count - 1;
            float xmax = pointSet[pointSet.Count - 1].X;
            for (i = pointSet.Count - 2; i >= 0; i--)
            {
                if (pointSet[i].X != xmax)
                    break;
            }
            int maxmin = i + 1;

            // Compute the lower hull on the stack H
            h[++top] = pointSet[minmin]; // push minmin point onto stack
            i = minmax;
            while (++i <= maxmin)
            {
                // the lower line joins P[minmin] with P[maxmin]
                if (MathUtils.Area(pointSet[minmin], pointSet[maxmin], pointSet[i]) >= 0 && i < maxmin)
                    continue; // ignore P[i] above or on the lower line

                while (top > 0) // there are at least 2 points on the stack
                {
                    // test if P[i] is left of the line at the stack top
                    if (MathUtils.Area(h[top - 1], h[top], pointSet[i]) > 0)
                        break; // P[i] is a new hull vertex

                    top--; // pop top point off stack
                }
                h[++top] = pointSet[i]; // push P[i] onto stack
            }

            // Next, compute the upper hull on the stack H above the bottom hull
            if (maxmax != maxmin) // if distinct xmax points
                h[++top] = pointSet[maxmax]; // push maxmax point onto stack
            int bot = top;
            i = maxmin;
            while (--i >= minmax)
            {
                // the upper line joins P[maxmax] with P[minmax]
                if (MathUtils.Area(pointSet[maxmax], pointSet[minmax], pointSet[i]) >= 0 && i > minmax)
                    continue; // ignore P[i] below or on the upper line

                while (top > bot) // at least 2 points on the upper stack
                {
                    // test if P[i] is left of the line at the stack top
                    if (MathUtils.Area(h[top - 1], h[top], pointSet[i]) > 0)
                        break; // P[i] is a new hull vertex

                    top--; // pop top point off stack
                }

                h[++top] = pointSet[i]; // push P[i] onto stack
            }

            if (minmax != minmin)
                h[++top] = pointSet[minmin]; // push joining endpoint onto stack

            res = new Vertices(top + 1);

            for (int j = 0; j < top + 1; j++)
            {
                res.Add(h[j]);
            }

            return res;
        }

        private class PointComparer : Comparer<Vector2>
        {
            public override int Compare(Vector2 a, Vector2 b)
            {
                int f = a.X.CompareTo(b.X);
                return f != 0 ? f : a.Y.CompareTo(b.Y);
            }
        }
    }
}