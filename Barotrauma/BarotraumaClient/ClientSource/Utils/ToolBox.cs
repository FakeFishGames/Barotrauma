using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Color = Microsoft.Xna.Framework.Color;

namespace Barotrauma
{
    public static partial class ToolBox
    {
        /// <summary>
        /// Checks if point is inside of a polygon
        /// </summary>
        /// <param name="point"></param>
        /// <param name="verts"></param>
        /// <param name="checkBoundingBox">Additional check to see if the point is within the bounding box before doing more complex math</param>
        /// <remarks>
        /// Note that the bounding box check can be more expensive than the vertex calculations in some cases.
        /// <see href="https://wrf.ecse.rpi.edu/Research/Short_Notes/pnpoly.html">Reference</see>
        /// </remarks>
        /// <returns></returns>
        public static bool PointIntersectsWithPolygon(Vector2 point, Vector2[] verts, bool checkBoundingBox = true)
        {
            var (x, y) = point;

            if (checkBoundingBox)
            {
                float minX = verts[0].X;
                float maxX = verts[0].X;
                float minY = verts[0].Y;
                float maxY = verts[0].Y;

                foreach (var (vertX, vertY) in verts)
                {
                    minX = Math.Min(vertX, minX);
                    maxX = Math.Max(vertX, maxX);
                    minY = Math.Min(vertY, minY);
                    maxY = Math.Max(vertY, maxY);
                }

                if (x < minX || x > maxX || y < minY || y > maxY ) { return false; }
            }

            bool isInside = false;

            for (int i = 0, j = verts.Length - 1; i < verts.Length; j = i++ )
            {
                if (verts[i].Y > y != verts[j].Y > y && x < (verts[j].X - verts[i].X) * (y - verts[i].Y) / (verts[j].Y - verts[i].Y) + verts[i].X )
                {
                    isInside = !isInside;
                }
            }

            return isInside;
        }

        public static Vector2 GetPolygonBoundingBoxSize(List<Vector2> verticess)
        {
            float minX = verticess[0].X;
            float maxX = verticess[0].X;
            float minY = verticess[0].Y;
            float maxY = verticess[0].Y;

            foreach (var (vertX, vertY) in verticess)
            {
                minX = Math.Min(vertX, minX);
                maxX = Math.Max(vertX, maxX);
                minY = Math.Min(vertY, minY);
                maxY = Math.Max(vertY, maxY);
            }

            return new Vector2(maxX - minX, maxY - minY);
        }

        public static List<Vector2> ScalePolygon(List<Vector2> vertices, Vector2 scale)
        {
            List<Vector2> newVertices = new List<Vector2>();

            Vector2 center = GetPolygonCentroid(vertices);

            foreach (Vector2 vert in vertices)
            {
                Vector2 centerVector = vert - center;
                Vector2 centerVectorScale = centerVector * scale;
                Vector2 scaledVector = centerVectorScale + center;
                newVertices.Add(scaledVector);
            }

            return newVertices;
        }

        public static Vector2 GetPolygonCentroid(List<Vector2> poly)
        {
            float accumulatedArea = 0.0f;
            float centerX = 0.0f;
            float centerY = 0.0f;

            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                float temp = poly[i].X * poly[j].Y - poly[j].X * poly[i].Y;
                accumulatedArea += temp;
                centerX += (poly[i].X + poly[j].X) * temp;
                centerY += (poly[i].Y + poly[j].Y) * temp;
            }

            if (Math.Abs(accumulatedArea) < 1E-7f) { return Vector2.Zero; }  // Avoid division by zero

            accumulatedArea *= 3f;
            return new Vector2(centerX / accumulatedArea, centerY / accumulatedArea);
        }

