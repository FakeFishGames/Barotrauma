using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        protected struct ClientContentPackage
        {
            public string Name;
            public string Hash;

            public ClientContentPackage(string name, string hash)
            {
                Name = name; Hash = hash;
            }
        }

        protected string GetPackageStr(ContentPackage contentPackage)
        {
            return "\"" + contentPackage.Name + "\" (hash " + contentPackage.MD5hash.ShortHash + ")";
        }
        protected string GetPackageStr(ClientContentPackage contentPackage)
        {
            return "\"" + contentPackage.Name + "\" (hash " + Md5Hash.GetShortHash(contentPackage.Hash) + ")";
        }

        public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
        public delegate void DisconnectCallback(NetworkConnection connection, string reason);
        public delegate void InitializationCompleteCallback(NetworkConnection connection);
        public delegate void ShutdownCallback();
        public delegate void OwnerDeterminedCallback(NetworkConnection connection);

        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public InitializationCompleteCallback OnInitializationComplete;
        public ShutdownCallback OnShutdown;
        public OwnerDeterminedCallback OnOwnerDetermined;

        protected int? ownerKey;

        public NetworkConnection OwnerConnection { get; protected set; }

        public abstract void InitializeSteamServerCallbacks(Facepunch.Steamworks.Server steamSrvr);

        public abstract void Start();
        public abstract void Close(string msg = null);
        public abstract void Update(float deltaTime);
        

        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod);
        public abstract void Disconnect(NetworkConnection conn, string msg = null);
    }
}
