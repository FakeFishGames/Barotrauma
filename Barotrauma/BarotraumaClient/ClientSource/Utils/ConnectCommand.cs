#nullable enable
using Barotrauma.Networking;

namespace Barotrauma
{
    readonly struct ConnectCommand
    {
        public readonly struct NameAndEndpoint
        {
            public readonly string ServerName;
            public readonly Endpoint Endpoint;
            
            public NameAndEndpoint(string serverName, Endpoint endpoint)
            {
                ServerName = serverName;
                Endpoint = endpoint;
            }
        }

        public readonly Either<NameAndEndpoint, ulong> EndpointOrLobby;
        
        public ConnectCommand(string serverName, Endpoint endpoint)
        {
            EndpointOrLobby = new NameAndEndpoint(serverName, endpoint);
        }

        public ConnectCommand(ulong lobbyId)
        {
            EndpointOrLobby = lobbyId;
        }
    }
}
