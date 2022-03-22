using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma
{
    public class PerformanceCounter
    {
        public double AverageFramesPerSecond { get; private set; }
        public double CurrentFramesPerSecond { get; private set; }
        
        public const int MaximumSamples = 10;

        private readonly Queue<double> sampleBuffer = new Queue<double>();

        private readonly Dictionary<string, Queue<long>> elapsedTicks = new Dictionary<string, Queue<long>>();
        private readonly Dictionary<string, long> avgTicksPerFrame = new Dictionary<string, long>();

#if CLIENT
        internal Graph UpdateTimeGraph = new Graph(500), DrawTimeGraph = new Graph(500);
#endif

        public IEnumerable<string> GetSavedIdentifiers
        {
            get { return avgTicksPerFrame.Keys; }
        }

        public void AddElapsedTicks(string identifier, long ticks)
        {
            if (!elapsedTicks.ContainsKey(identifier)) elapsedTicks.Add(identifier, new Queue<long>());
            elapsedTicks[identifier].Enqueue(ticks);

            if (elapsedTicks[identifier].Count > MaximumSamples)
            {
                elapsedTicks[identifier].Dequeue();
                avgTicksPerFrame[identifier] = (long)elapsedTicks[identifier].Average(i => i);
            }
        }

        public float GetAverageElapsedMillisecs(string identifier)
        {
            if (!avgTicksPerFrame.ContainsKey(identifier)) return 0.0f;
            return avgTicksPerFrame[identifier]  * 1000.0f / (float)Stopwatch.Frequency;
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
