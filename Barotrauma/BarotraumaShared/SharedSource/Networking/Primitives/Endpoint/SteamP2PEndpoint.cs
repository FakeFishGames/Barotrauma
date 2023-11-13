#nullable enable

namespace Barotrauma.Networking
{
    sealed class SteamP2PEndpoint : Endpoint
    {
        public readonly SteamId SteamId;

        public override string StringRepresentation => SteamId.StringRepresentation;

        public override LocalizedString ServerTypeString { get; } = TextManager.Get("SteamP2PServer");
        
        public SteamP2PEndpoint(SteamId steamId) : base(new SteamP2PAddress(steamId))
        {
            SteamId = steamId;
        }

        public new static Option<SteamP2PEndpoint> Parse(string endpointStr)
            => SteamId.Parse(endpointStr).Select(steamId => new SteamP2PEndpoint(steamId));
        
        public override bool Equals(object? obj)
            => obj switch
            {
                SteamP2PEndpoint otherEndpoint => this == otherEndpoint,
                _ => false
            };

        public override int GetHashCode()
            => SteamId.GetHashCode();

        public static bool operator ==(SteamP2PEndpoint a, SteamP2PEndpoint b)
            => a.SteamId == b.SteamId;

        public static bool operator !=(SteamP2PEndpoint a, SteamP2PEndpoint b)
            => !(a == b);
    }
}
