/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using FarseerPhysics.Collision;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common
{
    public enum PolygonError
    {
        /// <summary>
        /// There were no errors in the polygon
        /// </summary>
        NoError,

        /// <summary>
        /// Polygon must have between 3 and Settings.MaxPolygonVertices vertices.
        /// </summary>
        InvalidAmountOfVertices,

        /// <summary>
        /// Polygon must be simple. This means no overlapping edges.
        /// </summary>
        NotSimple,

        /// <summary>
        /// Polygon must have a counter clockwise winding.
        /// </summary>
        NotCounterClockWise,

        /// <summary>
        /// The polygon is concave, it needs to be convex.
        /// </summary>
        NotConvex,

        /// <summary>
        /// Polygon area is too small.
        /// </summary>
        AreaTooSmall,

        /// <summary>
        /// The polygon has a side that is too short.
        /// </summary>
        SideTooSmall
    }

#if !(XBOX360)
    [DebuggerDisplay("Count = {Count} Vertices = {ToString()}")]
#endif
    public class Vertices : List<Vector2>
    {
        public Vertices() { }

        public Vertices(int capacity) : base(capacity) { }

        public Vertices(IEnumerable<Vector2> vertices)
        {
            AddRange(vertices);
        }

        internal bool AttachedToBody { get; set; }

        /// <summary>
        /// You can add holes to this collection.
        /// It will get respected by some of the triangulation algoithms, but otherwise not used.
        /// </summary>
        public List<Vertices> Holes { get; set; }

        /// <summary>
        /// Gets the next index. Used for iterating all the edges with wrap-around.
        /// </summary>
        /// <param name="index">The current index</param>
        public int NextIndex(int index)
        {
            return (index + 1 > Count - 1) ? 0 : index + 1;
        }

        /// <summary>
        /// Gets the next vertex. Used for iterating all the edges with wrap-around.
        /// </summary>
        /// <param name="index">The current index</param>
        public Vector2 NextVertex(int index)
        {
            return this[NextIndex(index)];
        }

        /// <summary>
        /// Gets the previous index. Used for iterating all the edges with wrap-around.
        /// </summary>
        /// <param name="index">The current index</param>
        public int PreviousIndex(int index)
        {
            return index - 1 < 0 ? Count - 1 : index - 1;
        }

        /// <summary>
        /// Gets the previous vertex. Used for iterating all the edges with wrap-around.
        /// </summary>
        /// <param name="index">The current index</param>
        public Vector2 PreviousVertex(int index)
        {
            return this[PreviousIndex(index)];
        }

        /// <summary>
        /// Gets the signed area.
        /// If the area is less than 0, it indicates that the polygon is clockwise winded.
        /// </summary>
        /// <returns>The signed area</returns>
        public float GetSignedArea()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return 0;

            int i;
            float area = 0;

            for (i = 0; i < Count; i++)
            {
                int j = (i + 1) % Count;

                Vector2 vi = this[i];
                Vector2 vj = this[j];

                area += vi.X * vj.Y;
                area -= vi.Y * vj.X;
            }
            area /= 2.0f;
            return area;
        }

        /// <summary>
        /// Gets the area.
        /// </summary>
        /// <returns></returns>
        public float GetArea()
        {
            float area = GetSignedArea();
            return (area < 0 ? -area : area);
        }

        /// <summary>
        /// Gets the centroid.
        /// </summary>
        /// <returns></returns>
        public Vector2 GetCentroid()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return new Vector2(float.NaN, float.NaN);

            // Same algorithm is used by Box2D
            Vector2 c = Vector2.Zero;
            float area = 0.0f;
            const float inv3 = 1.0f / 3.0f;

            for (int i = 0; i < Count; ++i)
            {
                // Triangle vertices.
                Vector2 current = this[i];
                Vector2 next = (i + 1 < Count ? this[i + 1] : this[0]);

                float triangleArea = 0.5f * (current.X * next.Y - current.Y * next.X);
                area += triangleArea;

                // Area weighted centroid
                c += triangleArea * inv3 * (current + next);
            }

            // Centroid
            c *= 1.0f / area;
            return c;
        }

        /// <summary>
        /// Returns an AABB that fully contains this polygon.
        /// </summary>
        public AABB GetAABB()
        {
            AABB aabb;
            Vector2 lowerBound = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 upperBound = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < Count; ++i)
            {
                if (this[i].X < lowerBound.X)
                {
                    lowerBound.X = this[i].X;
                }
                if (this[i].X > upperBound.X)
                {
                    upperBound.X = this[i].X;
                }

                if (this[i].Y < lowerBound.Y)
                {
                    lowerBound.Y = this[i].Y;
                }
                if (this[i].Y > upperBound.Y)
                {
                    upperBound.Y = this[i].Y;
                }
            }

            aabb.LowerBound = lowerBound;
            aabb.UpperBound = upperBound;

            return aabb;
        }

        /// <summary>
        /// Translates the vertices with the specified vector.
        /// </summary>
        /// <param name="value">The value.</param>
        public void Translate(Vector2 value)
        {
            Translate(ref value);
        }

        /// <summary>
        /// Translates the vertices with the specified vector.
        /// </summary>
        /// <param name="value">The vector.</param>
        public void Translate(ref Vector2 value)
        {
            Debug.Assert(!AttachedToBody, "Translating vertices that are used by a Body can result in unstable behavior. Use Body.Position instead.");

            for (int i = 0; i < Count; i++)
                this[i] = Vector2.Add(this[i], value);

            if (Holes != null && Holes.Count > 0)
            {
                foreach (Vertices hole in Holes)
                {
                    hole.Translate(ref value);
                }
            }
        }

        /// <summary>
        /// Scales the vertices with the specified vector.
        /// </summary>
        /// <param name="value">The Value.</param>
        public void Scale(Vector2 value)
        {
            Scale(ref value);
        }

        /// <summary>
        /// Scales the vertices with the specified vector.
        /// </summary>
        /// <param name="value">The Value.</param>
        public void Scale(ref Vector2 value)
        {
            Debug.Assert(!AttachedToBody, "Scaling vertices that are used by a Body can result in unstable behavior.");

            for (int i = 0; i < Count; i++)
                this[i] = Vector2.Multiply(this[i], value);

            if (Holes != null && Holes.Count > 0)
            {
                foreach (Vertices hole in Holes)
                {
                    hole.Scale(ref value);
                }
            }
        }

        /// <summary>
        /// Rotate the vertices with the defined value in radians.
        /// 
        /// Warning: Using this method on an active set of vertices of a Body,
        /// will cause problems with collisions. Use Body.Rotation instead.
        /// </summary>
        /// <param name="value">The amount to rotate by in radians.</param>
        public void Rotate(float value)
        {
            Debug.Assert(!AttachedToBody, "Rotating vertices that are used by a Body can result in unstable behavior.");

            float num1 = (float)Math.Cos(value);
            float num2 = (float)Math.Sin(value);

            for (int i = 0; i < Count; i++)
            {
                Vector2 position = this[i];
                this[i] = new Vector2((position.X * num1 + position.Y * -num2), (position.X * num2 + position.Y * num1));
            }

            if (Holes != null && Holes.Count > 0)
            {
                foreach (Vertices hole in Holes)
                {
                    hole.Rotate(value);
                }
            }
        }

        /// <summary>
        /// Determines whether the polygon is convex.
        /// O(n^2) running time.
        /// 
        /// Assumptions:
        /// - The polygon is in counter clockwise order
        /// - The polygon has no overlapping edges
        /// </summary>
        /// <returns>
        /// 	<c>true</c> if it is convex; otherwise, <c>false</c>.
        /// </returns>
        public bool IsConvex()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return false;

            //Triangles are always convex
            if (Count == 3)
                return true;

            // Checks the polygon is convex and the interior is to the left of each edge.
            for (int i = 0; i < Count; ++i)
            {
                int next = i + 1 < Count ? i + 1 : 0;
                Vector2 edge = this[next] - this[i];

                for (int j = 0; j < Count; ++j)
                {
                    // Don't check vertices on the current edge.
                    if (j == i || j == next)
                        continue;

                    Vector2 r = this[j] - this[i];

                    float s = edge.X * r.Y - edge.Y * r.X;

                    if (s <= 0.0f)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Indicates if the vertices are in counter clockwise order.
        /// Warning: If the area of the polygon is 0, it is unable to determine the winding.
        /// </summary>
        public bool IsCounterClockWise()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return false;

            return (GetSignedArea() > 0.0f);
        }

        /// <summary>
        /// Forces the vertices to be counter clock wise order.
        /// </summary>
        public void ForceCounterClockWise()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return;

            if (!IsCounterClockWise())
                Reverse();
        }

        /// <summary>
        /// Checks if the vertices forms an simple polygon by checking for edge crossings.
        /// </summary>
        public bool IsSimple()
        {
            //The simplest polygon which can exist in the Euclidean plane has 3 sides.
            if (Count < 3)
                return false;

            for (int i = 0; i < Count; ++i)
            {
                Vector2 a1 = this[i];
                Vector2 a2 = NextVertex(i);
                for (int j = i + 1; j < Count; ++j)
                {
                    Vector2 b1 = this[j];
                    Vector2 b2 = NextVertex(j);

                    Vector2 temp;

                    if (LineTools.LineIntersect2(ref a1, ref a2, ref b1, ref b2, out temp))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if the polygon is valid for use in the engine.
        ///
        /// Performs a full check, for simplicity, convexity,
        /// orientation, minimum angle, and volume.
        /// 
        /// From Eric Jordan's convex decomposition library
        /// </summary>
        /// <returns>PolygonError.NoError if there were no error.</returns>
        public PolygonError CheckPolygon()
        {
            if (Count < 3 || Count > Settings.MaxPolygonVertices)
                return PolygonError.InvalidAmountOfVertices;

            if (!IsSimple())
                return PolygonError.NotSimple;

            if (GetArea() <= Settings.Epsilon)
                return PolygonError.AreaTooSmall;

            if (!IsConvex())
                return PolygonError.NotConvex;

            //Check if the sides are of adequate length.
            for (int i = 0; i < Count; ++i)
            {
                int next = i + 1 < Count ? i + 1 : 0;
                Vector2 edge = this[next] - this[i];
                if (edge.LengthSquared() <= Settings.Epsilon*Settings.Epsilon)
                {
                    return PolygonError.SideTooSmall;
                }
            }

            if (!IsCounterClockWise())
                return PolygonError.NotCounterClockWise;

            return PolygonError.NoError;
        }

        /// <summary>
        /// Projects to axis.
        /// </summary>
        /// <param name="axis">The axis.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        public void ProjectToAxis(ref Vector2 axis, out float min, out float max)
        {
            // To project a point on an axis use the dot product
            float dotProduct = Vector2.Dot(axis, this[0]);
            min = dotProduct;
            max = dotProduct;

            for (int i = 0; i < Count; i++)
            {
                dotProduct = Vector2.Dot(this[i], axis);
                if (dotProduct < min)
                {
                    min = dotProduct;
                }
                else
                {
                    if (dotProduct > max)
                    {
                        max = dotProduct;
                    }
                }
            }
        }

        /// <summary>
        /// Winding number test for a point in a polygon.
        /// </summary>
        /// See more info about the algorithm here: http://softsurfer.com/Archive/algorithm_0103/algorithm_0103.htm
        /// <param name="point">The point to be tested.</param>
        /// <returns>-1 if the winding number is zero and the point is outside
        /// the polygon, 1 if the point is inside the polygon, and 0 if the point
        /// is on the polygons edge.</returns>
        public int PointInPolygon(ref Vector2 point)
        {
            // Winding number
            int wn = 0;

            // Iterate through polygon's edges
            for (int i = 0; i < Count; i++)
            {
                // Get points
                Vector2 p1 = this[i];
                Vector2 p2 = this[NextIndex(i)];

                // Test if a point is directly on the edge
                Vector2 edge = p2 - p1;
                float area = MathUtils.Area(ref p1, ref p2, ref point);
                if (area == 0f && Vector2.Dot(point - p1, edge) >= 0f && Vector2.Dot(point - p2, edge) <= 0f)
                {
                    return 0;
                }
                // Test edge for intersection with ray from point
                if (p1.Y <= point.Y)
                {
                    if (p2.Y > point.Y && area > 0f)
                    {
                        ++wn;
                    }
                }
                else
                {
                    if (p2.Y <= point.Y && area < 0f)
                    {
                        --wn;
                    }
                }
            }
            return (wn == 0 ? -1 : 1);
        }

        /// <summary>
        /// Compute the sum of the angles made between the test point and each pair of points making up the polygon. 
        /// If this sum is 2pi then the point is an interior point, if 0 then the point is an exterior point. 
        /// ref: http://ozviz.wasp.uwa.edu.au/~pbourke/geometry/insidepoly/  - Solution 2 
        /// </summary>
        public bool PointInPolygonAngle(ref Vector2 point)
        {
            double angle = 0;

            // Iterate through polygon's edges
            for (int i = 0; i < Count; i++)
            {
                // Get points
                Vector2 p1 = this[i] - point;
                Vector2 p2 = this[NextIndex(i)] - point;

                angle += MathUtils.VectorAngle(ref p1, ref p2);
            }

            if (Math.Abs(angle) < Math.PI)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Transforms the polygon using the defined matrix.
        /// </summary>
        /// <param name="transform">The matrix to use as transformation.</param>
        public void Transform(ref Matrix transform)
        {
            // Transform main polygon
            for (int i = 0; i < Count; i++)
                this[i] = Vector2.Transform(this[i], transform);

            // Transform holes
            if (Holes != null && Holes.Count > 0)
            {
                for (int i = 0; i < Holes.Count; i++)
                {
                    Vector2[] temp = Holes[i].ToArray();
                    Vector2.Transform(temp, ref transform, temp);

                    Holes[i] = new Vertices(temp);
                }
            }
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                builder.Append(this[i].ToString());
                if (i < Count - 1)
                {
                    builder.Append(" ");
                }
            }
            return builder.ToString();
        }
    }
}