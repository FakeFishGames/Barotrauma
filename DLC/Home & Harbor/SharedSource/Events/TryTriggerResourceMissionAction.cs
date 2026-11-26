using Barotrauma;
using Barotrauma.Extensions;

namespace HomeAndHarbor;

/// <summary>
/// Triggers a mission between the current location and one of the locations nearby, e.g. a transport sub bringing food to the current outpost.
/// The element is configured the same way as a MissionTrigger in the resource definitions, 
/// allowing to check resource-based conditions and modify resources when the mission is unlocked.
/// The Success branch is executed if the mission was unlocked, the Failure branch if no suitable location was found.
/// </summary>
internal class TryTriggerResourceMissionAction : BinaryOptionAction
{
    public enum MissionDirection
    {
        /// <summary>
        /// The mission is sent from a nearby location to the current location.
        /// </summary>
        ToCurrentLocation,
        /// <summary>
        /// The mission is sent from the current location to a nearby location.
        /// Note that player outposts have no faction, so AllowedFactions and the [faction] tag can't be used when sending from one.
        /// </summary>
        FromCurrentLocation
    }

    [Serialize(MissionDirection.ToCurrentLocation, IsPropertySaveable.Yes, 
        description: 
            $"{nameof(MissionDirection.ToCurrentLocation)} = a nearby location sends a mission to the current location."+
            $" {nameof(MissionDirection.FromCurrentLocation)} = the current location sends a mission to a nearby location.")]
    public MissionDirection Direction { get; set; }

    [Serialize(true, IsPropertySaveable.Yes, description: "Should the \"mission unlocked\" notification pop up (for the whole crew) when the mission is unlocked?")]
    public bool CreateUnlockNotification { get; set; }

    private readonly ContentXElement element;

    public TryTriggerResourceMissionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        this.element = element;
    }

    protected override bool? DetermineSuccess()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign || campaign.Map?.CurrentLocation is not { } currentLocation)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(TryTriggerResourceMissionAction)} can only be used in the campaign.");
            return false;
        }
        if (PersistentOutpostData.GetPersistentOutpostData(currentLocation).ResourceManager is not { } currentResourceManager)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(TryTriggerResourceMissionAction)} failed, the current location has no resources.");
            return false;
        }

        //we're (presumably) in the outpost, and some of the resources may be items in the outpost:
        //make sure the resources match what's actually in the outpost before checking and modifying them
        currentResourceManager.SyncResourcesWithLoadedOutpostItems();
        bool success = TryTriggerMission(campaign, currentLocation, currentResourceManager);
        if (success)
        {
            //spawn/remove items to match the modified resources, otherwise the changes would get overridden by the items when leaving the outpost
            currentResourceManager.SyncLoadedOutpostItemsWithResources();
        }
        return success;
    }

    private bool TryTriggerMission(CampaignMode campaign, Location currentLocation, ResourceManager currentResourceManager)
    {
        string debugName = $"{nameof(TryTriggerResourceMissionAction)} in event {ParentEvent.Prefab.Identifier}";
        int currentWorldStep = campaign.Map.WorldStepCount;

        //the trigger created for the current location is only used for reading the configuration when sending missions to the current location
        MissionTrigger currentLocationTrigger = new MissionTrigger(element, currentResourceManager, debugName);
        List<Location> nearbyLocations = GetNearbyLocations(currentLocation, currentLocationTrigger.MaxDistanceFromDestinationOutpost);

        switch (Direction)
        {
            case MissionDirection.ToCurrentLocation:
                foreach (Location origin in nearbyLocations)
                {
                    if (PersistentOutpostData.GetPersistentOutpostData(origin).ResourceManager is not { } originResourceManager) { continue; }
                    //the generations in the trigger are bound to the resource manager of the origin, so we need a separate trigger for each candidate
                    MissionTrigger trigger = new MissionTrigger(element, originResourceManager, debugName);
                    if (Resource.TryTriggerMission(trigger, originResourceManager, triggeringResource: null, currentWorldStep,
                        destinationCandidates: currentLocation.ToEnumerable(), createNetworkEvent: true, createUnlockNotification: CreateUnlockNotification) != null)
                    {
                        return true;
                    }
                }
                return false;
            case MissionDirection.FromCurrentLocation:
                return Resource.TryTriggerMission(currentLocationTrigger, currentResourceManager, triggeringResource: null, currentWorldStep,
                    destinationCandidates: nearbyLocations, createNetworkEvent: true, createUnlockNotification: CreateUnlockNotification) != null;
            default:
                throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Locations within the max distance, the closest ones first (and in random order within the same distance).
    /// </summary>
    private static List<Location> GetNearbyLocations(Location currentLocation, int maxDistance)
    {
        List<Location> candidates = Map.GetLocationsWithinDistance(currentLocation, maxDistance).Where(l => l != currentLocation).ToList();
        candidates.Shuffle(Rand.RandSync.Unsynced);
        return candidates.OrderBy(l => Map.GetPathToLocation(currentLocation, l, maxDistance).Count).ToList();
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(TryTriggerResourceMissionAction)} -> (Direction: {Direction.ColorizeObject()}, Success: {succeeded.ColorizeObject()})";
    }
}
