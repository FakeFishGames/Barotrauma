using Barotrauma;
using Barotrauma.Extensions;

namespace HomeAndHarbor;

/// <summary>
/// Unlocks a mission that travels from the current location towards an unvisited location further on the campaign map (e.g. an expedition sent from a player outpost).
/// If the current location has a shortcut, the mission can also start from any other visited location with a shortcut, allowing it to reach further.
/// The mission starts from whichever of these locations has the shortest path to the destination.
/// The mission travels along the path towards the destination (see <see cref="Mission.PathToDestination"/>), and the actions defined in the mission are executed
/// when it reaches the destination, e.g. <see cref="DiscoverLocationAction"/> to reveal the destination.
/// The Success branch is executed if the mission was unlocked, the Failure branch if no suitable destination was found.
/// The actions in the Success branch use the unlocked mission as their parent mission, e.g. so a <see cref="ModifyResourceAction"/> can log the destination.
/// </summary>
internal class UnlockMissionToUnvisitedLocationAction : BinaryOptionAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the mission to unlock.")]
    public Identifier MissionIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the mission to unlock. If there are multiple missions with the tag, one is chosen randomly.")]
    public Identifier MissionTag { get; set; }

    [Serialize(2, IsPropertySaveable.Yes, description: "Minimum distance from the start of the mission to the destination (1 = one path between locations).")]
    public int MinDistance { get; set; }

    [Serialize(4, IsPropertySaveable.Yes, description: "Maximum distance from the start of the mission to the destination (1 = one path between locations).")]
    public int MaxDistance { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "If the current location has a shortcut, can the mission start from other visited locations with a shortcut?")]
    public bool UseShortcuts { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the \"mission unlocked\" notification pop up (for the whole crew) when the mission is unlocked?")]
    public bool CreateUnlockNotification { get; set; }

    public UnlockMissionToUnvisitedLocationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        if (MissionIdentifier.IsEmpty && MissionTag.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": {nameof(UnlockMissionToUnvisitedLocationAction)} has neither {nameof(MissionIdentifier)} nor {nameof(MissionTag)} configured.");
        }
    }

    protected override bool? DetermineSuccess()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign || campaign.Map?.CurrentLocation is not { } currentLocation)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(UnlockMissionToUnvisitedLocationAction)} can only be used in the campaign.");
            return false;
        }

        foreach ((Location startLocation, List<Location> path) in GetCandidatePaths(campaign.Map, currentLocation))
        {
            Mission? unlockedMission = MissionAction.UnlockMission(campaign, startLocation, MissionIdentifier, MissionTag,
                createMessageBox: CreateUnlockNotification,
                markLocationAsDiscovered: false,
                forceConnection: startLocation.GetConnection(path[1]),
                invokingContentPackage: ParentEvent.Prefab.ContentPackage,
                createNetworkEvent: true);
            if (unlockedMission == null) { continue; }

            unlockedMission.SetPathToDestination(path);
            //the mission is sent from the current location, even if it starts off from some other location through a shortcut
            unlockedMission.OriginLocation = currentLocation;
            //the clients discover the first location on the path when receiving the mission unlock, do the same here to keep the state in sync
            campaign.Map.Discover(path[1], checkTalents: false);
            Success?.Actions.ForEach(action => action.ParentMission = unlockedMission);
            DebugConsole.NewMessage($"Unlocked the mission {unlockedMission.Prefab.Identifier} from {currentLocation.DisplayName} to {path.Last().DisplayName} (path: {string.Join(" -> ", path.Select(l => l.DisplayName))}).");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the possible start locations and paths of the mission, the most preferable first:
    /// undiscovered destinations are preferred over discovered ones, and destinations further on the map over closer ones.
    /// </summary>
    private IEnumerable<(Location StartLocation, List<Location> Path)> GetCandidatePaths(Map map, Location currentLocation)
    {
        List<Location> startLocations = [currentLocation];
        if (UseShortcuts && LocationShortcutManager.IsShortcutAvailable(currentLocation))
        {
            startLocations.AddRange(map.Locations.Where(l => l != currentLocation && l.Visited && LocationShortcutManager.IsShortcutAvailable(l)));
        }

        //don't send multiple missions of this kind to the same destination
        HashSet<Location> existingDestinations = map.Locations
            .SelectMany(l => l.AvailableMissions)
            .Where(IsMatchingMission)
            .Select(m => m.Destination)
            .ToHashSet();

        //don't go past locked connections (e.g. biome gates the players haven't unlocked yet), or into the end areas
        bool CanTraverse(LocationConnection connection)
            => !connection.Locked && connection.Locations.None(l => l.Biome.IsEndBiome || map.EndLocations.Contains(l));

        HashSet<Location> destinations = startLocations
            .SelectMany(startLocation => Map.GetLocationsWithinDistance(startLocation, MaxDistance, CanTraverse))
            .ToHashSet();

        List<(Location StartLocation, List<Location> Path)> candidates = [];
        foreach (Location destination in destinations)
        {
            if (destination.Visited) { continue; }
            if (existingDestinations.Contains(destination)) { continue; }
            //must be further on the map than at least one of the start locations
            if (startLocations.None(startLocation => destination.MapPosition.X > startLocation.MapPosition.X)) { continue; }

            //use the shortest path from any of the start locations (the current location if there's a tie),
            //instead of potentially taking a long detour from some shortcut further away
            (Location StartLocation, List<Location> Path)? shortest = null;
            foreach (Location startLocation in startLocations)
            {
                List<Location> path = Map.GetPathToLocation(startLocation, destination, MaxDistance, CanTraverse);
                if (path.None()) { continue; }
                if (shortest == null || path.Count < shortest.Value.Path.Count)
                {
                    shortest = (startLocation, path);
                }
            }
            if (shortest is not { } candidate) { continue; }
            //path includes the start location, so the distance is the number of locations minus one
            //(if the destination is too close to any of the start locations, it's not a suitable destination for this mission)
            if (candidate.Path.Count - 1 < MinDistance) { continue; }
            candidates.Add(candidate);
        }

        return candidates
            .OrderBy(c => c.Path.Last().Discovered)
            .ThenByDescending(c => c.Path.Last().MapPosition.X);
    }

    private bool IsMatchingMission(Mission mission)
    {
        if (!MissionIdentifier.IsEmpty) { return mission.Prefab.Identifier == MissionIdentifier; }
        return mission.Prefab.Tags.Contains(MissionTag);
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(UnlockMissionToUnvisitedLocationAction)} -> ({(MissionIdentifier.IsEmpty ? MissionTag : MissionIdentifier).ColorizeObject()}, Success: {succeeded.ColorizeObject()})";
    }
}
