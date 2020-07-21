using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class GiveSkillExpAction : EventAction
    {
        [Serialize("", true)]
        public string Skill { get; set; }

        [Serialize(0.0f, true)]
        public float Amount { get; set; }

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public GiveSkillExpAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            if (string.IsNullOrEmpty(TargetTag))
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": GiveSkillExpAction without a target tag (the action needs to know whose skill to check).");
            }
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            var targets = ParentEvent.GetTargets(TargetTag).Where(e => e is Character).Select(e => e as Character);
            foreach (var target in targets)
            {
                target.Info?.IncreaseSkillLevel(Skill, Amount, target.WorldPosition + Vector2.UnitY * 150.0f);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(GiveSkillExpAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Skill: {Skill.ColorizeObject()}, Amount: {Amount.ColorizeObject()})";
        }
    }
}
