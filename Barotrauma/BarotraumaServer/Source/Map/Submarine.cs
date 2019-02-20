using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma
{
    partial class Submarine
    {
        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(ID);
            NetBuffer tempBuffer = new NetBuffer();
            subBody.Body.ServerWrite(tempBuffer, c, extraData);
            msg.Write((byte)tempBuffer.LengthBytes);
            msg.Write(tempBuffer);
            msg.WritePadBits();
        }
    }
}
