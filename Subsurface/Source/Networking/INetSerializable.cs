using System;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    interface INetSerializable { }

    /// <summary>
    /// Interface for entities that the clients can send information of to the server
    /// </summary>
    interface IClientSerializable : INetSerializable
    {
        void ClientWrite(NetBuffer msg, object[] extraData = null);
        void ServerRead(NetIncomingMessage msg, Client c);        
    }

    /// <summary>
    /// Interface for entities that the server can send information of to the clients
    /// </summary>
    interface IServerSerializable : INetSerializable
    {
        void ServerWrite(NetBuffer msg, Client c, object[] extraData = null);
        void ClientRead(NetIncomingMessage msg, float sendingTime);
    }
}
