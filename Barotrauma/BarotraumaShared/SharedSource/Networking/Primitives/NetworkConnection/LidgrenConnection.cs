using Lidgren.Network;

namespace Barotrauma.Networking
{
    sealed class LidgrenConnection : NetworkConnection<LidgrenEndpoint>
    {
        public readonly NetConnection NetConnection;

        public LidgrenConnection(NetConnection netConnection) : base(new LidgrenEndpoint(netConnection.RemoteEndPoint))
        {
            NetConnection = netConnection;
        }

        public override bool AddressMatches(NetworkConnection other)
            => other is LidgrenConnection { Endpoint: LidgrenEndpoint otherEndpoint }
               && Endpoint is LidgrenEndpoint endpoint
               && endpoint.Address == otherEndpoint.Address;
    }
}
