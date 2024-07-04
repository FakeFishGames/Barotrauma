#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Check whether there's at least / at most some number of entities matching some specific criteria.
    /// </summary>
    class CountTargetsAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entities to check.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional second tag. Can be used if the target must have two different tags.")]
        public Identifier SecondRequiredTargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional tag of a hull the target must be inside.")]
        public Identifier HullTag { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Minimum number of matching entities for the check to succeed. If omitted or negative, there is no minimum amount.")]
        public int MinAmount { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Maximum number of matching entities for the check to succeed. If omitted or negative, there is no maximum amount.")]
        public int MaxAmount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of some other entities to compare the number of targets to. E.g. you could compare the number of entities tagged as \"discoveredhull\" to entities tagged as \"anyhull\". The minimum/maximum amount of entities there must be relative to the other entities is configured using MinPercentageRelativeToTarget and MaxPercentageRelativeToTarget.")]
        public Identifier CompareToTarget { get; set; }

        [Serialize(-1.0f, IsPropertySaveable.Yes, description: "Minimum amount of targets, as a percentage of the number of entities tagged with CompareToTarget. E.g. you could compare the number of entities tagged as \"discoveredhull\" to entities tagged as \"anyhull\" to require 50% of hulls to be discovered.")]
        public float MinPercentageRelativeToTarget { get; set; }

        [Serialize(-1.0f, IsPropertySaveable.Yes, description: "Maximum amount of targets, as a percentage of the number of entities tagged with CompareToTarget. E.g. you could compare the number of entities tagged as \"floodedhull\" to entities tagged as \"anyhull\" to require less than 50% of hulls to be flooded.")]
        public float MaxPercentageRelativeToTarget { get; set; }

        private readonly IReadOnlyList<PropertyConditional> conditionals;

        public CountTargetsAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            var conditionalList = new List<PropertyConditional>();
            foreach (ContentXElement subElement in element.GetChildElements("conditional"))
            {
                conditionalList.AddRange(PropertyConditional.FromXElement(subElement!));
            }
            conditionals = conditionalList;

            if (CompareToTarget.IsEmpty)
            {
                int amount = element.GetAttributeInt("amount", -1);
                if (amount > -1)
                {
                    MinAmount = MaxAmount = amount;
                }
                if (MinAmount > MaxAmount && MaxAmount > -1)
                {
                    DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {MinAmount} is larger than {MaxAmount} in {nameof(CountTargetsAction)}.",
                        contentPackage: element.ContentPackage);
                }
            }
            else
            {
                if (MinPercentageRelativeToTarget < 0.0f && MaxPercentageRelativeToTarget < 0.0f)
                {
                    DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". Comparing to another target, but neither {nameof(MinPercentageRelativeToTarget)} or {nameof(MaxPercentageRelativeToTarget)} is set.",
                        contentPackage: element.ContentPackage);
                }
            }
        }

        protected override bool? DetermineSuccess()
        {
            var potentialTargets = ParentEvent.GetTargets(TargetTag);

            if (!SecondRequiredTargetTag.IsEmpty)
            {
                potentialTargets = potentialTargets.Where(t => ParentEvent.GetTargets(SecondRequiredTargetTag).Contains(t));
            }
            if (!HullTag.IsEmpty)
            {
                var hulls =  ParentEvent.GetTargets(HullTag).OfType<Hull>();
                potentialTargets = potentialTargets.Where(t =>
                    (t is Item it && hulls.Contains(it.CurrentHull)) ||
                    (t is Character c && hulls.Contains(c.CurrentHull)));
            }

            if (conditionals.Any())
            {
                potentialTargets = potentialTargets.Where(t => conditionals.Any(c => c.Matches(t as ISerializableEntity)));
            }

            int targetCount = potentialTargets.Count();

            if (CompareToTarget.IsEmpty)
            {
                if (MinAmount > -1 && targetCount < MinAmount) { return false; }
                if (MaxAmount > -1 && targetCount > MaxAmount) { return false; }
            }
            else
            {
                int compareToTargetCount = ParentEvent.GetTargets(CompareToTarget).Count();
                float percentage = MathUtils.Percentage(targetCount, compareToTargetCount);
                if (MinPercentageRelativeToTarget > -1 && percentage < MinPercentageRelativeToTarget) { return false; }
                if (MaxPercentageRelativeToTarget > -1 && percentage > MaxPercentageRelativeToTarget) { return false; }
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CountTargetsAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}