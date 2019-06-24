using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint);
        public abstract void Close(string msg=null);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
    }
}
