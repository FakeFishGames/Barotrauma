using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        public void Draw(SpriteBatch spriteBatch, Rectangle rect, float? maxVal, float xOffset, Color color)
        {
            float graphMaxVal = 1.0f;
            if (maxVal == null)
            {
                graphMaxVal = LargestValue();
            }
            else if (maxVal > 0.0f)
            {
                graphMaxVal = (float)maxVal;
            }

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

            if (values.Length == 0) return;

            float lineWidth = (float)rect.Width / (float)(values.Length - 2);
            float yScale = (float)rect.Height / graphMaxVal;

            Vector2 prevPoint = new Vector2(rect.Right, rect.Bottom - (values[1] + (values[0] - values[1]) * xOffset) * yScale);
            float currX = rect.Right - ((xOffset - 1.0f) * lineWidth);
            for (int i = 1; i < values.Length - 1; i++)
            {
                currX -= lineWidth;
                Vector2 newPoint = new Vector2(currX, rect.Bottom - values[i] * yScale);
                GUI.DrawLine(spriteBatch, prevPoint, newPoint - new Vector2(1.0f, 0), color);
                prevPoint = newPoint;
            }

            Vector2 lastPoint = new Vector2(rect.X,
                rect.Bottom - (values[values.Length - 1] + (values[values.Length - 2] - values[values.Length - 1]) * xOffset) * yScale);

            GUI.DrawLine(spriteBatch, prevPoint, lastPoint, color);
        }
    }
}
