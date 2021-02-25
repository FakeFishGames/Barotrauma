using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.Extensions;

namespace Barotrauma
{
    //TODO: Currently this is only used for text positioning? -> move there?
    [Flags]
    public enum Alignment
    {
        CenterX = 1, Left = 2, Right = 4, CenterY = 8, Top = 16, Bottom = 32,
        TopLeft = (Top | Left), TopCenter = (CenterX | Top), TopRight = (Top | Right),
        CenterLeft = (Left | CenterY), Center = (CenterX | CenterY), CenterRight = (Right | CenterY),
        BottomLeft = (Bottom | Left), BottomCenter = (CenterX | Bottom), BottomRight = (Bottom | Right),
        Any = Left | Right | Top | Bottom | Center
    }

    static class MathUtils
    {
        public static float Percentage(float portion, float total)
        {
            return portion / total * 100;
        }

        public static int PositiveModulo(int i, int n)
        {
            return (i % n + n) % n;
        }

        public static double Distance(double x1, double y1, double x2, double y2)
        {
            double dX = x1 - x2;
            double dY = y1 - y2;
            return Math.Sqrt(dX * dX + dY * dY);
        }

        public static double DistanceSquared(double x1, double y1, double x2, double y2)
        {
            double dX = x1 - x2;
            double dY = y1 - y2;
            return dX * dX + dY * dY;
        }

        public static int DistanceSquared(int x1, int y1, int x2, int y2)
        {
            int dX = x1 - x2;
            int dY = y1 - y2;
            return dX * dX + dY * dY;
        }

        public static Vector2 SmoothStep(Vector2 v1, Vector2 v2, float amount)
        {
            return new Vector2(
                 MathHelper.SmoothStep(v1.X, v2.X, amount),
                 MathHelper.SmoothStep(v1.Y, v2.Y, amount));
        }

        public static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        public static float SmootherStep(float t)
        {
            return t * t * t * (t * (6f * t - 15f) + 10f);
        }

        public static float EaseIn(float t)
        {
            return 1f - (float)Math.Cos(t * MathHelper.PiOver2);
        }

        public static float EaseOut(float t)
        {
            return (float)Math.Sin(t * MathHelper.PiOver2);
        }

        public static Vector2 ClampLength(this Vector2 v, float length)
        {
            float currLength = v.Length();
            if (currLength > length)
            {
                return v / currLength * length;
            }
            return v;
        }

        public static bool Contains(this Rectangle rect, double x, double y)
        {
            return x > rect.X && x < rect.Right && y > rect.Y && y < rect.Bottom;
        }

        public static float Round(float value, float div)
        {
            return (value < 0.0f) ? 
                (float)Math.Ceiling(value / div) * div : 
                (float)Math.Floor(value / div) * div;
        }

        public static float RoundTowardsClosest(float value, float div)
        {
            return (float)Math.Round(value / div) * div;
        }

        public static float VectorToAngle(Vector2 vector)
        {
            return (float)Math.Atan2(vector.Y, vector.X);
        }

        public static Point ToPoint(Vector2 vector)
        {
            return new Point((int)vector.X,(int)vector.Y);
        }

        public static bool IsValid(float value)
        {
            return (!float.IsInfinity(value) && !float.IsNaN(value));
        }

        public static bool IsValid(Vector2 vector)
        {
            return (IsValid(vector.X) && IsValid(vector.Y));
        }

        public static Rectangle ExpandRect(Rectangle rect, int amount)
        {
            return new Rectangle(rect.X - amount, rect.Y + amount, rect.Width + amount * 2, rect.Height + amount * 2);
        }

