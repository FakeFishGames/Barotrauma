#nullable enable
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

        public bool TryUnwrap(out T outValue) => TryUnwrap<T>(out outValue);

        public bool TryUnwrap<T1>(out T1 outValue) where T1 : T
        {
            switch (this)
            {
                case Some<T> { Value: T1 value }:
                    outValue = value;
                    return true;
                default:
                    outValue = default!;
                    return false;
            }
        }

        public Option<TType> Select<TType>(Func<T, TType> selector) =>
            this switch
            {
                Some<T> { Value: var value } => Option<TType>.Some(selector.Invoke(value)),
                None<T> _ => Option<TType>.None(),
                _ => throw new ArgumentOutOfRangeException()
            };

        public abstract Option<T> Fallback(Option<T> fallback);
        public abstract T Fallback(T fallback);

        public abstract bool ValueEquals(T value);

        public override bool Equals(object? obj)
            => obj switch
            {
                Some<T> { Value: var value } => this is Some<T> { Value: { } selfValue } && selfValue.Equals(value),
                None<T> _ => IsNone(),
                T value => this is Some<T> { Value: { } selfValue } && selfValue.Equals(value),
                _ => false
            };

        public override int GetHashCode()
            => this is Some<T> { Value: { } value } ? value.GetHashCode() : 0;

        public static bool operator ==(Option<T> a, Option<T> b)
            => a.Equals(b);

        public static bool operator !=(Option<T> a, Option<T> b)
            => !(a == b);

        public abstract override string ToString();
        
        public static implicit operator Option<T>(Option.UnspecifiedNone _)
            => None();
    }

    public static class Option
    {
        public sealed class UnspecifiedNone
        {
            private UnspecifiedNone() { }
            internal static readonly UnspecifiedNone Instance = new();
        }
        
        public static UnspecifiedNone None => UnspecifiedNone.Instance;
        
        public static Option<T> Some<T>(T value) => Option<T>.Some(value);
    }
}