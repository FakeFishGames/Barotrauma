using System;

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
            msg.Write(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.Write(Sender.ID);
            }

            msg.Write((byte)Order.PrefabList.IndexOf(Order.Prefab));
            msg.Write(TargetCharacter == null ? (UInt16)0 : TargetCharacter.ID);
            msg.Write(TargetEntity is Entity ? (TargetEntity as Entity).ID : (UInt16)0);
            msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
            if (TargetEntity is OrderTarget orderTarget)
            {
                msg.Write(true);
                msg.Write(orderTarget.Position.X);
                msg.Write(orderTarget.Position.Y);
                msg.Write(orderTarget.Hull == null ? (UInt16)0 : orderTarget.Hull.ID);
            }
            else
            {
                msg.Write(false);
            }
        }
    }
}
