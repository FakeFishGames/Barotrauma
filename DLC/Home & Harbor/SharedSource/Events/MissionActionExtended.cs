#nullable enable

using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// An extended version of MissionAction which can be used to check for DLC-only features and unlock shortcuts.
/// </summary>
class MissionActionExtended : EventAction
{
    [Serialize(false, IsPropertySaveable.Yes, description: "If set, the mission can only be unlocked in player-owned outposts.")]
    public bool RequirePlayerOwned { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "If set, the mission can only be unlocked in locations with a shortcut.")]
    public bool RequireShortcut { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "If set, a shortcut will be created in the selected location.")]
    public bool CreateShortcut { get; set; }

    private readonly MissionAction missionAction;

    public MissionActionExtended(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
        missionAction = new MissionAction(parentEvent, element);
        missionAction.LocationPredicate += IsLocationValid;
        missionAction.OnMissionUnlocked += OnMissionUnlocked;
    }

    public override void Update(float deltaTime)
    {
        missionAction.Update(deltaTime);
    }

    private bool IsLocationValid(Location location)
    {
        PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);        
        if (RequirePlayerOwned && !outpostData.IsPlayerOwned) { return false; }
        if (RequireShortcut && !outpostData.HasShortcut) { return false; }
        return true;
    }

    private void OnMissionUnlocked(Location location, Mission mission)
    {
        if (CreateShortcut)
        {
            PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
            outpostData.SetHasShortCut(hasShortcut: true, createNetworkEvent: true);
        }
    }

    public override bool IsFinished(ref string goToLabel)
    {
        return missionAction.IsFinished(ref goToLabel);
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        missionAction.Reset();
    }

    public override string? ToString()
    {
        return missionAction.ToString();
    }
}
