using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class ButtonTerminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            int signalIndex = msg.ReadRangedInteger(0, Signals.Length - 1);
            if (!item.CanClientAccess(c)) { return; }
            if (!SendSignal(signalIndex)) { return; }
            GameServer.Log($"{GameServer.CharacterLogName(c.Character)} sent a signal \"{Signals[signalIndex]}\" from {item.Name}", ServerLog.MessageType.ItemInteraction);
            item.CreateServerEvent(this, new object[] { signalIndex });
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            Write(msg, extraData);
        }
    }
}