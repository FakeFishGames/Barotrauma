using System;
using FarseerPhysics.Collision;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common
{
    /// <summary>
    /// Collection of helper methods for misc collisions.
    /// Does float tolerance and line collisions with lines and AABBs.
    /// </summary>
    public static class LineTools
    {
        public static float DistanceBetweenPointAndLineSegment(ref Vector2 point, ref Vector2 start, ref Vector2 end)
        {
            if (start == end)
                return Vector2.Distance(point, start);

            Vector2 v = Vector2.Subtract(end, start);
            Vector2 w = Vector2.Subtract(point, start);

            float c1 = Vector2.Dot(w, v);
            if (c1 <= 0) return Vector2.Distance(point, start);

            float c2 = Vector2.Dot(v, v);
            if (c2 <= c1) return Vector2.Distance(point, end);

            float b = c1 / c2;
            Vector2 pointOnLine = Vector2.Add(start, Vector2.Multiply(v, b));
            return Vector2.Distance(point, pointOnLine);
        }

        // From Eric Jordan's convex decomposition library
        /// <summary>
        ///Check if the lines a0->a1 and b0->b1 cross.
        ///If they do, intersectionPoint will be filled
        ///with the point of crossing.
        ///
        ///Grazing lines should not return true.
        /// 
        /// </summary>
        public static bool LineIntersect2(ref Vector2 a0, ref Vector2 a1, ref Vector2 b0, ref  Vector2 b1, out Vector2 intersectionPoint)
        {
            intersectionPoint = Vector2.Zero;

            if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                return false;

            float x1 = a0.X;
            float y1 = a0.Y;
            float x2 = a1.X;
            float y2 = a1.Y;
            float x3 = b0.X;
            float y3 = b0.Y;
            float x4 = b1.X;
            float y4 = b1.Y;

            //AABB early exit
            if (Math.Max(x1, x2) < Math.Min(x3, x4) || Math.Max(x3, x4) < Math.Min(x1, x2))
                return false;

            if (Math.Max(y1, y2) < Math.Min(y3, y4) || Math.Max(y3, y4) < Math.Min(y1, y2))
                return false;

            float ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3));
            float ub = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3));
            float denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
            if (Math.Abs(denom) < Settings.Epsilon)
            {
                //Lines are too close to parallel to call
                return false;
            }
            ua /= denom;
            ub /= denom;

            if ((0 < ua) && (ua < 1) && (0 < ub) && (ub < 1))
            {
                intersectionPoint.X = (x1 + ua * (x2 - x1));
                intersectionPoint.Y = (y1 + ua * (y2 - y1));
                return true;
            }

            return false;
        }

        //From Mark Bayazit's convex decomposition algorithm
        public static Vector2 LineIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            Vector2 i = Vector2.Zero;
            float a1 = p2.Y - p1.Y;
            float b1 = p1.X - p2.X;
            float c1 = a1 * p1.X + b1 * p1.Y;
            float a2 = q2.Y - q1.Y;
            float b2 = q1.X - q2.X;
            float c2 = a2 * q1.X + b2 * q1.Y;
            float det = a1 * b2 - a2 * b1;

            if (!MathUtils.FloatEquals(det, 0))
            {
                // lines are not parallel
                i.X = (b2 * c1 - b1 * c2) / det;
                i.Y = (a1 * c2 - a2 * c1) / det;
            }
            return i;
        }

        /// <summary>
        /// This method detects if two line segments (or lines) intersect,
        /// and, if so, the point of intersection. Use the <paramref name="firstIsSegment"/> and
        /// <paramref name="secondIsSegment"/> parameters to set whether the intersection point
        /// must be on the first and second line segments. Setting these
        /// both to true means you are doing a line-segment to line-segment
        /// intersection. Setting one of them to true means you are doing a
        /// line to line-segment intersection test, and so on.
        /// Note: If two line segments are coincident, then 
        /// no intersection is detected (there are actually
        /// infinite intersection points).
        /// Author: Jeremy Bell
        /// </summary>
        /// <param name="point1">The first point of the first line segment.</param>
        /// <param name="point2">The second point of the first line segment.</param>
        /// <param name="point3">The first point of the second line segment.</param>
        /// <param name="point4">The second point of the second line segment.</param>
        /// <param name="point">This is set to the intersection
        /// point if an intersection is detected.</param>
        /// <param name="firstIsSegment">Set this to true to require that the 
        /// intersection point be on the first line segment.</param>
        /// <param name="secondIsSegment">Set this to true to require that the
        /// intersection point be on the second line segment.</param>
        /// <returns>True if an intersection is detected, false otherwise.</returns>
        public static bool LineIntersect(ref Vector2 point1, ref Vector2 point2, ref Vector2 point3, ref Vector2 point4, bool firstIsSegment, bool secondIsSegment, out Vector2 point)
        {
            point = new Vector2();

            // these are reused later.
            // each lettered sub-calculation is used twice, except
            // for b and d, which are used 3 times
            float a = point4.Y - point3.Y;
            float b = point2.X - point1.X;
            float c = point4.X - point3.X;
            float d = point2.Y - point1.Y;

            // denominator to solution of linear system
            float denom = (a * b) - (c * d);

            // if denominator is 0, then lines are parallel
            if (!(denom >= -Settings.Epsilon && denom <= Settings.Epsilon))
            {
                float e = point1.Y - point3.Y;
                float f = point1.X - point3.X;
                float oneOverDenom = 1.0f / denom;

                // numerator of first equation
                float ua = (c * e) - (a * f);
                ua *= oneOverDenom;

                // check if intersection point of the two lines is on line segment 1
                if (!firstIsSegment || ua >= 0.0f && ua <= 1.0f)
                {
                    // numerator of second equation
                    float ub = (b * e) - (d * f);
                    ub *= oneOverDenom;

                    // check if intersection point of the two lines is on line segment 2
                    // means the line segments intersect, since we know it is on
                    // segment 1 as well.
                    if (!secondIsSegment || ub >= 0.0f && ub <= 1.0f)
                    {
                        // check if they are coincident (no collision in this case)
                        if (ua != 0f || ub != 0f)
                        {
                            //There is an intersection
                            point.X = point1.X + ua * b;
                            point.Y = point1.Y + ua * d;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// This method detects if two line segments (or lines) intersect,
        /// and, if so, the point of intersection. Use the <paramref name="firstIsSegment"/> and
        /// <paramref name="secondIsSegment"/> parameters to set whether the intersection point
        /// must be on the first and second line segments. Setting these
        /// both to true means you are doing a line-segment to line-segment
        /// intersection. Setting one of them to true means you are doing a
        /// line to line-segment intersection test, and so on.
        /// Note: If two line segments are coincident, then 
        /// no intersection is detected (there are actually
        /// infinite intersection points).
        /// Author: Jeremy Bell
        /// </summary>
        /// <param name="point1">The first point of the first line segment.</param>
        /// <param name="point2">The second point of the first line segment.</param>
        /// <param name="point3">The first point of the second line segment.</param>
        /// <param name="point4">The second point of the second line segment.</param>
        /// <param name="intersectionPoint">This is set to the intersection
        /// point if an intersection is detected.</param>
        /// <param name="firstIsSegment">Set this to true to require that the 
        /// intersection point be on the first line segment.</param>
        /// <param name="secondIsSegment">Set this to true to require that the
        /// intersection point be on the second line segment.</param>
        /// <returns>True if an intersection is detected, false otherwise.</returns>
        public static bool LineIntersect(Vector2 point1, Vector2 point2, Vector2 point3, Vector2 point4, bool firstIsSegment, bool secondIsSegment, out Vector2 intersectionPoint)
        {
            return LineIntersect(ref point1, ref point2, ref point3, ref point4, firstIsSegment, secondIsSegment, out intersectionPoint);
        }

        /// <summary>
        /// This method detects if two line segments intersect,
        /// and, if so, the point of intersection. 
        /// Note: If two line segments are coincident, then 
        /// no intersection is detected (there are actually
        /// infinite intersection points).
        /// </summary>
        /// <param name="point1">The first point of the first line segment.</param>
        /// <param name="point2">The second point of the first line segment.</param>
        /// <param name="point3">The first point of the second line segment.</param>
        /// <param name="point4">The second point of the second line segment.</param>
        /// <param name="intersectionPoint">This is set to the intersection
        /// point if an intersection is detected.</param>
        /// <returns>True if an intersection is detected, false otherwise.</returns>
        public static bool LineIntersect(ref Vector2 point1, ref Vector2 point2, ref Vector2 point3, ref Vector2 point4, out Vector2 intersectionPoint)
        {
            return LineIntersect(ref point1, ref point2, ref point3, ref point4, true, true, out intersectionPoint);
        }

        /// <summary>
        /// This method detects if two line segments intersect,
        /// and, if so, the point of intersection. 
        /// Note: If two line segments are coincident, then 
        /// no intersection is detected (there are actually
        /// infinite intersection points).
        /// </summary>
        /// <param name="point1">The first point of the first line segment.</param>
        /// <param name="point2">The second point of the first line segment.</param>
        /// <param name="point3">The first point of the second line segment.</param>
        /// <param name="point4">The second point of the second line segment.</param>
        /// <param name="intersectionPoint">This is set to the intersection
        /// point if an intersection is detected.</param>
        /// <returns>True if an intersection is detected, false otherwise.</returns>
        public static bool LineIntersect(Vector2 point1, Vector2 point2, Vector2 point3, Vector2 point4, out Vector2 intersectionPoint)
        {
            return LineIntersect(ref point1, ref point2, ref point3, ref point4, true, true, out intersectionPoint);
        }

        /// <summary>
        /// Get all intersections between a line segment and a list of vertices
        /// representing a polygon. The vertices reuse adjacent points, so for example
        /// edges one and two are between the first and second vertices and between the
        /// second and third vertices. The last edge is between vertex vertices.Count - 1
        /// and verts0. (ie, vertices from a Geometry or AABB)
        /// </summary>
        /// <param name="point1">The first point of the line segment to test</param>
        /// <param name="point2">The second point of the line segment to test.</param>
        /// <param name="vertices">The vertices, as described above</param>
        public static Vertices LineSegmentVerticesIntersect(ref Vector2 point1, ref Vector2 point2, Vertices vertices)
        {
            Vertices intersectionPoints = new Vertices();

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 point;
                if (LineIntersect(vertices[i], vertices[vertices.NextIndex(i)], point1, point2, true, true, out point))
                {
                    intersectionPoints.Add(point);
                }
            }

            return intersectionPoints;
        }

        /// <summary>
        /// Get all intersections between a line segment and an AABB. 
        /// </summary>
        /// <param name="point1">The first point of the line segment to test</param>
        /// <param name="point2">The second point of the line segment to test.</param>
        /// <param name="aabb">The AABB that is used for testing intersection.</param>
        public static Vertices LineSegmentAABBIntersect(ref Vector2 point1, ref Vector2 point2, AABB aabb)
        {
            return LineSegmentVerticesIntersect(ref point1, ref point2, aabb.Vertices);
        }
    }
}