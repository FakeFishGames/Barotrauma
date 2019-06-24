using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void OnConnectionDelegate(NetworkConnection connection, IReadMessage message);
        public OnConnectionDelegate OnConnection;

        public abstract void Start();
        public abstract void Close();
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract NetworkConnection GetConnectionByName(string name);
        public abstract NetworkConnection GetConnectionByEndPoint(object endPoint);
        public abstract NetworkConnection GetConnectionBySteamID(UInt64 steamId);
    }
}
