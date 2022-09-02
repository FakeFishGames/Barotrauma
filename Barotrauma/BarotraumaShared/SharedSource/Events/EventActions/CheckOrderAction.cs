namespace Barotrauma
{
    class CheckOrderAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderOption { get; set; }

        public CheckOrderAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            ISerializableEntity target = null;
            if (!TargetTag.IsEmpty)
            {
                foreach (var t in ParentEvent.GetTargets(TargetTag))
                {
                    if (t is ISerializableEntity e)
                    {
                        target = e;
                        break;
                    }
                }
            }
            if (target == null)
            {
                DebugConsole.ShowError($"CheckConditionalAction error: {GetEventName()} uses a CheckOrderAction but no valid target was found for tag \"{TargetTag}\"! This will cause the check to automatically succeed.");
                return true;
            }
            if (target is Character character)
            {
                var currentOrderInfo = character.GetCurrentOrderWithTopPriority();
                if (currentOrderInfo?.Identifier == OrderIdentifier)
                {
                    if (OrderOption.IsEmpty)
                    {
                        return true;
                    }
                    else
                    {
                        return currentOrderInfo?.Option == OrderOption;
                    }
                }
                return false;
            }
            return true;
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }
    }
}