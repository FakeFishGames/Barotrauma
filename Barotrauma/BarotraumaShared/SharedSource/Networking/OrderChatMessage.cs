using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Networking
{
    partial class OrderChatMessage : ChatMessage
    {
        public readonly Order Order;

        //who was this order given to
        public readonly Character TargetCharacter;

        //which entity is this order referring to (hull, reactor, railgun controller, etc)
        public ISpatialEntity TargetEntity => Order.TargetSpatialEntity;

        //additional instructions (power up, fire at will, etc)
        public Identifier OrderOption => Order.Option;

        public int OrderPriority => Order.ManualPriority;

        /// <summary>
        /// Used when the order targets a wall
        /// </summary>
        public int? WallSectionIndex => Order.WallSectionIndex;

        public bool IsNewOrder { get; }

        /// <summary>
        /// Same as calling <see cref="OrderChatMessage.OrderChatMessage(Order, string, int, string, ISpatialEntity, Character, Character)"/>,
        /// but the text parameter is set using <see cref="Order.GetChatMessage(string, string, bool, string)"/>
        /// </summary>
        public OrderChatMessage(Order order, Character targetCharacter, Character sender, bool isNewOrder = true)
            : this(order,
                   order?.GetChatMessage(targetCharacter?.Name, sender?.CurrentHull?.DisplayName?.Value, givingOrderToSelf: targetCharacter == sender, orderOption: order.Option, isNewOrder: isNewOrder),
                   targetCharacter, sender, isNewOrder)
        {
            
        }

        public OrderChatMessage(Order order, string text, Character targetCharacter, Character sender, bool isNewOrder = true)
            : base(sender?.Name, text, ChatMessageType.Order, sender, GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == sender))
        {
            Order = order;
            TargetCharacter = targetCharacter;
            IsNewOrder = isNewOrder;
        }

        public static void WriteOrder(IWriteMessage msg, Order order, Character targetCharacter, bool isNewOrder)
        {
            msg.Write(order.Prefab.Identifier);
            msg.Write(targetCharacter == null ? (UInt16)0 : targetCharacter.ID);
            msg.Write(order.TargetSpatialEntity is Entity ? (order.TargetEntity as Entity).ID : (UInt16)0);

            // The option of a Dismiss order is written differently so we know what order we target
            // now that the game supports multiple current orders simultaneously
            if (!order.IsDismissal)
            {
                msg.Write((byte)order.Options.IndexOf(order.Option));
            }
            else
            {
                if (order.Option != Identifier.Empty)
                {
                    msg.Write(true);
                    string[] dismissedOrder = order.Option.Value.Split('.');
                    msg.Write((byte)dismissedOrder.Length);
                    if (dismissedOrder.Length > 0)
                    {
                        Identifier dismissedOrderIdentifier = dismissedOrder[0].ToIdentifier();
                        var orderPrefab = OrderPrefab.Prefabs[dismissedOrderIdentifier];
                        msg.Write(dismissedOrderIdentifier);
                        if (dismissedOrder.Length > 1)
                        {
                            Identifier dismissedOrderOption = dismissedOrder[1].ToIdentifier();
                            msg.Write((byte)orderPrefab.Options.IndexOf(dismissedOrderOption));
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

            msg.Write((byte)order.ManualPriority);
            msg.Write((byte)order.TargetType);
            if (order.TargetType == Order.OrderTargetType.Position && order.TargetSpatialEntity is OrderTarget orderTarget)
            {
                msg.Write(true);
                msg.Write(orderTarget.Position.X);
                msg.Write(orderTarget.Position.Y);
                msg.Write(orderTarget.Hull == null ? (UInt16)0 : orderTarget.Hull.ID);
            }
            else
            {
                msg.Write(false);
                if (order.TargetType == Order.OrderTargetType.WallSection)
                {
                    msg.Write((byte)(order.WallSectionIndex ?? 0));
                }
            }

            msg.Write(isNewOrder);
        }

        private void WriteOrder(IWriteMessage msg)
        {
            WriteOrder(msg, Order, TargetCharacter, IsNewOrder);
        }

        public struct OrderMessageInfo
        {
            public Identifier OrderIdentifier { get; }
            public OrderPrefab OrderPrefab => OrderPrefab.Prefabs[OrderIdentifier];
            public Identifier OrderOption { get; }
            public int? OrderOptionIndex { get; }
            public Character TargetCharacter { get; }
            public Order.OrderTargetType TargetType { get; }
            public Entity TargetEntity { get; }
            public OrderTarget TargetPosition { get; }
            public int? WallSectionIndex { get; }
            public int Priority { get; }
            public bool IsNewOrder { get; }

            public OrderMessageInfo(Identifier orderIdentifier, Identifier orderOption, int? orderOptionIndex, Character targetCharacter,
                Order.OrderTargetType targetType, Entity targetEntity, OrderTarget targetPosition, int? wallSectionIndex, int orderPriority, bool isNewOrder)
            {
                OrderIdentifier = orderIdentifier;
                OrderOption = orderOption;
                OrderOptionIndex = orderOptionIndex;
                TargetCharacter = targetCharacter;
                TargetType = targetType;
                TargetEntity = targetEntity;
                TargetPosition = targetPosition;
                WallSectionIndex = wallSectionIndex;
                Priority = orderPriority;
                IsNewOrder = isNewOrder;
            }
        }

        public static OrderMessageInfo ReadOrder(IReadMessage msg)
        {
            Identifier orderIdentifier = msg.ReadIdentifier();
            ushort targetCharacterId = msg.ReadUInt16();
            Character targetCharacter = targetCharacterId != Entity.NullEntityID ? Entity.FindEntityByID(targetCharacterId) as Character : null;
            ushort targetEntityId = msg.ReadUInt16();
            Entity targetEntity = targetEntityId != Entity.NullEntityID ? Entity.FindEntityByID(targetEntityId) : null;

            int? optionIndex = null;
            Identifier orderOption = Identifier.Empty;
            // The option of a Dismiss order is written differently so we know what order we target
            // now that the game supports multiple current orders simultaneously
            if (orderIdentifier != Identifier.Empty)
            {
                var orderPrefab = OrderPrefab.Prefabs[orderIdentifier];
                if (!orderPrefab.IsDismissal)
                {
                    optionIndex = msg.ReadByte();
                }
                // Does the dismiss order have a specified target?
                else if (msg.ReadBoolean())
                {
                    int identifierCount = msg.ReadByte();
                    if (identifierCount > 0)
                    {
                        Identifier dismissedOrderIdentifier = msg.ReadIdentifier();
                        OrderPrefab dismissedOrderPrefab = null;
                        if (dismissedOrderIdentifier != Identifier.Empty)
                        {
                            dismissedOrderPrefab = OrderPrefab.Prefabs[dismissedOrderIdentifier];
                            orderOption = dismissedOrderPrefab.Identifier;
                        }
                        if (identifierCount > 1)
                        {
                            int dismissedOrderOptionIndex = msg.ReadByte();
                            if (dismissedOrderPrefab != null)
                            {
                                var options = dismissedOrderPrefab.Options;
                                if (options != null && dismissedOrderOptionIndex >= 0 && dismissedOrderOptionIndex < options.Length)
                                {
                                    orderOption = $"{orderOption.Value}.{options[dismissedOrderOptionIndex]}".ToIdentifier();
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                optionIndex = msg.ReadByte();
            }

            int orderPriority = msg.ReadByte();
            OrderTarget orderTargetPosition = null;
            Order.OrderTargetType orderTargetType = (Order.OrderTargetType)msg.ReadByte();
            int wallSectionIndex = 0;
            if (msg.ReadBoolean())
            {
                float x = msg.ReadSingle();
                float y = msg.ReadSingle();
                ushort hullId = msg.ReadUInt16();
                var hull = hullId != Entity.NullEntityID ? Entity.FindEntityByID(hullId) as Hull : null;
                orderTargetPosition = new OrderTarget(new Vector2(x, y), hull, creatingFromExistingData: true);
            }
            else if (orderTargetType == Order.OrderTargetType.WallSection)
            {
                wallSectionIndex = msg.ReadByte();
            }

            bool isNewOrder = msg.ReadBoolean();
            return new OrderMessageInfo(orderIdentifier, orderOption, optionIndex, targetCharacter,
                    orderTargetType, targetEntity, orderTargetPosition, wallSectionIndex, orderPriority, isNewOrder);
        }
    }
}