        public static int VectorOrientation(Vector2 p1, Vector2 p2, Vector2 p)
        {
            // Determinant
            float Orin = (p2.X - p1.X) * (p.Y - p1.Y) - (p.X - p1.X) * (p2.Y - p1.Y);

            if (Orin > 0)
                return -1; //          (* Orientation is to the left-hand side  *)
            if (Orin < 0)
                return 1; // (* Orientation is to the right-hand side *)

            return 0; //  (* Orientation is neutral aka collinear  *)
        }

        
        public static float CurveAngle(float from, float to, float step)
        {

            from = WrapAngleTwoPi(from);
            to = WrapAngleTwoPi(to);

            if (Math.Abs(from - to) < MathHelper.Pi)
            {
                // The simple case - a straight lerp will do. 
                return MathHelper.Lerp(from, to, step);
            }

            // If we get here we have the more complex case. 
            // First, increment the lesser value to be greater. 
            if (from < to)
                from += MathHelper.TwoPi;
            else
                to += MathHelper.TwoPi;

            float retVal = MathHelper.Lerp(from, to, step);

            // Now ensure the return value is between 0 and 2pi 
            if (retVal >= MathHelper.TwoPi)
                retVal -= MathHelper.TwoPi;
            return retVal;
        }

        /// <summary>
        /// wrap the angle between 0.0f and 2pi
        /// </summary>
        public static float WrapAngleTwoPi(float angle)
        {
            if (float.IsInfinity(angle) || float.IsNegativeInfinity(angle) || float.IsNaN(angle))
            {
                return 0.0f;
            }

            while (angle < 0)
                angle += MathHelper.TwoPi;
            while (angle >= MathHelper.TwoPi)
                angle -= MathHelper.TwoPi;

            return angle;
        }

        /// <summary>
        /// wrap the angle between -pi and pi
        /// </summary>
        public static float WrapAnglePi(float angle)
        {
            if (float.IsInfinity(angle) || float.IsNegativeInfinity(angle) || float.IsNaN(angle))
            {
                return 0.0f;
            }
            // Ensure that -pi <= angle < pi for both "from" and "to" 
            while (angle < -MathHelper.Pi)
                angle += MathHelper.TwoPi;
            while (angle >= MathHelper.Pi)
                angle -= MathHelper.TwoPi;

            return angle;
        }

        public static float GetShortestAngle(float from, float to)
        {
            // Ensure that 0 <= angle < 2pi for both "from" and "to" 
            from = WrapAngleTwoPi(from);
            to = WrapAngleTwoPi(to);

            if (Math.Abs(from - to) < MathHelper.Pi)
            {
                return to - from;
            }

            // If we get here we have the more complex case. 
            // First, increment the lesser value to be greater. 
            if (from < to)
                from += MathHelper.TwoPi;
            else
                to += MathHelper.TwoPi;

            return to - from;
        }

        /// <summary>
        /// Returns the angle between the two angles, where the direction matters.
        /// </summary>
        public static float GetMidAngle(float from, float to)
        {
            float max = Math.Max(from, to);
            float min = Math.Min(from, to);
            float diff = max - min;
            if (from < to)
            {
                // Clockwise
                return from + diff / 2;
            }
            else
            {
                // CCW
                return from - diff / 2;
            }
        }

        /// <summary>
        /// solves the angle opposite to side a (parameters: lengths of each side)
        /// </summary>
        public static float SolveTriangleSSS(float a, float b, float c)
        {
            float A = (float)Math.Acos((b * b + c * c - a * a) / (2 * b * c));

            if (float.IsNaN(A)) A = 1.0f;

            return A;
        }

        public static byte AngleToByte(float angle)
        {
            angle = WrapAngleTwoPi(angle);
            angle = angle * (255.0f / MathHelper.TwoPi);
            return Convert.ToByte(angle);
        }

        public static float ByteToAngle(byte b)
        {
            float angle = (float)b;
            angle = angle * (MathHelper.TwoPi / 255.0f);
            return angle;
        }

        /// <summary>
        /// check whether line from a to b is intersecting with line from c to b
        /// </summary>
        public static bool LinesIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float denominator = ((b.X - a.X) * (d.Y - c.Y)) - ((b.Y - a.Y) * (d.X - c.X));
            float numerator1 = ((a.Y - c.Y) * (d.X - c.X)) - ((a.X - c.X) * (d.Y - c.Y));
            float numerator2 = ((a.Y - c.Y) * (b.X - a.X)) - ((a.X - c.X) * (b.Y - a.Y));

            if (denominator == 0) return numerator1 == 0 && numerator2 == 0;

            float r = numerator1 / denominator;
            float s = numerator2 / denominator;

            return (r >= 0 && r <= 1) && (s >= 0 && s <= 1);
        }

