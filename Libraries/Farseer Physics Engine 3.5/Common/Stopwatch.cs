/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

#if SILVERLIGHT
using System;

namespace FarseerPhysics.Common
{
    // Source: http://www.wiredprairie.us/blog/index.php/archives/723

    /// <summary>
    /// An emulation for the Stopwatch class for Windows Phone and Silverlight.
    /// </summary>
    public sealed class Stopwatch
    {
        private long _startTick;
        private long _elapsed;
        private bool _isRunning;

        /// <summary>
        /// Creates a new instance of the class and starts the watch immediately.
        /// </summary>
        /// <returns>An instance of Stopwatch, running.</returns>
        public static Stopwatch StartNew()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            return sw;
        }

        /// <summary>
        /// Creates an instance of the Stopwatch class.
        /// </summary>
        public Stopwatch() { }

        /// <summary>
        /// Completely resets and deactivates the timer.
        /// </summary>
        public void Reset()
        {
            _elapsed = 0;
            _isRunning = false;
            _startTick = 0;
        }

        /// <summary>
        /// Begins the timer.
        /// </summary>
        public void Start()
        {
            if (!_isRunning)
            {
                _startTick = GetCurrentTicks();
                _isRunning = true;
            }
        }

        /// <summary>
        /// Stops the current timer.
        /// </summary>
        public void Stop()
        {
            if (_isRunning)
            {
                _elapsed += GetCurrentTicks() - _startTick;
                _isRunning = false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the instance is currently recording.
        /// </summary>
        public bool IsRunning
        {
            get { return _isRunning; }
        }

        /// <summary>
        /// Gets the Elapsed time as a Timespan.
        /// </summary>
        public TimeSpan Elapsed
        {
            get { return TimeSpan.FromMilliseconds(ElapsedMilliseconds); }
        }

        /// <summary>
        /// Gets the Elapsed time as the total number of milliseconds.
        /// </summary>
        public long ElapsedMilliseconds
        {
            get { return GetCurrentElapsedTicks() / TimeSpan.TicksPerMillisecond; }
        }

        /// <summary>
        /// Gets the Elapsed time as the total number of ticks (which is faked
        /// as Silverlight doesn't have a way to get at the actual "Ticks")
        /// </summary>
        public long ElapsedTicks
        {
            get { return GetCurrentElapsedTicks(); }
        }

        private long GetCurrentElapsedTicks()
        {
            return _elapsed + (IsRunning ? (GetCurrentTicks() - _startTick) : 0);
        }

        private long GetCurrentTicks()
        {
            // TickCount: Gets the number of milliseconds elapsed since the system started.
            return Environment.TickCount * TimeSpan.TicksPerMillisecond;
        }
    }
}
#endif