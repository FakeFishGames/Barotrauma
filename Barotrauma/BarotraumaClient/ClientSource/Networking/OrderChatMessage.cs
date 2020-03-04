using Lidgren.Network;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ClientWrite(IWriteMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)ChatMessageType.Order);
            msg.Write((byte)Order.PrefabList.IndexOf(Order.Prefab));

            msg.Write(TargetCharacter == null ? (UInt16)0 : TargetCharacter.ID);
            msg.Write(TargetEntity == null ? (UInt16)0 : TargetEntity.ID);
            msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
        }
    }
}
