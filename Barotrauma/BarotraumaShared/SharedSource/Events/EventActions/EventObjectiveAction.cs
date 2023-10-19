namespace Barotrauma
{
    partial class EventObjectiveAction : EventAction
    {
        public enum SegmentActionType { Trigger, Add, Complete, CompleteAndRemove, Remove, Fail, FailAndRemove };

        [Serialize(SegmentActionType.Trigger, IsPropertySaveable.Yes)]
        public SegmentActionType Type { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ObjectiveTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool CanBeCompleted { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ParentObjectiveId { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AutoPlayVideo { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TextTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string VideoFile { get; set; }

        [Serialize(450, IsPropertySaveable.Yes)]
        public int Width { get; set; }

        [Serialize(80, IsPropertySaveable.Yes)]
        public int Height { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        private bool isFinished;

        public EventObjectiveAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (Identifier.IsEmpty)
            {
                Identifier = element.GetAttributeIdentifier("id", Identifier.Empty);
            }
            if (Type != SegmentActionType.Trigger && !TextTag.IsEmpty)
            {
                DebugConsole.ThrowError(
                    $"Error in {nameof(EventObjectiveAction)} in the event \"{parentEvent.Prefab.Identifier}\""+
                    $" - {nameof(TextTag)} will do nothing unless the action triggers a message box or a video.");
            }
            if (element.GetChildElement("Replace") != null)
            {
                DebugConsole.ThrowError(
                    $"Error in {nameof(EventObjectiveAction)} in the event \"{parentEvent.Prefab.Identifier}\"" +
                    $" - unrecognized child element \"Replace\".");
            }
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            UpdateProjSpecific();
            isFinished = true;
        }

        partial void UpdateProjSpecific();

        public override bool IsFinished(ref string goToLabel) => isFinished;

        public override void Reset() => isFinished = false;
    }
}