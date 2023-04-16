using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Length && messageIndex >= Messages.Length) { return; }
            if (messageIndex < 0) { return; }

            LocalizedString header = messageIndex < Headers.Length ? Headers[messageIndex] : "";
            LocalizedString message = messageIndex < Messages.Length ? Messages[messageIndex] : "";
            if (!message.IsNullOrEmpty())
            {
                message = ModifyMessage(message, color: false);
            }

            GameServer.Log($"{TextManager.Get("MissionInfo")}: {header} - {message}", ServerLog.MessageType.ServerMessage);
        }

        public virtual void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.WriteUInt16((ushort)State);
        }

        public virtual void ServerWrite(IWriteMessage msg)
        {
            msg.WriteUInt16((ushort)State);
        }
    }
}