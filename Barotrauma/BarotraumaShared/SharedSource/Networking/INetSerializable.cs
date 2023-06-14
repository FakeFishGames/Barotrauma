namespace Barotrauma.Networking
{
    interface INetSerializable { }

    /// <summary>
    /// Interface for entities that the clients can send events to the server
    /// </summary>
    interface IClientSerializable : INetSerializable
    {
#if CLIENT
        void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null);
#endif
#if SERVER
        void ServerEventRead(IReadMessage msg, Client c);        
#endif
    }

    /// <summary>
    /// Interface for entities that the server can send events to the clients
    /// </summary>
    interface IServerSerializable : INetSerializable
    {
#if SERVER
        void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null);
#endif
#if CLIENT
        void ClientEventRead(IReadMessage msg, float sendingTime);
#endif
    }

    /// <summary>
    /// Interface for entities that handle ServerNetObject.ENTITY_POSITION
    /// </summary>
    interface IServerPositionSync : IServerSerializable
    {
#if SERVER
        void ServerWritePosition(ReadWriteMessage tempBuffer, Client c);
#endif
#if CLIENT
        void ClientReadPosition(IReadMessage msg, float sendingTime);
#endif
    }
}
