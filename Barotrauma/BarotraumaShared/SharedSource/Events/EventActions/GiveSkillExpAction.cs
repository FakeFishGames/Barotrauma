using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class GiveSkillExpAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Skill { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public GiveSkillExpAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (TargetTag.IsEmpty)
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
                target.Info?.IncreaseSkillLevel(Skill, Amount);
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
