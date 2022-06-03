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
        
        public const int MaximumSamples = 10;

        private readonly Queue<double> sampleBuffer = new Queue<double>();

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
                AverageFramesPerSecond = sampleBuffer.Average(i => i);
            }
            else
            {
                AverageFramesPerSecond = CurrentFramesPerSecond;
            }

            return true;
        }
    }    
}
