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
    }
}
