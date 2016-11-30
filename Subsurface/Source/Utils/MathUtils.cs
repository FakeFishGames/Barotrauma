using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    static class MathUtils
    {
        public static Vector2 SmoothStep(Vector2 v1, Vector2 v2, float amount)
        {
            return new Vector2(
                 MathHelper.SmoothStep(v1.X, v2.X, amount),
                 MathHelper.SmoothStep(v1.Y, v2.Y, amount));
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

        // a1 is line1 start, a2 is line1 end, b1 is line2 start, b2 is line2 end
        public static Vector2? GetLineIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            Vector2 b = a2 - a1;
            Vector2 d = b2 - b1;
            float bDotDPerp = b.X * d.Y - b.Y * d.X;

            // if b dot d == 0, it means the lines are parallel so have infinite intersection points
            if (bDotDPerp == 0) return null;

            Vector2 c = b1 - a1;
            float t = (c.X * d.Y - c.Y * d.X) / bDotDPerp;
            if (t < 0 || t > 1) return null;

            float u = (c.X * b.Y - c.Y * b.X) / bDotDPerp;
            if (u < 0 || u > 1) return null;

            return a1 + t * b;
        }

        public static Vector2? GetLineRectangleIntersection(Vector2 a1, Vector2 a2, Rectangle rect)
        {
            Vector2? intersection = GetLineIntersection(a1, a2, 
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.Right, rect.Y));

            if (intersection != null) return intersection;

            intersection = GetLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y-rect.Height),
                new Vector2(rect.Right, rect.Y-rect.Height));

            if (intersection != null) return intersection;

            intersection = GetLineIntersection(a1, a2,
                new Vector2(rect.X, rect.Y),
                new Vector2(rect.X, rect.Y - rect.Height));

            if (intersection != null) return intersection;

            return GetLineIntersection(a1, a2,
                new Vector2(rect.Right, rect.Y),
                new Vector2(rect.Right, rect.Y - rect.Height));
        }

        public static float LineToPointDistance(Vector2 lineA, Vector2 lineB, Vector2 point)
        {
            float xDiff = lineB.X - lineA.X;
            float yDiff = lineB.Y - lineA.Y;

            return (float)(Math.Abs(xDiff * (lineA.Y - point.Y) - yDiff * (lineA.X - point.X)) /
                Math.Sqrt(xDiff * xDiff + yDiff * yDiff));
        } 

        public static bool CircleIntersectsRectangle(Vector2 circlePos, float radius, Rectangle rect)
        {
            float xDist = Math.Abs(circlePos.X - rect.Center.X);
            int halfWidth = rect.Width / 2;
            
            if (xDist > (halfWidth + radius))   { return false; }
            if (xDist <= (halfWidth))           { return true; }

            float yDist = Math.Abs(circlePos.Y - rect.Center.Y);
            int halfHeight = rect.Height / 2;

            if (yDist > (halfHeight + radius))  { return false; }            
            if (yDist <= (halfHeight))          { return true; }

            float distSqX = xDist - halfWidth;
            float distSqY = yDist - halfHeight;

            return (distSqX * distSqX + distSqY * distSqY <= (radius * radius));
        }
        
        /// <summary>
        /// divide a convex hull into triangles
        /// </summary>
        /// <returns>List of triangle vertices (sorted counter-clockwise)</returns>
        public static List<Vector2[]> TriangulateConvexHull(List<Vector2> vertices, Vector2 center)
        {
            List<Vector2[]> triangles = new List<Vector2[]>();

            int triangleCount = vertices.Count - 2;

            vertices.Sort(new CompareCCW(center));

            int lastIndex = 1;
            for (int i = 0; i < triangleCount; i++)
            {
                Vector2[] triangleVertices = new Vector2[3];
                triangleVertices[0] = vertices[0];
                int k = 1;
                for (int j = lastIndex; j <= lastIndex + 1; j++)
                {
                    triangleVertices[k] = vertices[j];
                    k++;
                }
                lastIndex += 1;

                triangles.Add(triangleVertices);
            }

            return triangles;
        }

        public static List<Vector2> GiftWrap(List<Vector2> points)
        {
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
                    if (points[i] == currPoint) continue;
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

        public static List<Vector2[]> GenerateJaggedLine(Vector2 start, Vector2 end, int generations, float offsetAmount)
        {
            List<Vector2[]> segments = new List<Vector2[]>();

            segments.Add(new Vector2[] { start, end });
            
            for (int n = 0; n < generations; n++)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    Vector2 startSegment = segments[i][0];
                    Vector2 endSegment = segments[i][1];

                    segments.RemoveAt(i);

                    Vector2 midPoint = (startSegment + endSegment) / 2.0f;

                    Vector2 normal = Vector2.Normalize(endSegment - startSegment);
                    normal = new Vector2(-normal.Y, normal.X);
                    midPoint += normal * Rand.Range(-offsetAmount, offsetAmount, false);

                    segments.Insert(i, new Vector2[] { startSegment, midPoint });
                    segments.Insert(i + 1, new Vector2[] { midPoint, endSegment });

                    i++;
                }
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
