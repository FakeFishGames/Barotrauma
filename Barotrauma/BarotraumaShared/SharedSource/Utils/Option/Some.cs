using System;

namespace Barotrauma
{
    public sealed class Some<T> : Option<T>
    {
        public readonly T Value;

        private Some(T value)
        {
            if (value is null) { throw new ArgumentNullException(nameof(value), "Some<T> cannot contain null"); }
            Value = value;
        }

        public static Option<T> Create(T value) => new Some<T>(value);
    }
}