using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class ButtonTerminal : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            int signalIndex = msg.ReadRangedInteger(0, Signals.Length - 1);
            if (!item.CanClientAccess(c)) { return; }
            if (!SendSignal(signalIndex, c.Character)) { return; }
            GameServer.Log($"{GameServer.CharacterLogName(c.Character)} sent a signal \"{Signals[signalIndex]}\" from {item.Name}", ServerLog.MessageType.ItemInteraction);
            item.CreateServerEvent(this, new EventData(signalIndex));
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            Write(msg, extraData);
        }
    }
}