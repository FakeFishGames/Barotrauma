using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void MessageDelegate(NetworkConnection connection, IReadMessage message);
        public delegate void StatusChangeDelegate(NetworkConnection connection, ConnectionStatus status);
        public MessageDelegate OnConnectionValidated;
        public MessageDelegate OnMessageReceived;
        public StatusChangeDelegate OnStatusChanged;

        public abstract void Start();
        public abstract void Close(string msg=null);
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract NetworkConnection GetConnectionByName(string name);
        public abstract NetworkConnection GetConnectionByEndPoint(object endPoint);
        public abstract NetworkConnection GetConnectionBySteamID(UInt64 steamId);
    }
}
