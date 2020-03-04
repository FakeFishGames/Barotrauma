using System;
using System.Collections.Generic;
using System.Text;

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
            msg.Write(TargetEntity == null ? (UInt16)0 : TargetEntity.ID);
            msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
        }
    }
}
