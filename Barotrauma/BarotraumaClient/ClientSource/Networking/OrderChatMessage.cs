namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ClientWrite(IWriteMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)ChatMessageType.Order);
            WriteOrder(msg);
        }
    }
}
