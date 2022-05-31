using System;

namespace Barotrauma
{
    /// <summary>
    /// Implementation of <a href="https://en.wikipedia.org/wiki/Option_type">Option type</a>.
    /// </summary>
    /// <remarks>
    /// Credit <a href="https://github.com/Jlobblet/FunctionalStuff/tree/main/src/FunctionalStuff/Option">Jlobblet</a>
    /// </remarks>
    public abstract class Option<T>
    {
        public static Option<T> Some(T value) => Some<T>.Create(value);
        public static Option<T> None() => None<T>.Create();
        public bool IsNone() => this is None<T>;
        public bool IsSome() => this is Some<T>;

        public bool TryUnwrap(out T outValue)
        {
            switch (this)
            {
                case Some<T> { Value: var value }:
                    outValue = value;
                    return true;
                case None<T> _:
                    outValue = default;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public Option<TType> Select<TType>(Func<T, TType> selector) =>
            this switch
            {
                Some<T> { Value: var value } => Option<TType>.Some(selector.Invoke(value)),
                None<T> _ => Option<TType>.None(),
                _ => throw new ArgumentOutOfRangeException()
            };
    }
}