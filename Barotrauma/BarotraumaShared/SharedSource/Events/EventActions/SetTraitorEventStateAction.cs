#nullable enable

namespace Barotrauma
{
    /// <summary>
    /// Sets the state of the traitor event. Only valid in traitor events.
    /// </summary>
    class SetTraitorEventStateAction : EventAction
    {
        private readonly TraitorEvent? traitorEvent;

        public SetTraitorEventStateAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (parentEvent is TraitorEvent traitorEvent)
            {
                this.traitorEvent = traitorEvent;
            }
            else
            {
                DebugConsole.ThrowError($"Cannot use the action {nameof(SetTraitorEventStateAction)} in the event \"{parentEvent.Prefab.Identifier}\" because it's not a traitor event.",
                    contentPackage: element.ContentPackage);
            }
        }

        [Serialize(TraitorEvent.State.Completed, IsPropertySaveable.Yes, description: "The state to set the traitor event to (Incomplete, Completed or Failed).")]
        public TraitorEvent.State State { get; set; }

        private bool isFinished;

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
            if (isFinished || traitorEvent == null) { return; }
            traitorEvent.CurrentState = State;
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetTraitorEventStateAction)} -> (State: {State})";
        }
    }
}