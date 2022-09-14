using System.Xml.Linq;

namespace Barotrauma
{
    class CheckConditionalAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        private PropertyConditional Conditional { get; }

        public CheckConditionalAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.ShowError($"CheckConditionalAction error: {GetEventName()} uses a CheckConditionalAction with no target tag! This will cause the check to automatically succeed.");
            }
            foreach (var attribute in element.Attributes())
            {
                if (PropertyConditional.IsValid(attribute) && !IsTargetTagAttribute(attribute))
                {
                    Conditional = new PropertyConditional(attribute);
                    break;
                }
            }
            if (Conditional == null)
            {
                DebugConsole.ShowError($"CheckConditionalAction error: {GetEventName()} uses a CheckConditionalAction with no valid PropertyConditional! This will cause the check to automatically succeed.");
            }

            static bool IsTargetTagAttribute(XAttribute attribute) => attribute.NameAsIdentifier() == "targettag";
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }

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
                DebugConsole.ShowError($"CheckConditionalAction error: {GetEventName()} uses a CheckConditionalAction but no valid target was found for tag \"{TargetTag}\"! This will cause the check to automatically succeed.");
            }
            if (target == null || Conditional == null)
            {
                return true;
            }
            if (target is Item item)
            {
                return item.ConditionalMatches(Conditional);
            }
            return Conditional.Matches(target);
        }
    }
}