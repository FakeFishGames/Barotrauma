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

        /// <summary>
        /// Largest message sent to the server, before and after compression.
        /// </summary>
        private int largestSentPacketSize, largestSentPacketCompressedSize;
        private ClientPacketHeader largestSentPacketHeader;

        /// <summary>
        /// Largest message received from the server (after decompression).
        /// </summary>
        private int largestReceivedPacketSize;
        private ServerPacketHeader largestReceivedPacketHeader;

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

        /// <param name="msg">The message before compression</param>
        /// <param name="sentBytes">Size of the data that was actually sent (after compression, if the message was compressed)</param>
        public void RegisterSentPacket(IWriteMessage msg, int sentBytes)
        {
            if (msg.LengthBytes <= largestSentPacketSize) { return; }
            largestSentPacketSize = msg.LengthBytes;
            largestSentPacketCompressedSize = sentBytes;
            largestSentPacketHeader = (ClientPacketHeader)msg.Buffer[0];
            DebugConsole.Log($"New largest sent packet: {largestSentPacketHeader}, {MathUtils.GetBytesReadable(largestSentPacketSize)} ({MathUtils.GetBytesReadable(sentBytes)} sent)");
        }

        /// <param name="msg">The message after decompression, read position at the start of the message</param>
        public void RegisterReceivedPacket(IReadMessage msg)
        {
            //the message may not start at the beginning of the buffer (e.g. P2P messages that are read in place)
            int size = msg.LengthBytes - msg.BytePosition;
            if (size <= largestReceivedPacketSize) { return; }
            largestReceivedPacketSize = size;
            largestReceivedPacketHeader = (ServerPacketHeader)msg.Buffer[msg.BytePosition];
            DebugConsole.Log($"New largest received packet: {largestReceivedPacketHeader}, {MathUtils.GetBytesReadable(largestReceivedPacketSize)}");
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
            graphs[(int)NetStatType.SentBytes].Draw(spriteBatch, rect, null, color: GUIStyle.Orange);
            if (graphs[(int)NetStatType.ResentMessages].Average() > 0)
            {
                graphs[(int)NetStatType.ResentMessages].Draw(spriteBatch, rect, color: GUIStyle.Red);
                GUIStyle.SmallFont.DrawString(spriteBatch, "Peak resent: " + graphs[(int)NetStatType.ResentMessages].LargestValue() + " messages/s",
                    new Vector2(rect.Right + 10, rect.Y + 50), GUIStyle.Red);
            }

            GUIStyle.SmallFont.DrawString(spriteBatch,
                "Peak received: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.ReceivedBytes].LargestValue()) + "/s      " +
                "Avg received: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.ReceivedBytes].Average()) + "/s",
                new Vector2(rect.Right + 10, rect.Y + 10), Color.Cyan);

            GUIStyle.SmallFont.DrawString(spriteBatch, "Peak sent: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.SentBytes].LargestValue()) + "/s      " +
                "Avg sent: " + MathUtils.GetBytesReadable((int)graphs[(int)NetStatType.SentBytes].Average()) + "/s",
                new Vector2(rect.Right + 10, rect.Y + 30), GUIStyle.Orange);

            GUIStyle.SmallFont.DrawString(spriteBatch,
                $"Largest received packet: {MathUtils.GetBytesReadable(largestReceivedPacketSize)} ({largestReceivedPacketHeader})",
                new Vector2(rect.Right + 10, rect.Y + 70), Color.Cyan);
            GUIStyle.SmallFont.DrawString(spriteBatch,
                $"Largest sent packet: {MathUtils.GetBytesReadable(largestSentPacketSize)}, {MathUtils.GetBytesReadable(largestSentPacketCompressedSize)} compressed ({largestSentPacketHeader})",
                new Vector2(rect.Right + 10, rect.Y + 90), GUIStyle.Orange);
#if DEBUG
            /*int y = 10;

            foreach (KeyValuePair<string, long> msgBytesSent in server.messageCount.OrderBy(key => -key.Value))
            {
                GUIStyle.SmallFont.DrawString(spriteBatch, msgBytesSent.Key + ": " + MathUtils.GetBytesReadable(msgBytesSent.Value),
                    new Vector2(rect.Right - 200, rect.Y + y), GUIStyle.Red);

                y += 15;
            }
            
            TODO: reimplement?*/
#endif
        }
    }
}
