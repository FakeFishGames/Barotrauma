#nullable enable

namespace Barotrauma
{
    public abstract class ContentPackageId
    {
        public abstract string StringRepresentation { get; }

        public override string ToString()
            => StringRepresentation;

        public abstract override bool Equals(object? obj);

        public abstract override int GetHashCode();

        public static Option<ContentPackageId> Parse(string s)
            => ReflectionUtils.ParseDerived<ContentPackageId, string>(s);
    }
}