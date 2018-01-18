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

        //which item is this order referring to (reactor, railgun controller, etc)
        public readonly Item TargetItem;

        //additional instructions (power up, fire at will, etc)
        public readonly string OrderOption;

        public OrderChatMessage(Order order, string orderOption, Item targetItem, Character targetCharacter, Character sender)
            : base (sender.Name, 
                  order.GetChatMessage(targetCharacter?.Name, orderOption),
                  ChatMessageType.Order, sender)
        {
            Order = order;
            OrderOption = orderOption;
            TargetCharacter = targetCharacter;
            TargetItem = targetItem;
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
            msg.Write(TargetCharacter.ID);
            msg.Write(TargetItem == null ? (UInt16)0 : TargetItem.ID);
            msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
        }
    }
}
