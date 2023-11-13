using System;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Submarine
    {
        public void ServerWritePosition(ReadWriteMessage tempBuffer, Client c)
        {
            subBody.Body.ServerWrite(tempBuffer);
        }
        
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            throw new Exception($"Error while writing a network event for the submarine \"{Info.Name} ({ID})\". Submarines are not even supposed to send events!");
        }
    }
}
