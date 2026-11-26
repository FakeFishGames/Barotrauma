using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Discovers a location on the campaign map, and optionally marks it as visited, as if the player had entered it.
/// Marking a location with a shortcut as visited also makes the shortcut usable.
/// </summary>
internal class DiscoverLocationAction : EventAction
{
    public enum Target
    {
        /// <summary>
        /// The location the player is currently in.
        /// </summary>
        CurrentLocation,
        /// <summary>
        /// The location the mission originated from.
        /// </summary>
        StartLocation,
        /// <summary>
        /// The final destination of the mission.
        /// </summary>
        EndLocation
    }

    [Serialize(Target.EndLocation, IsPropertySaveable.Yes, description: "Which location to discover.")]
    public Target TargetLocation { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "Should the location be marked as visited? Makes the location's shortcut usable, if it has one.")]
    public bool MarkAsVisited { get; set; }

    private bool isFinished;

    public DiscoverLocationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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
        isFinished = true;

        if (GameMain.GameSession?.Campaign is not { } campaign || campaign.Map is not { } map)
        {
            DebugConsole.AddWarning($"Potential error in {nameof(DiscoverLocationAction)}: cannot be used when a campaign is not active.");
            return;
        }

        Location? targetLocation = TargetLocation switch
        {
            Target.CurrentLocation => campaign.CurrentLocation,
            Target.StartLocation => ParentMission?.OriginLocation ?? ParentMission?.Locations[0] ?? Level.Loaded?.StartLocation,
            Target.EndLocation => ParentMission?.Destination ?? Level.Loaded?.EndLocation,
            _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
        };
        if (targetLocation == null)
        {
            DebugConsole.AddWarning($"Potential error in {nameof(DiscoverLocationAction)}: could not determine the target location ({TargetLocation}).");
            return;
        }

        map.Discover(targetLocation, checkTalents: false);
        if (MarkAsVisited)
        {
            //don't increment the number of visits, the players haven't actually been there
            //(and the first actual visit may be relevant for e.g. events that trigger on the first visit)
            //visiting remotely also means the location doesn't count towards the difficulty of the furthest explored level
            map.Visit(targetLocation, resetTimeSinceVisited: true, incrementTimesVisited: false, visitedRemotely: true);
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(DiscoverLocationAction)} -> (Target: {TargetLocation}, Visit: {MarkAsVisited})";
    }
}
