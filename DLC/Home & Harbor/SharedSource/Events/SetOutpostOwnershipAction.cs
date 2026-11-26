using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Set whether an outpost is player-owned or not.
/// </summary>
class SetOutpostOwnershipAction : EventAction
{
    public enum Target
    {
        /// <summary>
        /// Default behavior: modifies the currently loaded location.
        /// </summary>
        CurrentLocation,
        /// <summary>
        /// Only valid for missions triggered by another mission. Modifies the start location of the triggering mission.
        /// </summary>
        MissionStartLocation,
        /// <summary>
        /// Only valid for missions triggered by another mission. Modifies the end location of the triggering mission.
        /// </summary>
        MissionEndLocation
    }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the outpost be player-owned or not?")]
    public bool IsPlayerOwned { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "If set to true, any missions or other factors that would normally prevent the location type from changing are ignored.")]
    public bool Force { get; set; }

    [Serialize(Target.CurrentLocation, IsPropertySaveable.Yes)]
    public Target TargetLocation { get; set; }

    public SetOutpostOwnershipAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
    }

    private bool isFinished = false;

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(SetOutpostOwnershipAction)} when a campaign is not active.");
            return;
        }
        Location targetLocation = TargetLocation switch
        {
            Target.CurrentLocation => campaign.CurrentLocation,
            Target.MissionStartLocation => ParentMission?.Locations[0] ?? campaign.CurrentLocation,
            Target.MissionEndLocation => ParentMission?.Locations[1] ?? campaign.CurrentLocation,
            _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
        };
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(targetLocation);
        outpostData.SetPlayerOwned(IsPlayerOwned, force: Force);

        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetOutpostOwnershipAction)} -> ({IsPlayerOwned})";
    }
}