        public static List<Vector2> SnapVertices(List<Vector2> points, int treshold = 1)
        {
            Stack<Vector2> toCheck = new Stack<Vector2>();
            List<Vector2> newPoints = new List<Vector2>();

            foreach (Vector2 point in points)
            {
                toCheck.Push(point);
            }

            while (toCheck.TryPop(out Vector2 point))
            {
                Vector2 newPoint = new Vector2(point.X, point.Y);
                foreach (Vector2 otherPoint in toCheck.Concat(newPoints))
                {
                    float diffX = Math.Abs(newPoint.X - otherPoint.X),
                          diffY = Math.Abs(newPoint.Y - otherPoint.Y);

                    if (diffX <= treshold)
                    {
                        newPoint.X = Math.Max(newPoint.X, otherPoint.X);
                    }

                    if (diffY <= treshold)
                    {
                        newPoint.Y = Math.Max(newPoint.Y, otherPoint.Y);
                    }
                }
                newPoints.Add(newPoint);
            }

            return newPoints;
        }

        public static ImmutableArray<RectangleF> SnapRectangles(IEnumerable<RectangleF> rects, int treshold = 1)
        {
            List<RectangleF> list = new List<RectangleF>();

            List<Vector2> points = new List<Vector2>();

            foreach (RectangleF rect in rects)
            {
                points.Add(new Vector2(rect.Left, rect.Top));
                points.Add(new Vector2(rect.Right, rect.Top));
                points.Add(new Vector2(rect.Right, rect.Bottom));
                points.Add(new Vector2(rect.Left, rect.Bottom));
            }

            points = SnapVertices(points, treshold);

            for (int i = 0; i < points.Count; i += 4)
            {
                Vector2 topLeft = points[i];
                Vector2 bottomRight = points[i + 2];

                list.Add(new RectangleF(topLeft, bottomRight - topLeft));
            }

            return list.ToImmutableArray();
        }

        public static List<List<Vector2>> CombineRectanglesIntoShape(IEnumerable<RectangleF> rectangles)
        {
            List<Vector2> points =
                (from point in rectangles.SelectMany(RectangleToPoints)
                 group point by point
                 into g
                 where g.Count() % 2 == 1
                 select g.Key)
                .ToList();

            List<Vector2> sortedY = points.OrderBy(p => p.Y).ThenByDescending(p => p.X).ToList();
            List<Vector2> sortedX = points.OrderBy(p => p.X).ThenByDescending(p => p.Y).ToList();

            Dictionary<Vector2, Vector2> edgesH = new Dictionary<Vector2, Vector2>();
            Dictionary<Vector2, Vector2> edgesV = new Dictionary<Vector2, Vector2>();

            int i = 0;
            while (i < points.Count)
            {
                float currY = sortedY[i].Y;

                while (i < points.Count && Math.Abs(sortedY[i].Y - currY) < 0.01f)
                {
                    edgesH[sortedY[i]] = sortedY[i + 1];
                    edgesH[sortedY[i + 1]] = sortedY[i];
                    i += 2;
                }

            }

            i = 0;

            while (i < points.Count)
            {
                float currX = sortedX[i].X;
                while (i < points.Count && Math.Abs(sortedX[i].X - currX) < 0.01f)
                {
                    edgesV[sortedX[i]] = sortedX[i + 1];
                    edgesV[sortedX[i + 1]] = sortedX[i];
                    i += 2;
                }
            }

            List<List<Vector2>> polygons = new List<List<Vector2>>();

            while (edgesH.Any())
            {
                var (key, _) = edgesH.First();
                List<(Vector2 Point, int Direction)> polygon = new List<(Vector2 Point, int Direction)> { (key, 0) };
                edgesH.Remove(key);

                while (true)
                {
                    var (curr, direction) = polygon[^1];

                    if (direction == 0)
                    {
                        Vector2 nextVertex = edgesV[curr];
                        edgesV.Remove(curr);
                        polygon.Add((nextVertex, 1));
                    }
                    else
                    {
                        Vector2 nextVertex = edgesH[curr];
                        edgesH.Remove(curr);
                        polygon.Add((nextVertex, 0));
                    }

                    if (polygon[^1] == polygon[0])
                    {
                        polygon.Remove(polygon[^1]);
                        break;
                    }
                }

                List<Vector2> poly = polygon.Select(t => t.Point).ToList();

                foreach (Vector2 vertex in poly)
                {
                    if (edgesH.ContainsKey(vertex))
                    {
                        edgesH.Remove(vertex);
                    }

                    if (edgesV.ContainsKey(vertex))
                    {
                        edgesV.Remove(vertex);
                    }
                }

                polygons.Add(poly);
            }

            return polygons;

            static IEnumerable<Vector2> RectangleToPoints(RectangleF rect)
            {
                (float x1, float y1, float x2, float y2) = (rect.Left, rect.Top, rect.Right, rect.Bottom);
                Vector2[] pts = { new Vector2(x1, y1), new Vector2(x2, y1), new Vector2(x2, y2), new Vector2(x1, y2) };
                return pts;
            }
        }

