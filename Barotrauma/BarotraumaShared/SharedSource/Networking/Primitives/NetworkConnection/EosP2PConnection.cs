#nullable enable

namespace Barotrauma.Networking;

sealed class EosP2PConnection : P2PConnection<EosP2PEndpoint>
{
    public EosP2PConnection(EosP2PEndpoint endpoint) : base(endpoint) { }
    public override bool AddressMatches(NetworkConnection other)
        => Endpoint == other.Endpoint;
}
