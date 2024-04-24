#nullable enable

namespace Barotrauma.Networking
{
    public sealed class SteamP2PAddress : P2PAddress
    {
        public readonly SteamId SteamId;

        public override string StringRepresentation => SteamId.StringRepresentation;

        public override bool IsLocalHost => false;

        public SteamP2PAddress(SteamId steamId)
        {
            SteamId = steamId;
        }

        public new static Option<SteamP2PAddress> Parse(string endpointStr)
            => SteamId.Parse(endpointStr).Select(steamId => new SteamP2PAddress(steamId));
        
        public override bool Equals(object? obj)
            => obj is SteamP2PAddress otherAddress && this == otherAddress;

        public override int GetHashCode()
            => SteamId.GetHashCode();

        public static bool operator ==(SteamP2PAddress a, SteamP2PAddress b)
            => a.SteamId == b.SteamId;

        public static bool operator !=(SteamP2PAddress a, SteamP2PAddress b)
            => !(a == b);
    }
}
