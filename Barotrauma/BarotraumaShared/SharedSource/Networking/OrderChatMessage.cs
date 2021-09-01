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

        public static void WriteOrder(IWriteMessage msg, Order order, Character targetCharacter, ISpatialEntity targetEntity, string orderOption, int orderPriority, int? wallSectionIndex)
        {
            msg.Write((byte)Order.PrefabList.IndexOf(order.Prefab));
            msg.Write(targetCharacter == null ? (UInt16)0 : targetCharacter.ID);
            msg.Write(targetEntity is Entity ? (targetEntity as Entity).ID : (UInt16)0);

            // The option of a Dismiss order is written differently so we know what order we target
            // now that the game supports multiple current orders simultaneously
            if (order.Prefab.Identifier != "dismissed")
            {
                msg.Write((byte)Array.IndexOf(order.Prefab.Options, orderOption));
            }
            else
            {
                if (!string.IsNullOrEmpty(orderOption))
                {
                    msg.Write(true);
                    string[] dismissedOrder = orderOption.Split('.');
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

            msg.Write((byte)orderPriority);
            msg.Write((byte)order.TargetType);
            if (order.TargetType == Order.OrderTargetType.Position && targetEntity is OrderTarget orderTarget)
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
                    msg.Write((byte)(wallSectionIndex ?? order.WallSectionIndex ?? 0));
                }
            }
        }

        private void WriteOrder(IWriteMessage msg)
        {
            WriteOrder(msg, Order, TargetCharacter, TargetEntity, OrderOption, OrderPriority, WallSectionIndex);
        }

        public struct OrderMessageInfo
        {
            public int OrderIndex { get; }
            public Order OrderPrefab { get; }
            public string OrderOption { get; }
            public int? OrderOptionIndex { get; }
            public Character TargetCharacter { get; }
            public Order.OrderTargetType TargetType { get; }
            public Entity TargetEntity { get; }
            public OrderTarget TargetPosition { get; }
            public int? WallSectionIndex { get; }
            public int Priority { get; }

            public OrderMessageInfo(int orderIndex, Order orderPrefab, string orderOption, int? orderOptionIndex, Character targetCharacter, Order.OrderTargetType targetType, Entity targetEntity, OrderTarget targetPosition, int? wallSectionIndex, int orderPriority)
            {
                OrderIndex = orderIndex;
                OrderPrefab = orderPrefab;
                OrderOption = orderOption;
                OrderOptionIndex = orderOptionIndex;
                TargetCharacter = targetCharacter;
                TargetType = targetType;
                TargetEntity = targetEntity;
                TargetPosition = targetPosition;
                WallSectionIndex = wallSectionIndex;
                Priority = orderPriority;
            }
        }

        public static OrderMessageInfo ReadOrder(IReadMessage msg)
        {
            int orderIndex = msg.ReadByte();
            ushort targetCharacterId = msg.ReadUInt16();
            Character targetCharacter = targetCharacterId != Entity.NullEntityID ? Entity.FindEntityByID(targetCharacterId) as Character : null;
            ushort targetEntityId = msg.ReadUInt16();
            Entity targetEntity = targetEntityId != Entity.NullEntityID ? Entity.FindEntityByID(targetEntityId) : null;

            Order orderPrefab = null;
            int? optionIndex = null;
            string orderOption = null;
            // The option of a Dismiss order is written differently so we know what order we target
            // now that the game supports multiple current orders simultaneously
            if (orderIndex >= 0 && orderIndex < Order.PrefabList.Count)
            {
                orderPrefab = Order.PrefabList[orderIndex];
                if (orderPrefab.Identifier != "dismissed")
                {
                    optionIndex = msg.ReadByte();
                }
                // Does the dismiss order have a specified target?
                else if (msg.ReadBoolean())
                {
                    int identifierCount = msg.ReadByte();
                    if (identifierCount > 0)
                    {
                        int dismissedOrderIndex = msg.ReadByte();
                        Order dismissedOrderPrefab = null;
                        if (dismissedOrderIndex >= 0 && dismissedOrderIndex < Order.PrefabList.Count)
                        {
                            dismissedOrderPrefab = Order.PrefabList[dismissedOrderIndex];
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
                                    orderOption += $".{options[dismissedOrderOptionIndex]}";
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

            return new OrderMessageInfo(orderIndex, orderPrefab, orderOption, optionIndex, targetCharacter, orderTargetType, targetEntity, orderTargetPosition, wallSectionIndex, orderPriority);
        }
    }
}
