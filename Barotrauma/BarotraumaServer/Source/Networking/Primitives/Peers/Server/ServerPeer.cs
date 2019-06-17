using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public abstract class ServerPeer
    {
        public delegate bool OnConnectionDelegate(NetworkConnection connection, IReadMessage message);
        
        public abstract NetworkConnection GetPlayerByName(string name);
        public abstract NetworkConnection GetPlayerByEndPoint(object endPoint);
        public abstract void KickPlayer(NetworkConnection connection, string reason);
        public abstract void BanPlayer(NetworkConnection connection, string reason, TimeSpan? duration);
    }
}
