using System;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Submarine
    {
        public void ServerWritePosition(IWriteMessage msg, Client c)
        {
            msg.WriteUInt16(ID);
            IWriteMessage tempBuffer = new WriteOnlyMessage();
            subBody.Body.ServerWrite(tempBuffer);
            msg.WriteByte((byte)tempBuffer.LengthBytes);
            msg.WriteBytes(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            msg.WritePadBits();
        }
        
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            throw new Exception($"Error while writing a network event for the submarine \"{Info.Name} ({ID})\". Submarines are not even supposed to send events!");
        }
    }
}
