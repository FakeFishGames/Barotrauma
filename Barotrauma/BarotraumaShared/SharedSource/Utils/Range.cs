#nullable enable
using System;

namespace Barotrauma
{
    /// <summary>
    /// An inclusive range, i.e. [Start, End] where Start <= End
    /// </summary>
    public struct Range<T> where T : notnull, IComparable<T>
    {
        private T start; private T end;
        public T Start
        {
            get { return start; }
            set
            {
                start = value;
                VerifyStartLessThanEnd();
            }
        }

        public T End
        {
            get { return end; }
            set
            {
                end = value;
                VerifyEndGreaterThanStart();
            }
        }

        public bool Contains(in T v)
            => start.CompareTo(v) <= 0 && end.CompareTo(v) >= 0;

        private void VerifyStartLessThanEnd()
        {
            if (start.CompareTo(end) > 0) { throw new InvalidOperationException($"Range<{typeof(T).Name}>.Start set to a value greater than End ({start} > {end})"); }
        }

        private void VerifyEndGreaterThanStart()
        {
            if (end.CompareTo(start) < 0) { throw new InvalidOperationException($"Range<{typeof(T).Name}>.End set to a value less than Start ({end} < {start})"); }
        }

        public Range(T start, T end)
        {
            this.start = start; this.end = end;
            VerifyEndGreaterThanStart();
        }
    }
}