using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
        public delegate void StatusChangeCallback(NetworkConnection connection, ConnectionStatus status);
        public MessageCallback OnConnectionValidated;
        public MessageCallback OnMessageReceived;
        public StatusChangeCallback OnStatusChanged;

        public abstract void Start();
        public abstract void Close(string msg=null);
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract NetworkConnection GetConnectionByName(string name);
        public abstract NetworkConnection GetConnectionByEndPoint(object endPoint);
        public abstract NetworkConnection GetConnectionBySteamID(UInt64 steamId);
    }
}
