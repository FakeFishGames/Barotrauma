using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
    }
}
