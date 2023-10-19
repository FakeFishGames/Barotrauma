#nullable enable

namespace Barotrauma
{
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
                DebugConsole.ThrowError($"Cannot use the action {nameof(SetTraitorEventStateAction)} in the event \"{parentEvent.Prefab.Identifier}\" because it's not a traitor event.");
            }
        }

        [Serialize(TraitorEvent.State.Completed, IsPropertySaveable.Yes)]
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