namespace Barotrauma.Networking;

public abstract class P2PAddress : Address
{
    public new static Option<P2PAddress> Parse(string str)
        => Address.Parse(str).Bind(addr => addr is P2PAddress p2pAddr ? Option.Some(p2pAddr) : Option.None);
}