using Barotrauma.Steam;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ServerWrite(IWriteMessage msg, Client c)
        {
            msg.WriteByte((byte)ServerNetObject.CHAT_MESSAGE);
            msg.WriteUInt16(NetStateID);
            msg.WriteRangedInteger((int)ChatMessageType.Order, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            msg.WriteString(SenderName);
            msg.WriteBoolean(SenderClient != null);
            if (SenderClient != null)
            {
                msg.WriteString(SenderClient.AccountId.TryUnwrap(out var accountId) ? accountId.StringRepresentation : SenderClient.SessionId.ToString());
            }
            msg.WriteBoolean(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.WriteUInt16(Sender.ID);
            }
            msg.WriteBoolean(false); //text color (no custom text colors for order messages)
            msg.WritePadBits();
            WriteOrder(msg);
        }
    }
}
