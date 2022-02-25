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
    }
}