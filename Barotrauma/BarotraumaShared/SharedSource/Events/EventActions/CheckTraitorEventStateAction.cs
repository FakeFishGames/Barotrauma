#nullable enable

namespace Barotrauma
{
    class CheckTraitorEventStateAction : BinaryOptionAction
    {
        [Serialize(TraitorEvent.State.Completed, IsPropertySaveable.Yes)]
        public TraitorEvent.State State { get; set; }

        private readonly TraitorEvent? traitorEvent;

        public CheckTraitorEventStateAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (parentEvent is TraitorEvent traitorEvent)
            {
                this.traitorEvent = traitorEvent;
            }
            else
            {
                DebugConsole.ThrowError($"Cannot use the action {nameof(CheckTraitorEventStateAction)} in the event \"{parentEvent.Prefab.Identifier}\" because it's not a traitor event.",
                    contentPackage: element.ContentPackage);
            }
        }

        protected override bool? DetermineSuccess()
        {
            return traitorEvent?.CurrentState == State;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckTraitorEventStateAction)} -> " +
                $"State: {State.ColorizeObject()}, Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}