namespace Barotrauma
{

    /// <summary>
    /// Clears the specific tag from the event (i.e. untagging all the entities that have been previously given the tag).
    /// </summary>
    class ClearTagAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "The tag to clear.")]
        public Identifier Tag { get; set; }

        private bool isFinished;

        public ClearTagAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goToLabel) => isFinished;

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (!Tag.IsEmpty)
            {
                ParentEvent.RemoveTag(Tag);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(ClearTagAction)} -> (Tag: {Tag.ColorizeObject()})";
        }
    }
}