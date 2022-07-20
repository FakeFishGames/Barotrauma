using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillCheckAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier RequiredSkill { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float RequiredLevel { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool ProbabilityBased { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public SkillCheckAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (TargetTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": SkillCheckAction without a target tag (the action needs to know whose skill to check).");
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