using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public delegate void MessageCallback(IReadMessage message);
        public delegate void StatusChangeCallback(ConnectionStatus status, string msg);
        public delegate void PasswordCallback(int nonce, int retries);
        public delegate void InitializationCompleteCallback();
        
        public MessageCallback OnMessageReceived;
        public StatusChangeCallback OnStatusChanged;
        public PasswordCallback OnRequestPassword;
        public InitializationCompleteCallback OnInitializationComplete;

        public string Name;

        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint);
        public abstract void Close(string msg=null);
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
    }
}
