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
        UInt32 NetStateID { get; }

        void ClientWrite(NetBuffer msg);
        void ServerRead(NetIncomingMessage msg, Client c);        
    }

    /// <summary>
    /// Interface for entities that the server can send information of to the clients
    /// </summary>
    interface IServerSerializable : INetSerializable
    {
        UInt32 NetStateID { get; }

        void ServerWrite(NetBuffer msg, Client c);
        void ClientRead(NetIncomingMessage msg, float sendingTime);
    }
}
