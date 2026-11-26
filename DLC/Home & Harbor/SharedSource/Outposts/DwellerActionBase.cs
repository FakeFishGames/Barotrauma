using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// Base class for event actions that share functionality related to outpost dwellers.
/// </summary>
internal abstract class DwellerActionBase(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to target.")]
    public Identifier TargetTag { get; set; }

    private readonly PeriodicValidationTimer validOutpostTimer = new();
    private bool inPlayerOwnedOutpost = false;
    private bool isFinished = false;

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        validOutpostTimer.Reset();
        inPlayerOwnedOutpost = false;
        isFinished = false;
    }

    public sealed override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (!inPlayerOwnedOutpost)
        {
            if (!validOutpostTimer.ShouldCheckNow(deltaTime))
            {
                return;
            }

            if (Level.Loaded?.StartLocation is not Location startLocation ||
                Level.Loaded.Type != LevelData.LevelType.Outpost ||
                startLocation.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is not { IsPlayerOwned: true })
            {
                // Not in a player-owned outpost, try again later
                return;
            }

            // Proceed with the event
            inPlayerOwnedOutpost = true;
        }

        // Find target characters
        var targetCharacters = FindTargetCharacters();
        if (!targetCharacters.Any())
        {
            return; // Don't finish, keep trying
        }

        // Validation passed earlier, so assumably this is an outpost, but let's still check it's currently player owned
        if (Level.Loaded?.StartLocation?.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is { } currentOutpostData)
        {
            // Execute the specific action on each target character
            foreach (var character in targetCharacters)
            {
                ExecuteOnCharacter(character, currentOutpostData);
            }
        }

        isFinished = true;
    }

    /// <summary>
    /// Execute the specific action on a target character.
    /// </summary>
    /// <param name="character">The character to process</param>
    /// <param name="outpostData">Current outpost data</param>
    /// <returns>True if the action was successful, false otherwise</returns>
    protected abstract bool ExecuteOnCharacter(Character character, PersistentOutpostData outpostData);

    private IEnumerable<Character> FindTargetCharacters()
    {
        if (TargetTag.IsEmpty)
        {
            DebugConsole.ThrowError($"{nameof(TargetTag)} is empty in {GetEventDebugName()}. This event always needs a target.");
            return Enumerable.Empty<Character>();
        }

        var tagTargets = ParentEvent.GetTargets(TargetTag);
        if (!tagTargets.Any())
        {
            DebugConsole.AddWarning($"Couldn't find any targets with the given {nameof(TargetTag)} for {GetEventDebugName()}.");
            return Enumerable.Empty<Character>();
        }
        
        // Filter to only valid NPCs
        var validTargets = tagTargets.OfType<Character>()
            .Where(c =>
                c is { Info: not null, IsPlayer: false, IsDead: false });
        if (!validTargets.Any())
        {
            DebugConsole.AddWarning($"None of the targets found with the given {nameof(TargetTag)} were valid targets (not player, not dead, not missing data) for {GetEventDebugName()}.");
            return Enumerable.Empty<Character>();
        }

        return validTargets;
    }
    
    public override string ToDebugString()
    {
        string target = $"Tag: {TargetTag}";
        return $"{ToolBox.GetDebugSymbol(isFinished)} {GetType().Name} -> ({target})";
    }
}