using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    class Graph
    {
        private float[] values;

        public Graph(int arraySize = 100)
        {
            values = new float[arraySize];
        }

        public float LargestValue()
        {
            float maxValue = 0.0f;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > maxValue) maxValue = values[i];
            }
            return maxValue;
        }

        public float Average()
        {
            return values.Length == 0 ? 0.0f : values.Average();
        }

        public void Update(float newValue)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                values[i] = values[i - 1];
            }
            values[0] = newValue;
        }

        public delegate void GraphDelegate(SpriteBatch spriteBatch, float value, int order, Vector2 position);

        public void Draw(SpriteBatch spriteBatch, Rectangle rect, float? maxValue = null, float xOffset = 0, Color? color = null, GraphDelegate doForEachValue = null)
        {
            color ??= Color.White;
            float graphMaxVal = 1.0f;
            if (maxValue == null)
            {
                graphMaxVal = LargestValue();
            }
            else if (maxValue > 0.0f)
            {
                graphMaxVal = (float)maxValue;
            }

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            if (values.Length == 0) { return; }

            float lineWidth = rect.Width / (float)(values.Length - 2);
            float yScale = rect.Height / graphMaxVal;

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (values[1] + (values[0] - values[1]) * xOffset) * yScale);
            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);
            for (int i = 1; i < values.Length - 1; i++)
            {
                float value = values[i];
                currX -= lineWidth;
                Vector2 newPoint = new Vector2(currX, rect.Bottom - value * yScale);
                GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color.Value);
                prevPoint = newPoint;
                doForEachValue?.Invoke(spriteBatch, value, i, newPoint);
            }
            int lastIndex = values.Length - 1;
            float lastValue = values[lastIndex];
            Vector2 lastPoint = new Vector2(rect.X, rect.Bottom - (lastValue + (values[values.Length - 2] - lastValue) * xOffset) * yScale);
            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color.Value);
            doForEachValue?.Invoke(spriteBatch, lastValue, lastIndex, lastPoint);
        }
    }
}
