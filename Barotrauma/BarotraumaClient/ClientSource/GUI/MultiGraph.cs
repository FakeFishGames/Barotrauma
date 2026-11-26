#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    /// <summary>
    /// Version of <see cref="Graph"/> that supports multiple lines
    /// and some extra features.
    /// </summary>
    internal class MultiGraph
    {
        private readonly float[][] values;
        private readonly string? tooltipFmt;

        /// <summary>
        /// Creates a new MultiGraph.
        /// </summary>
        /// <param name="dimensions">How many lines are in the graph.</param>
        /// <param name="arraySize">The data point width of the graph.</param>
        /// <param name="tooltipFmt">Optional tooltip composite format where {0} is the value rounded to 2 decimal places.</param>
        public MultiGraph(int dimensions, int arraySize, string? tooltipFmt)
        {
            this.tooltipFmt = tooltipFmt;
            values = new float[dimensions][];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = new float[arraySize];
            }
        }

        private float LargestValue()
        {
            float maxValue = 0.0f;
            foreach (float[] t in values)
            {
                foreach (float t1 in t)
                {
                    if (t1 > maxValue)
                    {
                        maxValue = t1;
                    }
                }
            }
            return maxValue;
        }

        /// <summary>
        /// Adds a new value to the graph for the given dimension.
        /// </summary>
        public void Update(int dimension, float newValue)
        {
            for (int i = values[dimension].Length - 1; i > 0; i--)
            {
                values[dimension][i] = values[dimension][i - 1];
            }
            values[dimension][0] = newValue;
        }

        /// <summary>
        /// Draws the graph with the given parameters.
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch.</param>
        /// <param name="rect">Rect to draw the graph in.</param>
        /// <param name="minimumValue">The smallest graph range height.</param>
        /// <param name="colors">
        /// Color for each graph line, array size should be equal to the dimension count.
        /// Use <see cref="Color.Transparent"/> to skip drawing a line.
        /// </param>
        /// <param name="outlineThickness">
        /// Width of both the outline and the graph lines.
        /// </param>
        public void Draw(SpriteBatch spriteBatch, RectangleF rect, float minimumValue, Span<Color> colors, float outlineThickness)
        {
            float largestValue = LargestValue();
            float graphMaxVal = MathF.Max(minimumValue, largestValue);

            GUI.DrawFilledRectangle(spriteBatch, rect.Location, rect.Size, Color.Black * 0.8f);
            GUI.DrawRectangle(spriteBatch, rect.Location, rect.Size, Color.White, isFilled: false, thickness: outlineThickness);

            RectangleF innerRect = rect;
            innerRect.Inflate(-outlineThickness, -outlineThickness);

            float index = 0;

            const float lines = 5f;

            for (float i = innerRect.Bottom; i > innerRect.Top; i -= (innerRect.Height / lines) - 0.01f)
            {
                GUI.DrawLine(spriteBatch, new Vector2(innerRect.X, i), new Vector2(innerRect.Right, i), Color.Gray * 0.5f, width: 1f);
                string label = $"{MathF.Round(index * graphMaxVal / lines, digits: 0)}";
                Vector2 textSize = GUIStyle.SmallFont.MeasureString(label);
                Vector2 textPos = new Vector2(rect.X - 5f - textSize.X, i - textSize.Y / 2);

                GUIStyle.SmallFont.DrawString(spriteBatch, label, textPos, Color.White);
                index++;
            }

            string? tooltip = null;
            for (int dimension = 0; dimension < values.Length; dimension++)
            {
                if (colors[dimension] == Color.Transparent) { continue; }

                if (values[dimension].Length == 0) { return; }
                DrawGraph(spriteBatch, graphMaxVal, innerRect, values[dimension], colors[dimension], outlineThickness, out string? newTooltip);
                if (newTooltip != null)
                {
                    tooltip = newTooltip;
                }
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                string formattedTooltip = string.IsNullOrEmpty(tooltipFmt)
                                              ? tooltip
                                              : string.Format(tooltipFmt, tooltip);

                Vector2 tooltipSize = GUIStyle.Font.MeasureString(formattedTooltip);
                Vector2 tooltipOffset = new Vector2(20f, 35f) * GUI.Scale;
                Vector2 tooltipPos = PlayerInput.MousePosition + tooltipOffset;
                RectangleF backgroundRect = new RectangleF(tooltipPos, tooltipSize);
                backgroundRect.Inflate(10f, 10f);
                GUI.DrawFilledRectangle(spriteBatch, backgroundRect, Color.Black * 0.8f);
                GUIStyle.Font.DrawString(spriteBatch, formattedTooltip, tooltipPos, Color.White);
            }
        }

        private static void DrawGraph(SpriteBatch spriteBatch, float graphMaxVal, RectangleF rect, float[] values, Color color, float lineThickness, out string? tooltip)
        {
            tooltip = null;
            float hoverDistance = 10 * GUI.Scale;

            float lineWidth = rect.Width / (values.Length - 2);
            float yScale = rect.Height / graphMaxVal;

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (values[1] + (values[0] - values[1])) * yScale);
            float currX = rect.Right;
            for (int i = 1; i < values.Length - 1; i++)
            {
                float value = values[i];
                currX -= lineWidth;
                Vector2 newPoint = new Vector2(currX, rect.Bottom - value * yScale);
                GUI.DrawLine(spriteBatch, prevPoint, newPoint - Vector2.UnitX, color, width: lineThickness);
                prevPoint = newPoint;

                if (Vector2.DistanceSquared(PlayerInput.MousePosition, newPoint) < (hoverDistance * hoverDistance))
                {
                    tooltip = $"{MathF.Round(value, digits: 2)}";
                }
            }
            int lastIndex = values.Length - 1;
            float lastValue = values[lastIndex];
            Vector2 lastPoint = new Vector2(rect.X, rect.Bottom - (lastValue + (values[^2] - lastValue)) * yScale);
            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color, width: 3f);
        }
    }
}