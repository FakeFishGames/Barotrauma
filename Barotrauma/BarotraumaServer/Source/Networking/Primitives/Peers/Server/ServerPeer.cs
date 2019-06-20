using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void OnConnectionDelegate(NetworkConnection connection, IReadMessage message);
        public abstract void ExecuteOnConnection(OnConnectionDelegate deleg);

        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract NetworkConnection GetPlayerByName(string name);
        public abstract NetworkConnection GetPlayerByEndPoint(object endPoint);
    }
}
