namespace Barotrauma.Networking
{
    sealed class SteamP2PConnection : NetworkConnection
    {
        public double Timeout = 0.0;

        public SteamP2PConnection(SteamId steamId) : this(new SteamP2PEndpoint(steamId)) { }
        
        public SteamP2PConnection(SteamP2PEndpoint endpoint) : base(endpoint)
        {
            Heartbeat();
        }

        public void Decay(float deltaTime)
        {
            Timeout -= deltaTime;
        }

        public void Heartbeat()
        {
            Timeout = TimeoutThreshold;
        }
    }
}
