#nullable enable
using System;

namespace Barotrauma
{
    public abstract class Either<T, U> where T : notnull where U : notnull
    {
        public static implicit operator Either<T, U>(T t) => new EitherT<T, U>(t);
        public static implicit operator Either<T, U>(U u) => new EitherU<T, U>(u);

        public static explicit operator T(Either<T, U> e) => e.TryGet(out T t) ? t : throw new InvalidCastException($"Contained object is not of type {typeof(T).Name}");
        public static explicit operator U(Either<T, U> e) => e.TryGet(out U u) ? u : throw new InvalidCastException($"Contained object is not of type {typeof(U).Name}");

        public abstract bool TryGet(out T t);
        public abstract bool TryGet(out U u);

        public abstract bool TryCast<V>(out V v);

        public abstract override string? ToString();

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();

        public static bool operator ==(Either<T, U>? a, Either<T, U>? b)
            => a is null ? b is null : a.Equals(b);

        public static bool operator !=(Either<T, U>? a, Either<T, U>? b)
            => !(a == b);
    }

    public sealed class EitherT<T, U> : Either<T, U> where T : notnull where U : notnull
    {
        public readonly T Value;

        public EitherT(T value) { Value = value; }

        public override string? ToString()
            => $"Either<{typeof(T).NameWithGenerics()}, {typeof(U).NameWithGenerics()}>({Value}: {typeof(T).NameWithGenerics()})";

        public override bool TryGet(out T t) { t = Value; return true; }
        public override bool TryGet(out U u) { u = default!; return false; }

        public override bool TryCast<V>(out V v)
        {
            if (Value is V result)
            {
                v = result;
                return true;
            }
            else
            {
                v = default!;
                return false;
            }
        }

        public override bool Equals(object? obj)
            => obj switch
            {
                EitherT<T, U> other => Value.Equals(other.Value),
                T value => Value.Equals(value),
                _ => false
            };

        public override int GetHashCode() => Value.GetHashCode();
    }

    public sealed class EitherU<T, U> : Either<T, U> where T : notnull where U : notnull
    {
        public readonly U Value;

        public EitherU(U value) { Value = value; }

        public override string? ToString()
            => $"Either<{typeof(T).NameWithGenerics()}, {typeof(U).NameWithGenerics()}>({Value}: {typeof(U).NameWithGenerics()})";

        public override bool TryGet(out T t) { t = default!; return false; }
        public override bool TryGet(out U u) { u = Value; return true; }

        public override bool TryCast<V>(out V v)
        {
            if (Value is V result)
            {
                v = result;
                return true;
            }
            else
            {
                v = default!;
                return false;
            }
        }

        public override bool Equals(object? obj)
            => obj switch
            {
                EitherU<T, U> other => Value.Equals(other.Value),
                U value => Value.Equals(value),
                _ => false
            };

        public override int GetHashCode() => Value.GetHashCode();
    }
}