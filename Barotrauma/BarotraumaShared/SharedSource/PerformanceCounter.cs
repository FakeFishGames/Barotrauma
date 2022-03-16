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
        private readonly Dictionary<string, Dictionary<string, TickInfo>> partialTickInfos = new Dictionary<string, Dictionary<string, TickInfo>>();

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

        private readonly List<string> tempSavedPartialIdentifiers = new List<string>();
        public IReadOnlyList<string> GetSavedPartialIdentifiers(string parentIdentifier)
        {
            lock (mutex)
            {
                tempSavedPartialIdentifiers.Clear();
                if (partialTickInfos.TryGetValue(parentIdentifier, out var tickInfos))
                {
                    tempSavedPartialIdentifiers.AddRange(tickInfos.Keys);
                }
            }
            return tempSavedPartialIdentifiers;
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

        public void AddPartialElapsedTicks(string parentIdentifier, string identifier, long ticks)
        {
            lock (mutex)
            {
                if (!partialTickInfos.TryGetValue(parentIdentifier, out var tickInfos))
                {
                    tickInfos = new Dictionary<string, TickInfo>();
                    partialTickInfos.Add(parentIdentifier, tickInfos);
                }
                if (!tickInfos.TryGetValue(identifier, out var tickInfo))
                {
                    tickInfo = new TickInfo();
                    tickInfos.Add(identifier, tickInfo);
                }
                tickInfo.ElapsedTicks.Enqueue(ticks);
                if (tickInfo.ElapsedTicks.Count > MaximumSamples)
                {
                    tickInfo.ElapsedTicks.Dequeue();
                    tickInfo.AvgTicksPerFrame = (long)tickInfo.ElapsedTicks.Average(i => i);
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

        public float GetPartialAverageElapsedMillisecs(string parentIdentifier, string identifier)
        {
            long ticksPerFrame = 0;
            lock (mutex)
            {
                if (!partialTickInfos.TryGetValue(parentIdentifier, out var tickInfos)) { return 0.0f; }
                if (!tickInfos.TryGetValue(identifier, out var tickInfo)) { return 0.0f; }
                ticksPerFrame = tickInfo.AvgTicksPerFrame;
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
