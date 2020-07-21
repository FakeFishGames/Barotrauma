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
            return potentialTargets.Any(chr => chr.GetSkillLevel(RequiredSkill?.ToLowerInvariant()) >= RequiredLevel);
        }

        public override string ToDebugString()
        {
            string subActionStr = "";
            if (succeeded.HasValue)
            {
                subActionStr = $"\n            Sub action: {(succeeded.Value ? Success : Failure)?.CurrentSubAction.ColorizeObject()}";
            }
            return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(SkillCheckAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Required skill: {RequiredSkill.ColorizeObject()}, Required level: {RequiredLevel.ColorizeObject()}, " +
                   $"Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})" +
                   subActionStr;
        }
    }
}