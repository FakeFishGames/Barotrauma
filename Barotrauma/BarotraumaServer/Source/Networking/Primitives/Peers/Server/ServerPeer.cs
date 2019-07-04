using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
        public delegate void StatusChangeCallback(NetworkConnection connection, ConnectionStatus status);
        public delegate void InitializationCompleteCallback(NetworkConnection connection);

        public MessageCallback OnMessageReceived;
        public StatusChangeCallback OnStatusChanged;
        public InitializationCompleteCallback OnInitializationComplete;

        public int? OwnerKey { get; protected set; }
        public NetworkConnection OwnerConnection { get; protected set; }

        public abstract void OnAuthChange(ulong steamID, ulong ownerID, ServerAuth.Status status);

        public abstract void Start();
        public abstract void Close(string msg=null);
        public abstract void Update();
        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract NetworkConnection GetConnectionByName(string name);
        public abstract NetworkConnection GetConnectionByEndPoint(object endPoint);
        public abstract NetworkConnection GetConnectionBySteamID(UInt64 steamId);
        public abstract void Disconnect(NetworkConnection conn, string msg=null);
    }
}
