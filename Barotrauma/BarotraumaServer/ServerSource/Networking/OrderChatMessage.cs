using Barotrauma.Steam;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ServerWrite(in SegmentTableWriter<ServerNetSegment> segmentTable, IWriteMessage msg, Client c)
        {
            segmentTable.StartNewSegment(ServerNetSegment.ChatMessage);
            msg.WriteUInt16(NetStateID);
            msg.WriteRangedInteger((int)ChatMessageType.Order, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            msg.WriteString(Text);
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
