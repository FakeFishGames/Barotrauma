using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public delegate void MessageCallback(IReadMessage message);
        public delegate void DisconnectCallback();
        public delegate void DisconnectMessageCallback(string message);
        public delegate void PasswordCallback(int salt, int retries);
        public delegate void InitializationCompleteCallback();
        
        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public DisconnectMessageCallback OnDisconnectMessageReceived;
        public PasswordCallback OnRequestPassword;
        public InitializationCompleteCallback OnInitializationComplete;

        public string Name;

        public string Version { get; protected set; }

        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint, int ownerKey);
        public abstract void Close(string msg = null);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
        public abstract void SendPassword(string password);

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}
