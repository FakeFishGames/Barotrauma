using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Submarine
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(ID);
            IWriteMessage tempBuffer = new WriteOnlyMessage();
            subBody.Body.ServerWrite(tempBuffer, c, extraData);
            msg.Write((byte)tempBuffer.LengthBytes);
            msg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            msg.WritePadBits();
        }
    }
}
