namespace Barotrauma.Networking
{
    sealed class SteamP2PConnection : P2PConnection<SteamP2PEndpoint>
    {
        public SteamP2PConnection(SteamId steamId) : this(new SteamP2PEndpoint(steamId)) { }
        
        public SteamP2PConnection(SteamP2PEndpoint endpoint) : base(endpoint) { }
    }
}
