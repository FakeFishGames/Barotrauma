using Barotrauma.Extensions;

namespace Barotrauma
{
    class CheckOrderAction : BinaryOptionAction
    {
        public enum OrderPriority
        {
            Top,
            Any
        }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderOption { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderTargetTag { get; set; }

        [Serialize(OrderPriority.Top, IsPropertySaveable.Yes)]
        public OrderPriority Priority { get; set; }

        public CheckOrderAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            var targetCharacters = ParentEvent.GetTargets(TargetTag);
            if (targetCharacters.None())
            {
                DebugConsole.LogError($"CheckConditionalAction error: {GetEventName()} uses a CheckOrderAction but no valid target characters were found for tag \"{TargetTag}\"! This will cause the check to automatically fail.");
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