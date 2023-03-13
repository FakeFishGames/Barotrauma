namespace Barotrauma
{
    partial class TutorialSegmentAction : EventAction
    {
        public enum SegmentActionType { Trigger, Add, Complete, CompleteAndRemove, Remove };

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

        private bool isFinished;

        public TutorialSegmentAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (Identifier.IsEmpty)
            {
                Identifier = element.GetAttributeIdentifier("id", Identifier.Empty);
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