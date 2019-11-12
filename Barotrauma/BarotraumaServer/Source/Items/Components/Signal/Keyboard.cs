using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            string newOutputValue = msg.ReadString();

            if (item.CanClientAccess(c))
            {
                if (newOutputValue.Length > MaxMessageLength)
                {
                    newOutputValue = newOutputValue.Substring(0, MaxMessageLength);
                }
                GameServer.Log(c.Character.LogName + " entered \"" + newOutputValue + "\" on " + item.Name,
                    ServerLog.MessageType.ItemInteraction);
                OutputValue = newOutputValue;
            }

            item.CreateServerEvent(this);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(OutputValue);
        }
    }
}