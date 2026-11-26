using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// Waits until the current location is a player-owned outpost before allowing the event to continue.
/// Place this at the start of events that should only run in player-owned outposts.
/// </summary>
internal class WaitForPlayerOwnedOutpostAction : EventAction
{
    [Serialize(5.0f, IsPropertySaveable.Yes, description: "Duration of the period for re-checking outpost validity if the check fails.")]
    public float RecheckPeriod { get; set; }

    private readonly PeriodicValidationTimer validationTimer;
    private bool isFinished = false;

    public WaitForPlayerOwnedOutpostAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        validationTimer = new PeriodicValidationTimer(RecheckPeriod);
    }

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        validationTimer.Reset();
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (!validationTimer.ShouldCheckNow(deltaTime))
        {
            return;
        }

        // Check if we're in a player-owned outpost
        if (Level.Loaded?.StartLocation is not Location startLocation ||
            Level.Loaded.Type != LevelData.LevelType.Outpost ||
            startLocation.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is not { IsPlayerOwned: true })
        {
            // Not yet, try again later
            return;
        }

        // In a player-owned outpost now, allow event to proceed
        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(WaitForPlayerOwnedOutpostAction)} -> (Finished: {isFinished.ColorizeObject()})";
    }
}