        public static bool GetLineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 intersection)
        {
            return GetLineIntersection(a1, a2, b1, b2, false, out intersection);
        }

        // a1 is line1 start, a2 is line1 end, b1 is line2 start, b2 is line2 end
        public static bool GetLineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, bool ignoreSegments, out Vector2 intersection)
        {
            intersection = Vector2.Zero;

            Vector2 b = a2 - a1;
            Vector2 d = b2 - b1;
            float bDotDPerp = b.X * d.Y - b.Y * d.X;

            // if b dot d == 0, it means the lines are parallel so have infinite intersection points
            if (bDotDPerp == 0) return false;

            Vector2 c = b1 - a1;
            float t = (c.X * d.Y - c.Y * d.X) / bDotDPerp;
            if ((t < 0 || t > 1) && !ignoreSegments) return false;

            float u = (c.X * b.Y - c.Y * b.X) / bDotDPerp;
            if ((u < 0 || u > 1) && !ignoreSegments) return false;

            intersection = a1 + t * b;
            return true;
        }
        
        public static bool GetAxisAlignedLineIntersection(Vector2 a1, Vector2 a2, Vector2 axisAligned1, Vector2 axisAligned2, bool isHorizontal, out Vector2 intersection)
        {
            intersection = Vector2.Zero;

            if (!isHorizontal)
            {
                float xDiff = axisAligned1.X - a1.X;
                if (Math.Sign(xDiff) == Math.Sign(axisAligned1.X - a2.X)) { return false; }
                
                float s = (a2.Y - a1.Y) / (a2.X - a1.X);
                float y = a1.Y + xDiff * s;

                if (axisAligned1.Y < axisAligned2.Y)
                {
                    if (y < axisAligned1.Y) return false;
                    if (y > axisAligned2.Y) return false;
                }
                else
                {
                    if (y > axisAligned1.Y) return false;
                    if (y < axisAligned2.Y) return false;
                }
                
                intersection = new Vector2(axisAligned1.X, y);
                return true;
            }
            else //horizontal line
            {
                float yDiff = axisAligned1.Y - a1.Y;
                if (Math.Sign(yDiff) == Math.Sign(axisAligned1.Y - a2.Y)) { return false; }

                float s = (a2.X - a1.X) / (a2.Y - a1.Y);
                float x = a1.X + yDiff * s;

                if (axisAligned1.X < axisAligned2.X)
                {
                    if (x < axisAligned1.X) return false;
                    if (x > axisAligned2.X) return false;
                }
                else
                {
                    if (x > axisAligned1.X) return false;
                    if (x < axisAligned2.X) return false;
                }
                
                intersection = new Vector2(x, axisAligned1.Y);
                return true;
            }
        }

        public static bool GetLineRectangleIntersection(Vector2 a1, Vector2 a2, Rectangle rect, out Vector2 intersection)
        {
            if (GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.Right, rect.Y),
                true, out intersection))
            {
                return true;
            }

            if (GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y - rect.Height),
                new Vector2(rect.Right, rect.Y - rect.Height),
                true, out intersection))
            {
                return true;
            }

            if (GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X, rect.Y - rect.Height),
                false, out intersection))
            {
                return true;
            }

            if (GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.Right, rect.Y),
                new Vector2(rect.Right, rect.Y - rect.Height),
                false, out intersection))
            {
                return true;
            }

            return false;
        }

        /*public static List<Vector2> GetLineRectangleIntersections(Vector2 a1, Vector2 a2, Rectangle rect)
        {
            List<Vector2> intersections = new List<Vector2>();

            Vector2? intersection = GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.Right, rect.Y),
                true);

            if (intersection != null) intersections.Add((Vector2)intersection);

            intersection = GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y - rect.Height),
                new Vector2(rect.Right, rect.Y - rect.Height),
                true);

            if (intersection != null) intersections.Add((Vector2)intersection);

            intersection = GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X, rect.Y - rect.Height),
                false);

            if (intersection != null) intersections.Add((Vector2)intersection);

            intersection = GetAxisAlignedLineIntersection(a1, a2,
                new Vector2(rect.Right, rect.Y),
                new Vector2(rect.Right, rect.Y - rect.Height),
                false);

            if (intersection != null) intersections.Add((Vector2)intersection);

            return intersections;
        }*/


        /// <summary>
        /// Get the intersections between a line (either infinite or a line segment) and a circle
        /// </summary>
        /// <param name="circlePos">Center of the circle</param>
        /// <param name="radius">Radius of the circle</param>
        /// <param name="point1">1st point on the line</param>
        /// <param name="point2">2nd point on the line</param>
        /// <param name="isLineSegment">Is the line a segment or infinite</param>
        /// <returns>The number of intersections</returns>
        public static int GetLineCircleIntersections(Vector2 circlePos, float radius,
            Vector2 point1, Vector2 point2, bool isLineSegment, out Vector2? intersection1, out Vector2? intersection2)
        {
            float dx, dy, A, B, C, det;

            dx = point2.X - point1.X;
            dy = point2.Y - point1.Y;

            A = dx * dx + dy * dy;
            B = 2 * (dx * (point1.X - circlePos.X) + dy * (point1.Y - circlePos.Y));
            C = (point1.X - circlePos.X) * (point1.X - circlePos.X) + (point1.Y - circlePos.Y) * (point1.Y - circlePos.Y) - radius * radius;

            det = B * B - 4 * A * C;
            if ((A <= 0.0000001) || (det < 0))
            {
                // No real solutions.
                intersection1 = null;
                intersection2 = null;
                return 0;
            }
            else if (det == 0)
            {
                // One solution.
                float t = -B / (2 * A);
                intersection1 = new Vector2(point1.X + t * dx, point1.Y + t * dy);
                intersection2 = null;
                return 1;
            }
            else
            {
                // Two solutions.
                float t1 = (float)((-B + Math.Sqrt(det)) / (2 * A));
                float t2 = (float)((-B - Math.Sqrt(det)) / (2 * A));

                //if the line is not infinite, we need to check if the intersections are on the segment
                if (isLineSegment)
                {
                    if (t1 >= 0 && t1 <= 1.0f)
                    {
                        intersection1 = point1 + new Vector2(dx, dy) * t1;
                        if (t2 >= 0 && t2 <= 1.0f)
                        {
                            //both intersections on the segment
                            intersection2 = point1 + new Vector2(dx, dy) * t2;
                            return 2;

                        }
                        //only the first intersection is on the segment
                        intersection2 = null;
                        return 1;
                    }
                    else if (t2 >= 0 && t2 <= 1.0f)
                    {
                        //only the second intersection is on the segment
                        intersection1 = point1 + new Vector2(dx, dy) * t2;
                        intersection2 = null;
                        return 1;
                    }
                    else
                    {
                        //neither is on the segment
                        intersection1 = null;
                        intersection2 = null;
                        return 0;
                    }
                }
                else
                {
                    intersection1 = point1 + new Vector2(dx, dy) * t1;
                    intersection2 = point1 + new Vector2(dx, dy) * t2;
                    return 2;
                }
            }
        }

        public static float LineToPointDistance(Vector2 lineA, Vector2 lineB, Vector2 point)
        {
            float xDiff = lineB.X - lineA.X;
            float yDiff = lineB.Y - lineA.Y;

            if (xDiff == 0 && yDiff == 0)
            {
                return Vector2.Distance(lineA, point);
            }

            return (float)(Math.Abs(xDiff * (lineA.Y - point.Y) - yDiff * (lineA.X - point.X)) /
                Math.Sqrt(xDiff * xDiff + yDiff * yDiff));
        }

        public static float LineToPointDistanceSquared(Vector2 lineA, Vector2 lineB, Vector2 point)
        {
            float xDiff = lineB.X - lineA.X;
            float yDiff = lineB.Y - lineA.Y;

            if (xDiff == 0 && yDiff == 0)
            {
                return Vector2.DistanceSquared(lineA, point);
            }

            float numerator = xDiff * (lineA.Y - point.Y) - yDiff * (lineA.X - point.X);
            return (numerator * numerator) / (xDiff * xDiff + yDiff * yDiff);
        }

        public static double LineSegmentToPointDistanceSquared(Point lineA, Point lineB, Point point)
        {
            double xDiff = lineB.X - lineA.X;
            double yDiff = lineB.Y - lineA.Y;

            if (xDiff == 0 && yDiff == 0)
            {
                double v1 = lineA.X - point.X;
                double v2 = lineA.Y - point.Y;
                return (v1 * v1) + (v2 * v2);
            }

            // Calculate the t that minimizes the distance.
            double t = ((point.X - lineA.X) * xDiff + (point.Y - lineA.Y) * yDiff) / (xDiff * xDiff + yDiff * yDiff);

            // See if this represents one of the segment's
            // end points or a point in the middle.
            if (t < 0)
            {
                xDiff = point.X - lineA.X;
                yDiff = point.Y - lineA.Y;
            }
            else if (t > 1)
            {
                xDiff = point.X - lineB.X;
                yDiff = point.Y - lineB.Y;
            }
            else
            {
                xDiff = point.X - (lineA.X + t * xDiff);
                yDiff = point.Y - (lineA.Y + t * yDiff);
            }

            return xDiff * xDiff + yDiff * yDiff;
        }

        public static Vector2 GetClosestPointOnLineSegment(Vector2 lineA, Vector2 lineB, Vector2 point)
        {
            float xDiff = lineB.X - lineA.X;
            float yDiff = lineB.Y - lineA.Y;

            if (xDiff == 0 && yDiff == 0)
            {
                return lineA;
            }

            // Calculate the t that minimizes the distance.
            float t = ((point.X - lineA.X) * xDiff + (point.Y - lineA.Y) * yDiff) / (xDiff * xDiff + yDiff * yDiff);

            // See if this represents one of the segment's
            // end points or a point in the middle.
            if (t < 0)
            {
                return lineA;
            }
            else if (t > 1)
            {
                return lineB;
            }
            else
            {
                return new Vector2(lineA.X + t * xDiff, lineA.Y + t * yDiff);
            }
        }

        public static bool CircleIntersectsRectangle(Vector2 circlePos, float radius, Rectangle rect)
        {
            int halfWidth = rect.Width / 2;
            float xDist = Math.Abs(circlePos.X - (rect.X + halfWidth));
            if (xDist > halfWidth + radius) { return false; }

            int halfHeight = rect.Height / 2;
            float yDist = Math.Abs(circlePos.Y - (rect.Y + halfHeight));
            if (yDist > halfHeight + radius) { return false; }

            if (xDist <= halfWidth || yDist <= halfHeight) { return true; }

            float distSqX = xDist - halfWidth;
            float distSqY = yDist - halfHeight;

            return distSqX * distSqX + distSqY * distSqY <= radius * radius;
        }

        /// <summary>
        /// Get a point on a circle's circumference
        /// </summary>
        /// <param name="center">Center of the circle</param>
        /// <param name="radius">Radius of the circle</param>
        /// <param name="angle">Angle (in radians) from the center</param>
        /// <returns></returns>
        public static Vector2 GetPointOnCircumference(Vector2 center, float radius, float angle)
        {
            return new Vector2(
                center.X + radius * (float)Math.Cos(angle),
                center.Y + radius * (float)Math.Sin(angle));
        }

        /// <summary>
        /// Get a specific number of evenly distributed points on a circle's circumference
        /// </summary>
        /// <param name="center">Center of the circle</param>
        /// <param name="radius">Radius of the circle</param>
        /// <param name="points">Number of points to calculate</param>
        /// <param name="firstAngle">Angle (in radians) of the first point from the center</param>
        /// <returns></returns>
        public static Vector2[] GetPointsOnCircumference(Vector2 center, float radius, int points, float firstAngle = 0.0f)
        {
            var maxAngle = (float)(2 * Math.PI);
            var angleStep = maxAngle / points;
            var coordinates = new Vector2[points];
            for (int i = 0; i < points; i++)
            {
                var angle = firstAngle + (i * angleStep);
                if (angle > maxAngle) { angle -= maxAngle; }
                coordinates[i] = GetPointOnCircumference(center, radius, angle);
            }
            return coordinates;
        }

        /// <summary>
        /// divide a convex hull into triangles
        /// </summary>
        /// <returns>List of triangle vertices (sorted counter-clockwise)</returns>
        public static List<Vector2[]> TriangulateConvexHull(List<Vector2> vertices, Vector2 center)
        {
            List<Vector2[]> triangles = new List<Vector2[]>();
            vertices.Sort(new CompareCCW(center));
            for (int i = 0; i < vertices.Count; i++)
            {
                triangles.Add(new Vector2[3] { center, vertices[i], vertices[(i + 1) % vertices.Count] });
            }
            return triangles;
        }

        public static List<Vector2> GiftWrap(List<Vector2> points)
        {
            if (points.Count == 0) return points;

            Vector2 leftMost = points[0];
            foreach (Vector2 point in points)
            {
                if (point.X < leftMost.X) leftMost = point;
            }

            List<Vector2> wrappedPoints = new List<Vector2>();

            Vector2 currPoint = leftMost;
            Vector2 endPoint;
            do
            {
                wrappedPoints.Add(currPoint);
                endPoint = points[0];

                for (int i = 1; i < points.Count; i++)
                {
                    if (points[i].NearlyEquals(currPoint)) continue;
                    if (currPoint == endPoint ||
                        MathUtils.VectorOrientation(currPoint, endPoint, points[i]) == -1)
                    {
                        endPoint = points[i];
                    }
                }
                
                currPoint = endPoint;

            }
            while (endPoint != leftMost);

            return wrappedPoints;
        }

        public static List<Vector2[]> GenerateJaggedLine(Vector2 start, Vector2 end, int iterations, float offsetAmount, Rectangle? bounds = null)
        {
            List<Vector2[]> segments = new List<Vector2[]>
            {
                new Vector2[] { start, end }
            };

            for (int n = 0; n < iterations; n++)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    Vector2 startSegment = segments[i][0];
                    Vector2 endSegment = segments[i][1];

                    segments.RemoveAt(i);

                    Vector2 midPoint = (startSegment + endSegment) / 2.0f;

                    Vector2 normal = Vector2.Normalize(endSegment - startSegment);
                    normal = new Vector2(-normal.Y, normal.X);
                    midPoint += normal * Rand.Range(-offsetAmount, offsetAmount, Rand.RandSync.Server);

                    if (bounds.HasValue)
                    {
                        if (midPoint.X < bounds.Value.X)
                        {
                            midPoint.X = bounds.Value.X + (bounds.Value.X - midPoint.X);
                        }
                        else if (midPoint.X > bounds.Value.Right)
                        {
                            midPoint.X = bounds.Value.Right - (midPoint.X - bounds.Value.Right);
                        }
                        if (midPoint.Y < bounds.Value.Y)
                        {
                            midPoint.Y = bounds.Value.Y + (bounds.Value.Y - midPoint.Y);
                        }
                        else if (midPoint.Y > bounds.Value.Bottom)
                        {
                            midPoint.Y = bounds.Value.Bottom - (midPoint.Y - bounds.Value.Bottom);
                        }
                    }

                    segments.Insert(i, new Vector2[] { startSegment, midPoint });
                    segments.Insert(i + 1, new Vector2[] { midPoint, endSegment });

                    i++;
                }

                offsetAmount *= 0.5f;
            }

            return segments;
        }

        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.### XB", e.g. "4.2 KB" or "1.434 GB"
        public static string GetBytesReadable(long i)
        {
            // Get absolute value
            long absolute_i = (i < 0 ? -i : i);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absolute_i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (i >> 50);
            }
            else if (absolute_i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (i >> 40);
            }
            else if (absolute_i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (i >> 30);
            }
            else if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return i.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable = (readable / 1024);
            // Return formatted number with suffix
            return readable.ToString("0.# ") + suffix;
        }

        public static void SplitRectanglesHorizontal(List<Rectangle> rects, Vector2 point)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (point.Y > rects[i].Y && point.Y < rects[i].Y + rects[i].Height)
                {
                    Rectangle rect1 = rects[i];
                    Rectangle rect2 = rects[i];

                    rect1.Height = (int)(point.Y - rects[i].Y);

                    rect2.Height = rects[i].Height - rect1.Height;
                    rect2.Y = rect1.Y + rect1.Height;
                    rects[i] = rect1;
                    rects.Insert(i + 1, rect2); i++;
                }
            }
        }

        public static void SplitRectanglesVertical(List<Rectangle> rects, Vector2 point)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (point.X>rects[i].X && point.X<rects[i].X+rects[i].Width)
                {
                    Rectangle rect1 = rects[i];
                    Rectangle rect2 = rects[i];
                    
                    rect1.Width = (int)(point.X-rects[i].X);

                    rect2.Width = rects[i].Width - rect1.Width;
                    rect2.X = rect1.X + rect1.Width;
                    rects[i] = rect1;
                    rects.Insert(i + 1, rect2); i++;
                }
            }

            /*for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Width <= 0 || rects[i].Height <= 0)
                {
                    rects.RemoveAt(i); i--;
                }
            }*/
        }

        /// <summary>
        /// Float comparison. Note that may still fail in some cases.
        /// </summary>
        // References: 
        // http://floating-point-gui.de/errors/comparison/
        // https://stackoverflow.com/questions/3874627/floating-point-comparison-functions-for-c-sharp
        public static bool NearlyEqual(float a, float b, float epsilon = 0.0001f)
        {
            float diff = Math.Abs(a - b);
            if (a == b)
            {
                // shortcut, handles infinities
                return true;
            }
            else if (a == 0 || b == 0 || diff < float.Epsilon)
            {
                // a or b is zero or both are extremely close to it
                // relative error is less meaningful here
                return diff < epsilon;
            }
            else
            {
                // use relative error
                return diff / (Math.Abs(a) + Math.Abs(b)) < epsilon;
            }
        }

        /// <summary>
        /// Float comparison. Note that may still fail in some cases.
        /// </summary>
        public static bool NearlyEqual(Vector2 a, Vector2 b, float epsilon = 0.0001f)
        {
            return NearlyEqual(a.X, b.X, epsilon) && NearlyEqual(a.Y, b.Y, epsilon);
        }

        /// <summary>
        /// Returns a position in a curve.
        /// </summary>
        public static Vector2 Bezier(Vector2 start, Vector2 control, Vector2 end, float t)
        {
            return Pow(1 - t, 2) * start + 2 * t * (1 - t) * control + Pow(t, 2) * end;
        }

        public static float Pow(float f, float p)
        {
            return (float)Math.Pow(f, p);
        }

        public static float Pow2(float f) => f * f;

        /// <summary>
        /// Converts the alignment to a vector where -1,-1 is the top-left corner, 0,0 the center and 1,1 bottom-right
        /// </summary>
        public static Vector2 ToVector2(this Alignment alignment)
        {
            Vector2 vector = new Vector2(0.0f,0.0f);
            if (alignment.HasFlag(Alignment.Left))
            {
                vector.X = -1.0f;
            }
            else if (alignment.HasFlag(Alignment.Right))
            {
                vector.X = 1.0f;
            }
            if (alignment.HasFlag(Alignment.Top))
            {
                vector.Y = -1.0f;
            }
            else if (alignment.HasFlag(Alignment.Bottom))
            {
                vector.Y = 1.0f;
            }
            return vector;
        }

        /// <summary>
        /// Rotates a point in 2d space around another point.
        /// Modified from:
        /// http://www.gamefromscratch.com/post/2012/11/24/GameDev-math-recipes-Rotating-one-point-around-another-point.aspx
        /// </summary>
        public static Vector2 RotatePointAroundTarget(Vector2 point, Vector2 target, float radians, bool clockWise = true)
        {
            var sin = Math.Sin(radians);
            var cos = Math.Cos(radians);
            if (!clockWise)
            {
                sin = -sin;
            }
            Vector2 dir = point - target;
            var x = (cos * dir.X) - (sin * dir.Y) + target.X;
            var y = (sin * dir.X) + (cos * dir.Y) + target.Y;
            return new Vector2((float)x, (float)y);
        }

        /// <summary>
        /// Rotates a point in 2d space around the origin
        /// </summary>
        public static Vector2 RotatePoint(Vector2 point, float radians)
        {
            var sin = Math.Sin(radians);
            var cos = Math.Cos(radians);
            var x = (cos * point.X) - (sin * point.Y);
            var y = (sin * point.X) + (cos * point.Y);
            return new Vector2((float)x, (float)y);
        }

        /// <summary>
        /// Returns the corners of an imaginary rectangle.
        /// Unlike the XNA rectangle, this can be rotated with the up parameter.
        /// </summary>
        public static Vector2[] GetImaginaryRect(Vector2 up, Vector2 center, Vector2 size)
        {
            return GetImaginaryRect(new Vector2[4], up, center, size);
        }

        /// <summary>
        /// Returns the corners of an imaginary rectangle.
        /// Unlike the XNA Rectangle, this can be rotated with the up parameter.
        /// </summary>
        public static Vector2[] GetImaginaryRect(Vector2[] corners, Vector2 up, Vector2 center, Vector2 size)
        {
            if (corners.Length != 4)
            {
                throw new Exception("Invalid length for the corners array. Must be 4.");
            }
            Vector2 halfSize = size / 2;
            Vector2 left = up.Right();
            corners[0] = center + up * halfSize.Y + left * halfSize.X;
            corners[1] = center + up * halfSize.Y - left * halfSize.X;
            corners[2] = center - up * halfSize.Y - left * halfSize.X;
            corners[3] = center - up * halfSize.Y + left * halfSize.X;
            return corners;
        }

        /// <summary>
        /// Check if a point is inside a rectangle.
        /// Unlike the XNA Rectangle, this rectangle might have been rotated.
        /// For XNA Rectangles, use the Contains instance method.
        /// </summary>
        public static bool RectangleContainsPoint(Vector2[] corners, Vector2 point)
        {
            if (corners.Length != 4)
            {
                throw new Exception("Invalid length of the corners array! Must be 4");
            }
            return RectangleContainsPoint(corners[0], corners[1], corners[2], corners[3], point);
        }

        /// <summary>
        /// Check if a point is inside a rectangle.
        /// Unlike the XNA Rectangle, this rectangle might have been rotated.
        /// For XNA Rectangles, use the Contains instance method.
        /// </summary>
        public static bool RectangleContainsPoint(Vector2 c1, Vector2 c2, Vector2 c3, Vector2 c4, Vector2 point)
        {
            return TriangleContainsPoint(c1, c2, c3, point) || TriangleContainsPoint(c1, c3, c4, point);
        }

        /// <summary>
        /// Slightly modified from https://gamedev.stackexchange.com/questions/110229/how-do-i-efficiently-check-if-a-point-is-inside-a-rotated-rectangle
        /// </summary>
        public static bool TriangleContainsPoint(Vector2 c1, Vector2 c2, Vector2 c3, Vector2 point)
        {
            // Compute vectors        
            Vector2 v0 = c3 - c1;
            Vector2 v1 = c2 - c1;
            Vector2 v2 = point - c1;

            // Compute dot products
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            // Compute barycentric coordinates
            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            // Check if the point is in triangle
            return u >= 0 && v >= 0 && (u + v) < 1;
        }

        /// <summary>
        /// Returns a scalar t from a value v between a range from min to max. Clamped between 0 and 1.
        /// </summary>
        public static float InverseLerp(float min, float max, float v)
        {
            float diff = max - min;
            // Ensure that we don't get division by zero exceptions.
            if (diff == 0) { return v >= max ? 1f : 0f; }
            return MathHelper.Clamp((v - min) / diff, 0f, 1f);
        }
    }

    class CompareCCW : IComparer<Vector2>
    {
        private Vector2 center;

        public CompareCCW(Vector2 center)
        {
            this.center = center;
        }
        public int Compare(Vector2 a, Vector2 b)
        {
            return Compare(a, b, center);
        }

        public static int Compare(Vector2 a, Vector2 b, Vector2 center)
        {
            if (a == b) return 0;
            if (a.X - center.X >= 0 && b.X - center.X < 0) return -1;
            if (a.X - center.X < 0 && b.X - center.X >= 0) return 1;
            if (a.X - center.X == 0 && b.X - center.X == 0)
            {
                if (a.Y - center.Y >= 0 || b.Y - center.Y >= 0) return Math.Sign(b.Y - a.Y);
                return Math.Sign(a.Y - b.Y);
            }

            // compute the cross product of vectors (center -> a) x (center -> b)
            float det = (a.X - center.X) * (b.Y - center.Y) - (b.X - center.X) * (a.Y - center.Y);
            if (det < 0) return -1;
            if (det > 0) return 1;

            // points a and b are on the same line from the center
            // check which point is closer to the center
            float d1 = (a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y);
            float d2 = (b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y);
            return Math.Sign(d2 - d1);
        }
    }
}
