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
                GameServer.Log(c.Character.LogName + " set the output value of " + item.Name + " to " + newOutputValue,
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