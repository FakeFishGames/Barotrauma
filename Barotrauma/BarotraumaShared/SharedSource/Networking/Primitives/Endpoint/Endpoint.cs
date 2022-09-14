#nullable enable

namespace Barotrauma.Networking
{
    abstract class Endpoint
    {
        public abstract string StringRepresentation { get; }
        
        public abstract LocalizedString ServerTypeString { get; }

        public readonly Address Address;

        public Endpoint(Address address)
        {
            Address = address;
        }
        
        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();

        public override string ToString() => StringRepresentation;

        public static Option<Endpoint> Parse(string str)
            => ReflectionUtils.ParseDerived<Endpoint, string>(str);

        public static bool operator ==(Endpoint? a, Endpoint? b)
        {
            if (a is null)
            {
                return b is null;
            }
            else
            {
                return a.Equals(b);
            }
        }

        public static bool operator !=(Endpoint? a, Endpoint? b)
            => !(a == b);
    }
}
