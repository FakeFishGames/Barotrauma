using Lidgren.Network;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public override void ClientWrite(NetOutgoingMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)ChatMessageType.Order);
            msg.Write((byte)Order.PrefabList.IndexOf(Order.Prefab));

            msg.Write(TargetCharacter.ID);
            msg.Write(TargetItem == null ? (UInt16)0 : TargetItem.ID);
            msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
        }
    }
}
