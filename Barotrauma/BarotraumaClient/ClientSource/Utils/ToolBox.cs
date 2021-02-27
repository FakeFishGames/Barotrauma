using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        /// Convert a HSV value into a RGB value.
        /// </summary>
        /// <param name="hue">Value between 0 and 360</param>
        /// <param name="saturation">Value between 0 and 1</param>
        /// <param name="value">Value between 0 and 1</param>
        /// <see href="https://en.wikipedia.org/wiki/HSL_and_HSV#HSV_to_RGB">Reference</see>
        /// <returns></returns>
        public static Color HSVToRGB(float hue, float saturation, float value)
        {
            float c = value * saturation;

            float h = Math.Clamp(hue, 0, 360) / 60f;

            float x = c * (1 - Math.Abs(h % 2 - 1));

            float r = 0,
                  g = 0,
                  b = 0;

            if (0 <= h && h <= 1)     { r = c; g = x; b = 0; }
            else if (1 < h && h <= 2) { r = x; g = c; b = 0; }
            else if (2 < h && h <= 3) { r = 0; g = c; b = x; }
            else if (3 < h && h <= 4) { r = 0; g = x; b = c; }
            else if (4 < h && h <= 5) { r = x; g = 0; b = c; }
            else if (5 < h && h <= 6) { r = c; g = 0; b = x; }

            float m = value - c;

            return new Color(r + m, g + m, b + m);
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
                GameAnalyticsManager.AddErrorEventOnce("ToolBox.GradientLerp:EmptyColorArray", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Empty color array passed to the GradientLerp method.\n" + Environment.StackTrace.CleanupStackTrace());
                return Color.Black;
            }

            if (t <= 0.0f || !MathUtils.IsValid(t)) { return gradient[0]; }
            if (t >= 1.0f) { return gradient[gradient.Length - 1]; }

            float scaledT = t * (gradient.Length - 1);

            return Color.Lerp(gradient[(int)scaledT], gradient[(int)Math.Min(scaledT + 1, gradient.Length - 1)], (scaledT - (int)scaledT));
        }

        public static string WrapText(string text, float lineLength, ScalableFont font, float textScale = 1.0f, bool playerInput = false) //TODO: could integrate this into the ScalableFont class directly
        {
            Vector2 textSize = font.MeasureString(text);
            if (textSize.X <= lineLength) { return text; }

            if (!playerInput)
            {
                text = text.Replace("\n", " \n ");
            }

            List<string> words = new List<string>();
            string currWord = "";

            for (int i = 0; i < text.Length; i++)
            {
                if (TextManager.IsCJK(text[i].ToString()))
                {
                    if (currWord.Length > 0)
                    {
                        words.Add(currWord);
                        currWord = "";
                    }
                    words.Add(text[i].ToString());
                }
                else if (text[i] == ' ')
                {
                    if (currWord.Length > 0)
                    {
                        words.Add(currWord);
                        currWord = "";
                    }
                    words.Add(string.Empty);
                }
                else
                {
                    currWord += text[i];
                }
            }
            if (currWord.Length > 0)
            {
                words.Add(currWord);
                currWord = "";
            }

            StringBuilder wrappedText = new StringBuilder();
            float linePos = 0f;
            Vector2 spaceSize = font.MeasureString(" ") * textScale;
            for (int i = 0; i < words.Count; ++i)
            {
                string currentWord = words[i];
                if (currentWord.Length == 0)
                {
                    // space
                    currentWord = " ";
                }
                else if (string.IsNullOrWhiteSpace(currentWord) && currentWord != "\n")
                {
                    continue;
                }

                Vector2 size = words[i].Length == 0 ? spaceSize : font.MeasureString(currentWord) * textScale;

                if (size.X > lineLength)
                {
                    float splitSize = 0.0f;
                    List<string> splitWord = new List<string>() { string.Empty };
                    int k = 0;

                    for (int j = 0; j < currentWord.Length; j++)
                    {
                        splitWord[k] += currentWord[j];
                        splitSize += (font.MeasureString(currentWord[j].ToString()) * textScale).X;

                        if (splitSize + linePos > lineLength)
                        {
                            linePos = splitSize = 0.0f;
                            splitWord[k] = splitWord[k].Remove(splitWord[k].Length - 1) + "\n";
                            if (splitWord[k].Length <= 1) { break; }
                            j--;
                            splitWord.Add(string.Empty);
                            k++;
                        }
                    }

                    for (int j = 0; j < splitWord.Count; j++)
                    {
                        wrappedText.Append(splitWord[j]);
                    }

                    linePos = splitSize;
                }
                else
                {
                    if (linePos + size.X < lineLength)
                    {
                        wrappedText.Append(currentWord);
                        if (currentWord == "\n")
                        {
                            linePos = 0.0f;
                        }
                        else
                        {
                            linePos += size.X;
                        }
                    }
                    else
                    {
                        wrappedText.Append("\n");
                        wrappedText.Append(currentWord);

                        linePos = size.X;
                    }
                }
            }

            if (!playerInput)
            {
                return wrappedText.ToString().Replace(" \n ", "\n");
            }
            else
            {
                return wrappedText.ToString();
            }
        }

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
    }
}
