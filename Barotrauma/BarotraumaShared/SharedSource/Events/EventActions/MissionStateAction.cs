#nullable enable
namespace Barotrauma;

/// <summary>Changes the state of missions. The way the states are used depends on the type of mission.</summary>
internal sealed class MissionStateAction : EventAction
{
    /// <summary>The operation to perform on missions' states.</summary>
    public enum OperationType
    {
        /// <summary>Sets the missions' states to <see cref="State"/>.</summary>
        Set,
        /// <summary>Adds <see cref="State"/> to the missions' states.</summary>
        Add
    }

    [Serialize("", IsPropertySaveable.Yes, "Identifiers of the missions whose states to change. Leave blank to only set the state of the mission that triggered the parent event.")]
    public Identifier MissionIdentifier { get; set; }

    [Serialize(OperationType.Set, IsPropertySaveable.Yes, "The operation to perform on missions' states.")]
    public OperationType Operation { get; set; }

    [Serialize(0, IsPropertySaveable.Yes, "The value to apply to missions' states.")]
    public int State { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, "If set to true, missions are forced to fail without a chance of retrying them.")]
    public bool ForceFailure { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, "If set to true, missions are forced to retry even if they were completed.")]
    public bool ForceRetry { get; set; }

    private bool setForceFailure = false;
    private bool setForceRetry = false;
    private bool setState = false;

    public MissionStateAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        setForceFailure = element.GetAttribute("forcefailure") != null;
        setForceRetry = element.GetAttribute("forceretry") != null;
        //"value" is an accepted alias for "state"
        setState = element.GetAttribute("value") != null || element.GetAttribute("state") != null;
        State = element.GetAttributeInt("value", State);
        if (Operation == OperationType.Add && State == 0 && !ForceFailure)
        {
            DebugConsole.AddWarning($"Potential error in event \"{parentEvent.Prefab.Identifier}\": {nameof(MissionStateAction)} is set to only add 0 to the mission state, which will do nothing.",
                contentPackage: element.ContentPackage);
        }
    }

    private bool isFinished;
    public override bool IsFinished(ref string goTo) => isFinished;
    public override void Reset(bool shouldEndConversations = true) => isFinished = false;

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (!MissionIdentifier.IsEmpty)
        {
            foreach (Mission mission in GameMain.GameSession.Missions)
            {
                if (mission.Prefab.Identifier != MissionIdentifier) { continue; }
                SetMissionState(mission);
            }
        }
        else if (ParentEvent.TriggeringMission != null)
        {
            SetMissionState(ParentEvent.TriggeringMission);
        }

        isFinished = true;
    }

    private void SetMissionState(Mission mission)
    {
        if (setForceFailure) { mission.ForceFailure = ForceFailure; }
        if (setForceRetry) { mission.ForceRetry = ForceRetry; }
        if (setState)
        {
            switch (Operation)
            {
                case OperationType.Set:
                    mission.State = State;
                    break;
                case OperationType.Add:
                    mission.State += State;
                    break;
            }
        }
    }

    public override string ToDebugString() => $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionStateAction)} -> ({(Operation == OperationType.Set ? State : '+' + State)})";
}