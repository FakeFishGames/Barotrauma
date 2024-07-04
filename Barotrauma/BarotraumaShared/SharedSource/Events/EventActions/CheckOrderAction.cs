using Barotrauma.Extensions;

namespace Barotrauma
{
    /// <summary>
    /// Check whether a specific character has been given a specific order.
    /// </summary>
    class CheckOrderAction : BinaryOptionAction
    {
        public enum OrderPriority
        {
            Top,
            Any
        }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to check.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the order the target character must have.")]
        public Identifier OrderIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The option that must be selected for the order. If the order has multiple options (such as turning on or turning off a reactor).")]
        public Identifier OrderOption { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity the order must be targeting. Only valid for orders that can target a specific entity (such as orders to operate a specific turret).")]
        public Identifier OrderTargetTag { get; set; }

        [Serialize(OrderPriority.Any, IsPropertySaveable.Yes, description: "Does the order need to have top priority, or is any priority fine?")]
        public OrderPriority Priority { get; set; }

        public CheckOrderAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            var targetCharacters = ParentEvent.GetTargets(TargetTag);
            if (targetCharacters.None())
            {
                DebugConsole.LogError($"CheckConditionalAction error: {GetEventName()} uses a CheckOrderAction but no valid target characters were found for tag \"{TargetTag}\"! This will cause the check to automatically fail.",
                    contentPackage: ParentEvent.Prefab.ContentPackage);
                return false;
            }
            foreach (var t in targetCharacters)
            {
                if (t is not Character c)
                {
                    continue;
                }
                if (Priority == OrderPriority.Top)
                {
                    if (c.GetCurrentOrderWithTopPriority() is Order topPrioOrder && IsMatch(topPrioOrder))
                    {
                        return true;
                    }
                }
                else if (Priority == OrderPriority.Any)
                {
                    foreach (var order in c.CurrentOrders)
                    {
                        if (IsMatch(order))
                        {
                            return true;
                        }
                    }
                }
                
                bool IsMatch(Order order)
                {
                    if (order?.Identifier == OrderIdentifier)
                    {
                        if (!OrderTargetTag.IsEmpty && (order.TargetEntity is not Item targetItem || !targetItem.HasTag(OrderTargetTag)))
                        {
                            return false;
                        }
                        if (OrderOption.IsEmpty || order?.Option == OrderOption)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            return false;
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }
    }
}