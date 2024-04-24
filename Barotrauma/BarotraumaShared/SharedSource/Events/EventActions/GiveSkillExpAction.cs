using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Increases the skill level of a specific character.
    /// </summary>
    class GiveSkillExpAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the skill to increase.")]
        public Identifier Skill { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How much the skill should increase.")]
        public float Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) whose skill to increase.")]
        public Identifier TargetTag { get; set; }

        public GiveSkillExpAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": {nameof(GiveSkillExpAction)} without a target tag (the action needs to know whose skill to check).",
                    contentPackage: element.ContentPackage);
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
