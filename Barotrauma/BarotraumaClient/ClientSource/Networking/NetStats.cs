using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

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

        private readonly Graph[] graphs;

        private readonly float[] totalValue;
        private readonly float[] lastValue;

        const float UpdateInterval = 0.1f;
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

            for (int i = 0; i < 3; i++)
            {
                graphs[i].Update(totalValue[i] / UpdateInterval);
                totalValue[i] = 0.0f;
            }

            updateTimer = UpdateInterval;
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle rect)
        {
            GUI.DrawRectangle(spriteBatch, rect, Color.Black * 0.4f, true);

            graphs[(int)NetStatType.ReceivedBytes].Draw(spriteBatch, rect, color: Color.Cyan);
            graphs[(int)NetStatType.SentBytes].Draw(spriteBatch, rect, null, color: GUI.Style.Orange);
            if (graphs[(int)NetStatType.ResentMessages].Average() > 0)
            {
                graphs[(int)NetStatType.ResentMessages].Draw(spriteBatch, rect, color: GUI.Style.Red);
                GUI.SmallFont.DrawString(spriteBatch, "Peak resent: " + graphs[(int)NetStatType.ResentMessages].LargestValue() + " messages/s",
                    new Vector2(rect.Right + 10, rect.Y + 50), GUI.Style.Red);
            }

            GUI.SmallFont.DrawString(spriteBatch,
                "Peak received: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.ReceivedBytes].LargestValue()) + "/s      " +
                "Avg received: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.ReceivedBytes].Average()) + "/s",
                new Vector2(rect.Right + 10, rect.Y + 10), Color.Cyan);

            GUI.SmallFont.DrawString(spriteBatch, "Peak sent: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.SentBytes].LargestValue()) + "/s      " +
                "Avg sent: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.SentBytes].Average()) + "/s",
                new Vector2(rect.Right + 10, rect.Y + 30), GUI.Style.Orange);
#if DEBUG
            /*int y = 10;

            foreach (KeyValuePair<string, long> msgBytesSent in server.messageCount.OrderBy(key => -key.Value))
            {
                GUI.SmallFont.DrawString(spriteBatch, msgBytesSent.Key + ": " + MathUtils.GetBytesReadable(msgBytesSent.Value),
                    new Vector2(rect.Right - 200, rect.Y + y), GUI.Style.Red);

                y += 15;
            }
            
            TODO: reimplement?*/
#endif
        }
    }
}
