using System;
using System.Collections.Generic;
using System.Text;

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
                  order.GetChatMessage(targetCharacter?.Name, sender?.CurrentHull?.DisplayName, givingOrderToSelf: targetCharacter == sender, orderOption: orderOption),
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
    }
}
