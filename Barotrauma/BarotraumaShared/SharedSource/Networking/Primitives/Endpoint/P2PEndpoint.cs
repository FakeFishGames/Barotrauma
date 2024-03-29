#nullable enable
namespace Barotrauma.Networking;

abstract class P2PEndpoint : Endpoint
{
    protected P2PEndpoint(P2PAddress address) : base(address) { }

    public abstract P2PConnection MakeConnectionFromEndpoint();

    public new static Option<P2PEndpoint> Parse(string str)
        => Endpoint.Parse(str).Bind(ep => ep is P2PEndpoint pep ? Option.Some(pep) : Option.None);
}
