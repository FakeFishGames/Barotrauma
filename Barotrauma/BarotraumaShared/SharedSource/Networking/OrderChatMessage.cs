using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public readonly Order Order;

        //who was this order given to
        public readonly Character TargetCharacter;

        //which entity is this order referring to (hull, reactor, railgun controller, etc)
        public readonly ISpatialEntity TargetEntity;

        //additional instructions (power up, fire at will, etc)
        public readonly string OrderOption;

        public readonly int OrderPriority;

        /// <summary>
        /// Used when the order targets a wall
        /// </summary>
        public int? WallSectionIndex { get; set; }

        public OrderChatMessage(Order order, string orderOption, int priority, ISpatialEntity targetEntity, Character targetCharacter, Character sender)
            : this(order, orderOption, priority,
                   order?.GetChatMessage(targetCharacter?.Name, sender?.CurrentHull?.DisplayName, givingOrderToSelf: targetCharacter == sender, orderOption: orderOption),
                   targetEntity, targetCharacter, sender)
        {

        }

        public OrderChatMessage(Order order, string orderOption, int priority, string text, ISpatialEntity targetEntity, Character targetCharacter, Character sender)
            : base(sender?.Name, text, ChatMessageType.Order, sender, GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == sender))
        {
            Order = order;
            OrderOption = orderOption;
            OrderPriority = priority;
            TargetCharacter = targetCharacter;
            TargetEntity = targetEntity;
        }

        private void WriteOrder(IWriteMessage msg)
        {
            msg.Write((byte)Order.PrefabList.IndexOf(Order.Prefab));
            msg.Write(TargetCharacter == null ? (UInt16)0 : TargetCharacter.ID);
            msg.Write(TargetEntity is Entity ? (TargetEntity as Entity).ID : (UInt16)0);

            // The option of a Dismiss order is written differently so we know what order we target
            // now that the game supports multiple current orders simultaneously
            if (Order.Prefab.Identifier != "dismissed")
            {
                msg.Write((byte)Array.IndexOf(Order.Prefab.Options, OrderOption));
            }
            else
            {
                if (!string.IsNullOrEmpty(OrderOption))
                {
                    msg.Write(true);
                    string[] dismissedOrder = OrderOption.Split('.');
                    msg.Write((byte)dismissedOrder.Length);
                    if (dismissedOrder.Length > 0)
                    {
                        string dismissedOrderIdentifier = dismissedOrder[0];
                        var orderPrefab = Order.GetPrefab(dismissedOrderIdentifier);
                        msg.Write((byte)Order.PrefabList.IndexOf(orderPrefab));
                        if (dismissedOrder.Length > 1)
                        {
                            string dismissedOrderOption = dismissedOrder[1];
                            msg.Write((byte)Array.IndexOf(orderPrefab.Options, dismissedOrderOption));
                        }
                    }
                }
                else
                {
                    // If the order option is not specified for a Dismiss order,
                    // we dismiss all current orders for the character
                    msg.Write(false);
                }
            }

            msg.Write((byte)OrderPriority);
            msg.Write((byte)Order.TargetType);
            if (Order.TargetType == Order.OrderTargetType.Position && TargetEntity is OrderTarget orderTarget)
            {
                msg.Write(true);
                msg.Write(orderTarget.Position.X);
                msg.Write(orderTarget.Position.Y);
                msg.Write(orderTarget.Hull == null ? (UInt16)0 : orderTarget.Hull.ID);
            }
            else
            {
                msg.Write(false);
                if (Order.TargetType == Order.OrderTargetType.WallSection)
                {
                    msg.Write((byte)(WallSectionIndex ?? Order.WallSectionIndex ?? 0));
                }
            }
        }
    }
}
