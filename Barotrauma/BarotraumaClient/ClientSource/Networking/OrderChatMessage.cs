using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ClientWrite(in SegmentTableWriter<ClientNetSegment> segmentTableWriter, IWriteMessage msg)
        {
            segmentTableWriter.StartNewSegment(ClientNetSegment.ChatMessage);
            msg.WriteUInt16(NetStateID);
            msg.WriteRangedInteger((int)ChatMessageType.Order, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            msg.WriteRangedInteger((int)ChatMode.None, 0, Enum.GetValues(typeof(ChatMode)).Length - 1);
            WriteOrder(msg);
        }
    }
}
