using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// Starts a fire at the position of a specific target.
    /// </summary>
    class FireAction : EventAction
    {
        [Serialize(10.0f, IsPropertySaveable.Yes, description: "Size of the fire (width in pixels).")]
        public float Size { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity to start the fire at.")]
        public Identifier TargetTag { get; set; }

        public FireAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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
            var targets = ParentEvent.GetTargets(TargetTag);
            foreach (var target in targets)
            {
                Vector2 pos = target.WorldPosition;

                var newFire = new FireSource(pos);
                newFire.Size = new Vector2(Size, Size);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(FireAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Size: {Size.ColorizeObject()})";
        }
    }
}