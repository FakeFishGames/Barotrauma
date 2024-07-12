#nullable enable


namespace Barotrauma.Networking;

sealed class EosP2PEndpoint : P2PEndpoint
{
    public EosInterface.ProductUserId ProductUserId => new EosInterface.ProductUserId((Address as EosP2PAddress)!.EosStringRepresentation);
    
    public EosP2PEndpoint(EosInterface.ProductUserId puid) : this(new EosP2PAddress(puid.Value)) { }
    
    public EosP2PEndpoint(EosP2PAddress address) : base(address) { }

    public override string StringRepresentation => (Address as EosP2PAddress)!.StringRepresentation;

    public override LocalizedString ServerTypeString { get; } = TextManager.Get("PlayerHostedServer");

    public override int GetHashCode()
        => (Address as EosP2PAddress)!.GetHashCode();

    public override bool Equals(object? obj)
        => obj is EosP2PEndpoint otherEndpoint
           && ProductUserId == otherEndpoint.ProductUserId;

    public new static Option<EosP2PEndpoint> Parse(string endpointStr)
        => EosP2PAddress.Parse(endpointStr).Select(eosAddress => new EosP2PEndpoint(eosAddress));

    public const string SocketName = "Barotrauma.EosP2PSocket";

    public override P2PConnection MakeConnectionFromEndpoint()
        => new EosP2PConnection(this);
}
