#nullable enable

namespace Barotrauma.Networking;

abstract class P2PConnection<T> : P2PConnection where T : P2PEndpoint
{
    protected P2PConnection(T endpoint) : base(endpoint) { }

    public new T Endpoint => (base.Endpoint as T)!;
}

abstract class P2PConnection : NetworkConnection<P2PEndpoint>
{
    protected P2PConnection(P2PEndpoint endpoint) : base(endpoint)
    {
        Heartbeat();
    }

    public double Timeout = 0.0;

    public void Decay(float deltaTime)
    {
        Timeout -= deltaTime;
    }

    public void Heartbeat()
    {
        Timeout = TimeoutThreshold;
    }
}
