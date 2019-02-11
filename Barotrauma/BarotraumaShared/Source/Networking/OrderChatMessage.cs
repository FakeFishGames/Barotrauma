using System;
using System.Collections.Generic;
using System.Text;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public readonly Order Order;

        //who was this order given to
        public readonly Character TargetCharacter;

        //which entity is this order referring to (hull, reactor, railgun controller, etc)
        public readonly Entity TargetEntity;

        //additional instructions (power up, fire at will, etc)
        public readonly string OrderOption;

        public OrderChatMessage(Order order, string orderOption, Entity targetEntity, Character targetCharacter, Character sender)
            : this(order, orderOption,
                  order.GetChatMessage(targetCharacter?.Name, sender?.CurrentHull?.RoomName, givingOrderToSelf: targetCharacter == sender, orderOption: orderOption),
                  targetEntity, targetCharacter, sender)
        {
        }

        public OrderChatMessage(Order order, string orderOption, string text, Entity targetEntity, Character targetCharacter, Character sender)
            : base(sender?.Name, text, ChatMessageType.Order, sender)
        {
            Order = order;
            OrderOption = orderOption;
            TargetCharacter = targetCharacter;
            TargetEntity = targetEntity;
        }

        public override void ServerWrite(NetOutgoingMessage msg, Client c)
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