        // Convert an RGB value into an HLS value.
        public static Vector3 RgbToHLS(this Color color)
        {
            return RgbToHLS(color.ToVector3());
        }

        // Convert an HLS value into an RGB value.
        public static Color HLSToRGB(Vector3 hls)
        {
            double h = hls.X, l = hls.Y, s = hls.Z;

            double p2;
            if (l <= 0.5) p2 = l * (1 + s);
            else p2 = l + s - l * s;

            double p1 = 2 * l - p2;
            double double_r, double_g, double_b;
            if (s == 0)
            {
                double_r = l;
                double_g = l;
                double_b = l;
            }
            else
            {
                double_r = QqhToRgb(p1, p2, h + 120);
                double_g = QqhToRgb(p1, p2, h);
                double_b = QqhToRgb(p1, p2, h - 120);
            }

            // Convert RGB to the 0 to 255 range.
            return new Color((byte)(double_r * 255.0), (byte)(double_g * 255.0), (byte)(double_b * 255.0));
        }

        private static double QqhToRgb(double q1, double q2, double hue)
        {
            if (hue > 360) hue -= 360;
            else if (hue < 0) hue += 360;

            if (hue < 60) return q1 + (q2 - q1) * hue / 60;
            if (hue < 180) return q2;
            if (hue < 240) return q1 + (q2 - q1) * (240 - hue) / 60;
            return q1;
        }

        /// <summary>
        /// Convert a RGB value into a HSV value.
        /// </summary>
        /// <param name="color"></param>
        /// <see href="https://www.cs.rit.edu/~ncs/color/t_convert.html">Reference</see>
        /// <returns>
        /// Vector3 where X is the hue (0-360 or NaN)
        /// Y is the saturation (0-1)
        /// Z is the value (0-1)
        /// </returns>
        public static Vector3 RGBToHSV(Color color)
        {
            float r = color.R / 255f,
                  g = color.G / 255f,
                  b = color.B / 255f;

            float h, s;

            float min = Math.Min(r, Math.Min(g, b));
            float max = Math.Max(r, Math.Max(g, b));

            float v = max;

            float delta = max - min;

            if (max != 0)
            {
                s = delta / max;
            }
            else
            {
                s = 0;
                h = -1;
                return new Vector3(h, s, v);
            }

            if (MathUtils.NearlyEqual(r, max))
            {
                h = (g - b) / delta;
            }
            else if (MathUtils.NearlyEqual(g, max))
            {
                h = 2 + (b - r) / delta;
            }
            else
            {
                h = 4 + (r - g) / delta;
            }

            h *= 60;
            if (h < 0) { h += 360; }

            return new Vector3(h, s, v);
        }


        public static Color Add(this Color sourceColor, Color color)
        {
            return new Color(
                sourceColor.R + color.R,
                sourceColor.G + color.G,
                sourceColor.B + color.B,
                sourceColor.A + color.A);
        }

        public static Color Subtract(this Color sourceColor, Color color)
        {
            return new Color(
                sourceColor.R - color.R,
                sourceColor.G - color.G,
                sourceColor.B - color.B,
                sourceColor.A - color.A);
        }

