using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CheckConditionalAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize(PropertyConditional.LogicalOperatorType.Or, IsPropertySaveable.Yes)]
        public PropertyConditional.LogicalOperatorType LogicalOperator { get; set; }

        private ImmutableArray<PropertyConditional> Conditionals { get; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ApplyTagToLinkedHulls { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the hull the target item is inside when the item is used.")]
        public Identifier ApplyTagToHull { get; set; }

        public CheckConditionalAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.LogError($"CheckConditionalAction error: {GetEventName()} uses a CheckConditionalAction with no target tag! This will cause the check to automatically succeed.",
                    contentPackage: parentEvent.Prefab.ContentPackage);
            }
            var conditionalElements = element.GetChildElements("Conditional");
            if (conditionalElements.None())
            {
                //backwards compatibility
                Conditionals = PropertyConditional.FromXElement(element, IsConditionalAttribute).ToImmutableArray();
            }
            else
            {
                var conditionalList = new List<PropertyConditional>();
                foreach (ContentXElement subElement in conditionalElements)
                {
                    conditionalList.AddRange(PropertyConditional.FromXElement(subElement));
                    break;
                }
                Conditionals = conditionalList.ToImmutableArray();
            }

            if (Conditionals.None())
            {
                DebugConsole.LogError($"CheckConditionalAction error: {GetEventName()} uses a CheckConditionalAction with no valid PropertyConditional! This will cause the check to automatically succeed.",
                    contentPackage: parentEvent.Prefab.ContentPackage);
            }

            static bool IsConditionalAttribute(XAttribute attribute)
            {
                var nameAsIdentifier = attribute.NameAsIdentifier();
                return 
                    nameAsIdentifier != nameof(TargetTag) &&
                    nameAsIdentifier != nameof(LogicalOperator) &&
                    nameAsIdentifier != nameof(ApplyTagToLinkedHulls) &&
                    nameAsIdentifier != nameof(ApplyTagToHull);
            }
        }

        private string GetEventName()
        {
            return ParentEvent?.Prefab?.Identifier is { IsEmpty: false } identifier ? $"the event \"{identifier}\"" : "an unknown event";
        }

        protected override bool? DetermineSuccess()
        {
            IEnumerable<ISerializableEntity> targets = null;
            if (!TargetTag.IsEmpty)
            {
                targets = ParentEvent.GetTargets(TargetTag).OfType<ISerializableEntity>();
            }

            if (targets.None())
            {
                DebugConsole.LogError($"{nameof(CheckConditionalAction)} error: {GetEventName()} uses a {nameof(CheckConditionalAction)} but no valid target was found for tag \"{TargetTag}\"! This will cause the check to automatically succeed.",
                    contentPackage: ParentEvent.Prefab.ContentPackage);
            }

            if (targets.None() || Conditionals.None())
            {
                foreach (var target in targets)
                {
                    ApplyTagsToHulls(target as Entity, ApplyTagToHull, ApplyTagToLinkedHulls);
                }
                return true;
            }
            else
            {
                bool success = false;
                foreach (var target in targets)
                {
                    if (ConditionalsMatch(target))
                    {
                        success = true;
                        ApplyTagsToHulls(target as Entity, ApplyTagToHull, ApplyTagToLinkedHulls);
                    }
                }
                return success;
            }
        }

        private bool ConditionalsMatch(ISerializableEntity target)
        {
            if (LogicalOperator == PropertyConditional.LogicalOperatorType.And)
            {
                return Conditionals.All(c => ConditionalMatches(target, c));
            }
            else
            {
                return Conditionals.Any(c => ConditionalMatches(target, c));
            }
        }

        private static bool ConditionalMatches(ISerializableEntity target, PropertyConditional conditional)
        {
            if (target is Item item)
            {
                if (!conditional.TargetItemComponent.IsNullOrEmpty() &&
                    item.Components.None(ic => ic.Name == conditional.TargetItemComponent))
                {
                    return false;
                }
                return item.ConditionalMatches(conditional);
            }
            return conditional.Matches(target);            
        }
    }
}