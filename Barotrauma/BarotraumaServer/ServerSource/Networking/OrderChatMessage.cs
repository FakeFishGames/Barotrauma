namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ServerWrite(IWriteMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)ChatMessageType.Order);
            msg.Write(SenderName);
            msg.Write(SenderClient != null);
            msg.Write(SenderClient != null ?
                        ((SenderClient.SteamID != 0) ? SenderClient.SteamID : SenderClient.ID) :
                        0);
            msg.Write(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.Write(Sender.ID);
            }
            msg.WritePadBits();
            WriteOrder(msg);
        }
    }
}
