#nullable enable

namespace Barotrauma.Networking
{
    public abstract class AccountId
    {
        public abstract string StringRepresentation { get; }
        public abstract string EosStringRepresentation { get; }
        
        public static Option<AccountId> Parse(string str)
            => ReflectionUtils.ParseDerived<AccountId, string>(str);

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();
        
        public override string ToString() => StringRepresentation;

        public static bool operator ==(AccountId? a, AccountId? b)
            => a is null
                ? b is null
                : a.Equals(b);

        public static bool operator !=(AccountId? a, AccountId? b)
            => !(a == b);
    }
}