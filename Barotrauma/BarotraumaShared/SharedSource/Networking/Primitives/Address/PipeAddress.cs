#nullable enable

namespace Barotrauma.Networking
{
    sealed class PipeAddress : Address
    {
        public override string StringRepresentation => "PIPE";

        public override bool IsLocalHost => true;

        public override bool Equals(object? obj)
            => obj is PipeAddress;

        public override int GetHashCode() => 1;
        
        public static bool operator ==(PipeAddress a, PipeAddress b)
            => true;

        public static bool operator !=(PipeAddress a, PipeAddress b)
            => !(a == b);
    }
}
