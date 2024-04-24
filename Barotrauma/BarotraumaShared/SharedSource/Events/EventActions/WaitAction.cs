namespace Barotrauma
{
    /// <summary>
    /// Waits for a specific amount of time before continuing the execution of the event.
    /// </summary>
    class WaitAction : EventAction
    {
        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How long to wait (in seconds).")]
        public float Time { get; set; }

        private float timeRemaining;

        public WaitAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            timeRemaining = Time;
        }

        public override bool IsFinished(ref string goTo)
        {
            return timeRemaining <= 0;
        }
        public override void Reset()
        {
            timeRemaining = Time;
        }

        public override void Update(float deltaTime)
        {
            timeRemaining -= deltaTime;
            if (timeRemaining < 0.0f) { timeRemaining = 0.0f; }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(timeRemaining <= 0)} {nameof(WaitAction)} -> (Remaining: {timeRemaining.ColorizeObject()}, Time: {Time.ColorizeObject()})";
        }
    }
}