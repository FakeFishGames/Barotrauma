using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public delegate void MessageCallback(IReadMessage message);
        public delegate void StatusChangeCallback(ConnectionStatus status);

        public MessageCallback OnConnectionValidated;
        public MessageCallback OnMessageReceived;
        public StatusChangeCallback OnStatusChanged;

        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint);
        public abstract void Close(string msg=null);
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
    }
}
