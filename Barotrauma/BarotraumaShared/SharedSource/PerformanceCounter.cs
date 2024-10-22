using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma
{
    public class PerformanceCounter
    {
        private readonly object mutex = new object();

        public double AverageFramesPerSecond { get; private set; }
        public double CurrentFramesPerSecond { get; private set; }

        public double AverageFramesPerSecondInPastMinute { get; private set; }

        public const int MaximumSamples = 10;

        private readonly Queue<double> sampleBuffer = new Queue<double>();

        private readonly Queue<double> averageFramesPerSecondBuffer = new Queue<double>();

        private readonly Stopwatch timer = new Stopwatch();
        private long lastSecondMark = 0;
        private long lastMinuteMark = 0;

        public class TickInfo
        {
            public Queue<long> ElapsedTicks { get; set; } = new Queue<long>();
            public long AvgTicksPerFrame { get; set; }
        }

        private readonly Dictionary<string, Queue<long>> elapsedTicks = new Dictionary<string, Queue<long>>();
        private readonly Dictionary<string, long> avgTicksPerFrame = new Dictionary<string, long>();

#if CLIENT
        internal Graph UpdateTimeGraph = new Graph(500), DrawTimeGraph = new Graph(500);
#endif

        private readonly List<string> tempSavedIdentifiers = new List<string>();

        public IReadOnlyList<string> GetSavedIdentifiers
        {
            get 
            {
                lock (mutex)
                {
                    tempSavedIdentifiers.Clear();
                    tempSavedIdentifiers.AddRange(avgTicksPerFrame.Keys);
                }
                return tempSavedIdentifiers;
            }
        }

        public PerformanceCounter()
        {
            timer.Start();
        }

        public void AddElapsedTicks(string identifier, long ticks)
        {
            lock (mutex)
            {
                if (!elapsedTicks.ContainsKey(identifier)) { elapsedTicks.Add(identifier, new Queue<long>()); }
                elapsedTicks[identifier].Enqueue(ticks);

                if (elapsedTicks[identifier].Count > MaximumSamples)
                {
                    elapsedTicks[identifier].Dequeue();
                    avgTicksPerFrame[identifier] = (long)elapsedTicks[identifier].Average(i => i);
                }
            }
        }

        public float GetAverageElapsedMillisecs(string identifier)
        {
            long ticksPerFrame = 0;
            lock (mutex)
            {
                avgTicksPerFrame.TryGetValue(identifier, out ticksPerFrame);
            }
            return ticksPerFrame * 1000.0f / Stopwatch.Frequency;
        }

        public bool Update(double deltaTime)
        {
            if (deltaTime == 0.0f) { return false; }

            CurrentFramesPerSecond = 1.0 / deltaTime;

            sampleBuffer.Enqueue(CurrentFramesPerSecond);
            if (sampleBuffer.Count > MaximumSamples)
            {
                sampleBuffer.Dequeue();
                AverageFramesPerSecond = sampleBuffer.Average();
            }
            else
            {
                AverageFramesPerSecond = CurrentFramesPerSecond;
            }

            long currentTime = timer.ElapsedMilliseconds;
            long currentSecond = currentTime / 1000;


            if (currentSecond > lastSecondMark)
            {
                averageFramesPerSecondBuffer.Enqueue(AverageFramesPerSecond);
                lastSecondMark = currentSecond;
            }

            if (currentTime - lastMinuteMark >= 60 * 1000)
            {
                //the FPS could be even higher than this on a high-end monitor, but let's restrict it to 144 to reduce the number of distinct event IDs
                const int MaxFPS = 144;
                AverageFramesPerSecondInPastMinute = averageFramesPerSecondBuffer.Average();
                GameAnalyticsManager.AddDesignEvent($"FPS:{MathHelper.Clamp((int)AverageFramesPerSecondInPastMinute, 0, MaxFPS)}");
                GameAnalyticsManager.AddDesignEvent($"FPSLowest:{MathHelper.Clamp((int)averageFramesPerSecondBuffer.Min(), 0, MaxFPS)}");
                averageFramesPerSecondBuffer.Clear();
                lastMinuteMark = currentTime;
            }

            return true;
        }
    }    
}
