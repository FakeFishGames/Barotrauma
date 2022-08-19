namespace Barotrauma
{
    public sealed class None<T> : Option<T>
    {
        private None() { }

        public static Option<T> Create() => new None<T>();
        
        public override Option<T> Fallback(Option<T> fallback) => fallback;
        public override T Fallback(T fallback) => fallback;

        public override bool ValueEquals(T value) => false;

        public override string ToString()
            => $"None<{typeof(T).Name}>";
    }
}