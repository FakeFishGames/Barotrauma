using Barotrauma.Steam;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ServerWrite(IWriteMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.WriteRangedInteger((int)ChatMessageType.Order, 0, Enum.GetValues(typeof(ChatMessageType)).Length - 1);
            msg.Write(SenderName);
            msg.Write(SenderClient != null);
            if (SenderClient != null)
            {
                msg.Write(SenderClient.AccountId.TryUnwrap(out var accountId) ? accountId.StringRepresentation : SenderClient.SessionId.ToString());
            }
            msg.Write(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.Write(Sender.ID);
            }            
            msg.Write(false); //text color (no custom text colors for order messages)
            msg.WritePadBits();
            WriteOrder(msg);
        }
    }
}
