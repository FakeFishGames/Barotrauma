using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class NetStats
    {
        public enum NetStatType
        {
            SentBytes = 0,
            ReceivedBytes = 1,
            ResentMessages = 2
        }

        private Graph[] graphs;

        private float[] totalValue;
        private float[] lastValue;

        const float UpdateInterval = 1.0f;
        float updateTimer;

        public NetStats()
        {
            graphs = new Graph[3];

            totalValue = new float[3];
            lastValue = new float[3];
            for (int i = 0; i < 3; i++ )
            {                
                graphs[i] = new Graph();
            }            
        }

        public void AddValue(NetStatType statType, float value)
        {
            float valueChange = value - lastValue[(int)statType];

            totalValue[(int)statType] += valueChange;

            lastValue[(int)statType] = value;
        }

        public void Update(float deltaTime)
        {
            updateTimer -= deltaTime;

            if (updateTimer > 0.0f) return;

            for (int i = 0; i<3; i++)
            {

                graphs[i].Update(totalValue[i] * 10.0f);
                totalValue[i] = 0.0f;
            }

            updateTimer = UpdateInterval/10.0f;
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect)
        {
            GUI.DrawRectangle(spriteBatch, rect, Color.Black*0.4f, true);

            graphs[(int)NetStatType.ReceivedBytes].Draw(spriteBatch, rect, null, 0.0f, Color.Cyan);

            graphs[(int)NetStatType.SentBytes].Draw(spriteBatch, rect, null, 0.0f, Color.Orange);

            graphs[(int)NetStatType.ResentMessages].Draw(spriteBatch, rect, null, 0.0f, Color.Red);

            spriteBatch.DrawString(GUI.SmallFont, "Peak received: "+graphs[(int)NetStatType.ReceivedBytes].LargestValue()+" bytes/s", 
                new Vector2(rect.X + 10, rect.Y+10), Color.Cyan);

            spriteBatch.DrawString(GUI.SmallFont, "Peak sent: " + graphs[(int)NetStatType.SentBytes].LargestValue() + " bytes/s",
                new Vector2(rect.X + 10, rect.Y + 30), Color.Orange);

            spriteBatch.DrawString(GUI.SmallFont, "Peak resent: " + graphs[(int)NetStatType.ResentMessages].LargestValue() + " messages/s",
                new Vector2(rect.X + 10, rect.Y + 50), Color.Red);
        }
    }

    class Graph
    {
        const int ArraySize = 100;

        private float[] values;

        public Graph()
        {
            values = new float[ArraySize];
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

        public void Update(float newValue)
        {
            for (int i = values.Length-1; i > 0; i--)
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

            float lineWidth = (float)rect.Width / (float)(values.Length - 2);
            float yScale = (float)rect.Height / graphMaxVal;

            GUI.DrawRectangle(spriteBatch, rect, Color.White);

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
