#nullable enable

namespace Barotrauma.Networking
{
    abstract class Address
    {
        public abstract string StringRepresentation { get; }

        public static Option<Address> Parse(string str)
            => ReflectionUtils.ParseDerived<Address>(str);

        public abstract bool IsLocalHost { get; }

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();
        
        public override string ToString() => StringRepresentation;

        public static bool operator ==(Address a, Address b)
            => a.Equals(b);

        public static bool operator !=(Address a, Address b)
            => !(a == b);
    }
}
