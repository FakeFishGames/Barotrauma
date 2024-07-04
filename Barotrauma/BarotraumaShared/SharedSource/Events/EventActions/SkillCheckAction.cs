using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Performs a skill check and executes either the Success or Failure child actions depending on whether the check succeeds.
    /// </summary>
    class SkillCheckAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "The identifier of the skill to check.")]
        public Identifier RequiredSkill { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "The required skill level for the check to succeed.")]
        public float RequiredLevel { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the skill check be probability-based (i.e. if you have half the required skill level, the chance of success is 50%), or should the check always fail when under the required level and always succeed when above? ")]
        public bool ProbabilityBased { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) whose skill to check. If there are multiple targets, the action succeeds if any of their skill checks succeeds.")]
        public Identifier TargetTag { get; set; }

        public SkillCheckAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (TargetTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": SkillCheckAction without a target tag (the action needs to know whose skill to check).",
                    contentPackage: element.ContentPackage);
            }
        }

        protected override bool? DetermineSuccess()
        {
            var potentialTargets = ParentEvent.GetTargets(TargetTag).Where(e => e is Character).Select(e => e as Character);

            if (ProbabilityBased)
            {
                return potentialTargets.Any(chr => chr.GetSkillLevel(RequiredSkill) / RequiredLevel > Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced));
            }
            else
            {
                return potentialTargets.Any(chr => chr.GetSkillLevel(RequiredSkill) >= RequiredLevel);
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(SkillCheckAction)} -> (Target: {TargetTag.ColorizeObject()}, " +
                   $"Skill: {RequiredSkill.ColorizeObject()}, Level: {RequiredLevel.ColorizeObject()}, " +
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}