        public static LocalizedString LimitString(LocalizedString str, GUIFont font, int maxWidth)
        {
            return new LimitLString(str, font, maxWidth);
        }

        public static LocalizedString LimitString(string str, GUIFont font, int maxWidth)
            => LimitString((LocalizedString)str, font, maxWidth);

        public static string LimitString(string str, ScalableFont font, int maxWidth)
        {
            if (maxWidth <= 0 || string.IsNullOrWhiteSpace(str)) return "";

            float currWidth = font.MeasureString("...").X;
            for (int i = 0; i < str.Length; i++)
            {
                currWidth += font.MeasureString(str[i].ToString()).X;

                if (currWidth > maxWidth)
                {
                    return str.Substring(0, Math.Max(i - 2, 1)) + "...";
                }
            }

            return str;
        }

        public static Color GradientLerp(float t, params Color[] gradient)
        {
            if (!MathUtils.IsValid(t)) { return Color.Purple; }
            System.Diagnostics.Debug.Assert(gradient.Length > 0, "Empty color array passed to the GradientLerp method");
            if (gradient.Length == 0)
            {
#if DEBUG
                DebugConsole.ThrowError("Empty color array passed to the GradientLerp method.\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                GameAnalyticsManager.AddErrorEventOnce("ToolBox.GradientLerp:EmptyColorArray", GameAnalyticsManager.ErrorSeverity.Error,
                    "Empty color array passed to the GradientLerp method.\n" + Environment.StackTrace.CleanupStackTrace());
                return Color.Black;
            }

            if (t <= 0.0f || !MathUtils.IsValid(t)) { return gradient[0]; }
            if (t >= 1.0f) { return gradient[gradient.Length - 1]; }

            float scaledT = t * (gradient.Length - 1);

            return Color.Lerp(gradient[(int)scaledT], gradient[(int)Math.Min(scaledT + 1, gradient.Length - 1)], (scaledT - (int)scaledT));
        }

        public static LocalizedString WrapText(LocalizedString text, float lineLength, GUIFont font, float textScale = 1.0f)
        {
            return new WrappedLString(text, lineLength, font, textScale);
        }

        public static string WrapText(string text, float lineLength, ScalableFont font, float textScale = 1.0f)
            => font.WrapText(text, lineLength / textScale);

        public static void ParseConnectCommand(string[] args, out string name, out string endpoint, out UInt64 lobbyId)
        {
            name = null; endpoint = null; lobbyId = 0;
            if (args == null || args.Length < 2) { return; }

            if (args[0].Equals("-connect", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3) { return; }
                name = args[1];
                endpoint = args[2];
            }
            else if (args[0].Equals("+connect_lobby", StringComparison.OrdinalIgnoreCase))
            {
                UInt64.TryParse(args[1], out lobbyId);
            }
        }

        public static bool VersionNewerIgnoreRevision(Version a, Version b)
        {
            if (b.Major > a.Major) { return true; }
            if (b.Major < a.Major) { return false; }
            if (b.Minor > a.Minor) { return true; }
            if (b.Minor < a.Minor) { return false; }
            if (b.Build > a.Build) { return true; }
            if (b.Build < a.Build) { return false; }
            return false;
        }

        public static void OpenFileWithShell(string filename)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = filename,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }

        public static Vector2 PaddingSizeParentRelative(RectTransform parent, float padding)
        {
            var (sizeX, sizeY) = parent.NonScaledSize.ToVector2();

            float higher = sizeX,
                  lower = sizeY;
            bool swap = lower > higher;
            if (swap) { (higher, lower) = (lower, higher); }

            float diffY = lower - lower * padding;

            float paddingX = (higher - diffY) / higher,
                  paddingY = padding;

            if (swap) { (paddingX, paddingY) = (paddingY, paddingX); }

            return new Vector2(paddingX, paddingY);
        }
    }
}
