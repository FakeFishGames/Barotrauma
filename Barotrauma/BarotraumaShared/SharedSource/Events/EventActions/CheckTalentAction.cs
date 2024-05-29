#nullable enable

namespace Barotrauma
{
    /// <summary>
    /// Check whether a specific character has a specific talent.
    /// </summary>
    internal sealed class CheckTalentAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the talent to check for.")]
        public Identifier TalentIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to check.")]
        public Identifier TargetTag { get; set; }

        public CheckTalentAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            if (TargetTag.IsEmpty)
            {
                return false;
            }

            Character? matchingCharacter = null;

            foreach (Entity entity in ParentEvent.GetTargets(TargetTag))
            {
                if (entity is Character character)
                {
                    matchingCharacter = character;
                    break;
                }
            }

            return matchingCharacter is not null && matchingCharacter.HasTalent(TalentIdentifier);
        }

        public override string ToDebugString()
        {
            string subActionStr = "";
            if (succeeded.HasValue)
            {
                subActionStr = $"\n            Sub action: {(succeeded.Value ? Success : Failure)?.CurrentSubAction.ColorizeObject()}";
            }

            return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(CheckTalentAction)} -> (Talent: {TalentIdentifier.ColorizeObject()}" +
                   $" Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})" +
                   subActionStr;
        }
    }
}