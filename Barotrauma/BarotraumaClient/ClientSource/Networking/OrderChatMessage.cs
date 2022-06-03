using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ClientWrite(IWriteMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.WriteRangedInteger((int)ChatMessageType.Order, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            WriteOrder(msg);
        }
    }
}
