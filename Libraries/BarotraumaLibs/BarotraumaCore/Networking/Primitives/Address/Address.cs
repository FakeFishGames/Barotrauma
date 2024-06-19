#nullable enable

namespace Barotrauma.Networking
{
    public abstract class Address
    {
        public abstract string StringRepresentation { get; }

        public static Option<Address> Parse(string str)
            => ReflectionUtils.ParseDerived<Address, string>(str);

        public abstract bool IsLocalHost { get; }

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();
        
        public override string ToString() => StringRepresentation;

        public static bool operator ==(Address a, Address b)
        {
            if (a is null || b is null)
            {
                return a is null == b is null;
            }
            else
            {
                return a.Equals(b);
            }
        }


        public static bool operator !=(Address a, Address b)
            => !(a == b);
    }
}
