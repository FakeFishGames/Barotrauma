using Lidgren.Network;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Interface for entities that the clients can send information of to the server
    /// </summary>
    interface IClientSerializable
    {
        ushort NetStateID { get; }

        void ClientWrite(NetOutgoingMessage msg);
        void ServerRead(NetIncomingMessage msg);        
    }

    /// <summary>
    /// Interface for entities that the server can send information of to the clients
    /// </summary>
    interface IServerSerializable
    {
        ushort NetStateID { get; }

        void ServerWrite(NetOutgoingMessage msg);
        void ClientRead(NetIncomingMessage msg);
    }
}
