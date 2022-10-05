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

        public static NullPackageId NULL = new();
    }

    public sealed class NullPackageId : ContentPackageId {
        public override string StringRepresentation => "";

        public override bool Equals(object? obj) {
            return false;
        }

        public override int GetHashCode() {
            return 0;
        }
	}
}