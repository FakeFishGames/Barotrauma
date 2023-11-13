using Lidgren.Network;

namespace Barotrauma.Networking
{
    sealed class LidgrenConnection : NetworkConnection
    {
        public readonly NetConnection NetConnection;

        public LidgrenConnection(NetConnection netConnection) : base(new LidgrenEndpoint(netConnection.RemoteEndPoint))
        {
            NetConnection = netConnection;
        }
    }
}
