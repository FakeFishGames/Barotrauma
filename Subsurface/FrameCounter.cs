using System.Collections.Generic;
using System.Linq;

namespace Subsurface
{
        public class FrameCounter
        {
            public long TotalFrames { get; private set; }
            public double TotalSeconds { get; private set; }
            public double AverageFramesPerSecond { get; private set; }
            public double CurrentFramesPerSecond { get; private set; }

            public const int MaximumSamples = 10;

            private Queue<double> _sampleBuffer = new Queue<double>();

            public bool Update(double deltaTime)
            {
                //float deltaTime = stopwatch.ElapsedMilliseconds / 1000.0f;

                if (deltaTime == 0.0f) { return false; }
                //stopwatch.Restart();

                CurrentFramesPerSecond = (1.0 / deltaTime);

                _sampleBuffer.Enqueue(CurrentFramesPerSecond);

                if (_sampleBuffer.Count > MaximumSamples)
                {
                    _sampleBuffer.Dequeue();
                    AverageFramesPerSecond = _sampleBuffer.Average(i => i);
                }
                else
                {
                    AverageFramesPerSecond = CurrentFramesPerSecond;
                }

                if (AverageFramesPerSecond < 0 || AverageFramesPerSecond > 500) { }
                  
                TotalFrames++;
                TotalSeconds += deltaTime;
                return true;
            }
        }
    
}
