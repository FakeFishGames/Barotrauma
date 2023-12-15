using System.Linq;

namespace Barotrauma
{
    class GiveExpAction : EventAction
    {
        [Serialize(0, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public GiveExpAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (TargetTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": {nameof(GiveExpAction)} without a target tag (the action needs to know whose skill to check).",
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
                target.Info?.GiveExperience(Amount);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(GiveExpAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Amount: {Amount.ColorizeObject()})";
        }
    }
}
