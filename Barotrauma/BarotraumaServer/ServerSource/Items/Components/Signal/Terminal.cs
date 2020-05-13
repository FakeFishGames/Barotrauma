using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent, IClientSerializable, IServerSerializable
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
                GameServer.Log(GameServer.CharacterLogName(c.Character) + " entered \"" + newOutputValue + "\" on " + item.Name,
                    ServerLog.MessageType.ItemInteraction);
                OutputValue = newOutputValue;
                item.SendSignal(0, newOutputValue, "signal_out", null);
                item.CreateServerEvent(this);
            }

        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(OutputValue);
        }
    }
}