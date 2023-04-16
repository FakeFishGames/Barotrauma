#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma
{
    public readonly struct Option<T> where T : notnull
    {
        private readonly bool hasValue;
        private readonly T? value;

        private Option(bool hasValue, T? value)
        {
            this.hasValue = hasValue;
            this.value = value;
        }

        public bool IsSome() => hasValue;
        public bool IsNone() => !IsSome();

        public bool TryUnwrap<T1>([NotNullWhen(returnValue: true)] out T1? outValue) where T1 : T
        {
            bool hasValueOfGivenType = false;
            outValue = default;

            if (hasValue && value is T1 t1)
            {
                hasValueOfGivenType = true;
                outValue = t1;
            }

            return hasValueOfGivenType;
        }

        public bool TryUnwrap([NotNullWhen(returnValue: true)] out T? outValue)
            => TryUnwrap<T>(out outValue);

        public Option<TType> Select<TType>(Func<T, TType> selector) where TType : notnull
            => TryUnwrap(out T? selfValue) ? Option.Some(selector(selfValue)) : Option.None;

        public Option<TType> Bind<TType>(Func<T, Option<TType>> binder) where TType : notnull
            => TryUnwrap(out T? selfValue) ? binder(selfValue) : Option.None;

        public T Fallback(T fallback)
            => TryUnwrap(out var v) ? v : fallback;

        public Option<T> Fallback(Option<T> fallback)
            => IsSome() ? this : fallback;

        public static Option<T> Some(T value)
            => typeof(T) switch
            {
                var t when t == typeof(bool)
                    => throw new Exception("Option type rejects booleans"),
                {IsConstructedGenericType: true} t when t.GetGenericTypeDefinition() == typeof(Option<>)
                    => throw new Exception("Option type rejects nested Option"),
                {IsConstructedGenericType: true} t when t.GetGenericTypeDefinition() == typeof(Nullable<>)
                    => throw new Exception("Option type rejects Nullable"),
                _
                    => new Option<T>(hasValue: true, value: value ?? throw new Exception("Option type rejects null"))
            };

        public override bool Equals(object? obj)
            => obj switch
            {
                Option<T> otherOption when otherOption.IsNone()
                    => IsNone(),
                Option<T> otherOption when otherOption.TryUnwrap(out var otherValue)
                    => ValueEquals(otherValue),
                T otherValue
                    => ValueEquals(otherValue),
                _
                    => false
            };

        public bool ValueEquals(T otherValue)
            => TryUnwrap(out T? selfValue) && selfValue.Equals(otherValue);
        
        public override int GetHashCode()
            => TryUnwrap(out T? selfValue) ? selfValue.GetHashCode() : 0;

        public static bool operator ==(Option<T> a, Option<T> b)
            => a.Equals(b);

        public static bool operator !=(Option<T> a, Option<T> b)
            => !(a == b);

        public static Option<T> None()
            => default;

        public static implicit operator Option<T>(in Option.UnspecifiedNone _)
            => None();

        public override string ToString()
            => TryUnwrap(out var selfValue)
                ? $"Some<{typeof(T).Name}>({selfValue})"
                : $"None<{typeof(T).Name}>";
    }

    public static class Option
    {
        public static Option<T> Some<T>(T value) where T : notnull
            => Option<T>.Some(value);

        public static UnspecifiedNone None
            => default;

        public readonly ref struct UnspecifiedNone
        {
        }
    }
}