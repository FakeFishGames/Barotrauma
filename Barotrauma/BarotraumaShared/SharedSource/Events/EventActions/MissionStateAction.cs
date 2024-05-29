namespace Barotrauma
{

    /// <summary>
    /// Changes the state of a specific active mission. The way the states are used depends on the type of mission.
    /// </summary>
    class MissionStateAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the mission whose state to change.")]
        public Identifier MissionIdentifier { get; set; }

        public enum OperationType
        {
            Set,
            Add
        }

        [Serialize(OperationType.Set, IsPropertySaveable.Yes, description: "Should the value be added to the state of the mission, or should the state be set to the specified value.")]
        public OperationType Operation { get; set; }

        [Serialize(0, IsPropertySaveable.Yes, description: "The state to set the mission to, or how much to add to the state of the mission.")]
        public int State { get; set; }

        private bool isFinished;

        public MissionStateAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            State = element.GetAttributeInt("value", State);
            if (MissionIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": MissionIdentifier has not been configured.",
                    contentPackage: element.ContentPackage);
            }
        }

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

            foreach (Mission mission in GameMain.GameSession.Missions)
            {
                if (mission.Prefab.Identifier != MissionIdentifier) { continue; }
                switch (Operation)
                {
                    case OperationType.Set:
                        mission.State = State;
                        break;
                    case OperationType.Add:
                        mission.State += 1;
                        break;
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionStateAction)} -> ({(Operation == OperationType.Set ? State : '+' + State)})";
        }
    }
}