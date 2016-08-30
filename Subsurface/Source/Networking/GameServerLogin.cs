using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class UnauthenticatedClient
    {
        public NetConnection Connection;
        public int Nonce;

        public float AuthTimer;

        public UnauthenticatedClient(NetConnection connection, int nonce)
        {
            Connection = connection;
            Nonce = nonce;

            AuthTimer = 5.0f;
        }
    }

    partial class GameServer : NetworkMember, IPropertyObject
    {
        List<UnauthenticatedClient> unauthenticatedClients = new List<UnauthenticatedClient>();
        
    }
}
