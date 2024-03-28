#nullable enable

namespace Barotrauma.Networking
{
    sealed class SteamP2PEndpoint : P2PEndpoint
    {
        public SteamId SteamId => (Address as SteamP2PAddress)!.SteamId;

        public override string StringRepresentation => SteamId.StringRepresentation;

        public override LocalizedString ServerTypeString { get; } = TextManager.Get("PlayerHostedServer");
        
        public SteamP2PEndpoint(SteamId steamId) : base(new SteamP2PAddress(steamId)) { }

        public override int GetHashCode()
            => SteamId.GetHashCode();

        public override bool Equals(object? obj)
            => obj is SteamP2PEndpoint otherEndpoint
               && this.SteamId == otherEndpoint.SteamId;

        public new static Option<SteamP2PEndpoint> Parse(string endpointStr)
            => SteamId.Parse(endpointStr).Select(steamId => new SteamP2PEndpoint(steamId));

        public override P2PConnection MakeConnectionFromEndpoint()
            => new SteamP2PConnection(this);
    }
}
