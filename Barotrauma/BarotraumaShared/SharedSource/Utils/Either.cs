using System;

namespace Barotrauma
{
    public abstract class Either<T, U>
    {
        public static implicit operator Either<T, U>(T t) => new EitherT<T, U>(t);
        public static implicit operator Either<T, U>(U u) => new EitherU<T, U>(u);

        public static explicit operator T(Either<T, U> e) => e.TryGet(out T t) ? t : throw new InvalidCastException($"Contained object is not of type {typeof(T).Name}");
        public static explicit operator U(Either<T, U> e) => e.TryGet(out U u) ? u : throw new InvalidCastException($"Contained object is not of type {typeof(U).Name}");

        public abstract bool TryGet(out T t);
        public abstract bool TryGet(out U u);

        public abstract bool TryCast<V>(out V v);

        public abstract override string ToString();
    }

    public sealed class EitherT<T, U> : Either<T, U>
    {
        public readonly T Value;

        public EitherT(T value) { Value = value; }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override bool TryGet(out T t) { t = Value; return true; }
        public override bool TryGet(out U u) { u = default; return false; }

        public override bool TryCast<V>(out V v)
        {
            if (Value is V result)
            {
                v = result;
                return true;
            }
            else
            {
                v = default;
                return false;
            }
        }
    }

    public sealed class EitherU<T, U> : Either<T, U>
    {
        public readonly U Value;

        public EitherU(U value) { Value = value; }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override bool TryGet(out T t) { t = default; return false; }
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
                v = default;
                return false;
            }
        }
    }
}