namespace Barotrauma.Networking
{
    interface INetSerializable { }

    /// <summary>
    /// Interface for entities that the clients can send information of to the server
    /// </summary>
    interface IClientSerializable : INetSerializable
    {
#if CLIENT
        void ClientWrite(IWriteMessage msg, object[] extraData = null);
#endif
#if SERVER
        void ServerRead(ClientNetObject type, IReadMessage msg, Client c);        
#endif
    }

    /// <summary>
    /// Interface for entities that the server can send information of to the clients
    /// </summary>
    interface IServerSerializable : INetSerializable
    {
#if SERVER
        void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null);
#endif
#if CLIENT
        void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime);
#endif
    }
}
