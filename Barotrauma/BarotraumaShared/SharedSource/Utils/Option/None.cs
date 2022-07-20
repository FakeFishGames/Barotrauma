namespace Barotrauma
{
    public sealed class None<T> : Option<T>
    {
        private None() { }

        public static Option<T> Create() => new None<T>();
    }
}