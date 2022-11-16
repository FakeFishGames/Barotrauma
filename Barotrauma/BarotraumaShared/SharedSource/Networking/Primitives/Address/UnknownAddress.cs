#nullable enable

namespace Barotrauma.Networking
{
    sealed class UnknownAddress : Address
    {
        public override string StringRepresentation => "Hidden";

        public override bool IsLocalHost => false;

        public override bool Equals(object? obj)
            => ReferenceEquals(obj, this);

        public override int GetHashCode() => 1;
    }
}
