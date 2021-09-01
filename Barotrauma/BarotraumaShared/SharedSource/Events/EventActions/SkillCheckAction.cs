using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SkillCheckAction : BinaryOptionAction
    {
        [Serialize("", true)]
        public string RequiredSkill { get; set; }

        [Serialize(0.0f, true)]
        public float RequiredLevel { get; set; }

        [Serialize(true, true)]
        public bool ProbabilityBased { get; set; }

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public SkillCheckAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) 
        { 
            if (string.IsNullOrEmpty(TargetTag))
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": SkillCheckAction without a target tag (the action needs to know whose skill to check).");
            }
        }

        protected override bool? DetermineSuccess()
        {
            var potentialTargets = ParentEvent.GetTargets(TargetTag).Where(e => e is Character).Select(e => e as Character);

            if (ProbabilityBased)
            {
                return potentialTargets.Any(chr => chr.GetSkillLevel(RequiredSkill?.ToLowerInvariant()) / RequiredLevel > Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced));
            }
            else
            {
                return potentialTargets.Any(chr => chr.GetSkillLevel(RequiredSkill?.ToLowerInvariant()) >= RequiredLevel);
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