#nullable enable
namespace Barotrauma;

/// <summary>
/// Check whether the difficulty of the current level is within some specific range.
/// </summary>
class CheckDifficultyAction : BinaryOptionAction
{
    [Serialize(0.0f, IsPropertySaveable.Yes, description: "Minimum difficulty of the current level for the check to succeed.")]
    public float MinDifficulty { get; set; }

    [Serialize(100.0f, IsPropertySaveable.Yes, description: "Maximum difficulty of the current level for the check to succeed.")]
    public float MaxDifficulty { get; set; }

    public CheckDifficultyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)         
    {
        if (MaxDifficulty <= MinDifficulty)
        {
            DebugConsole.LogError($"Potential error in event {GetEventDebugName()}: maximum difficulty ({MaxDifficulty}) is not larger than minimum difficulty ({MinDifficulty}) in {nameof(CheckDifficultyAction)}.",
                contentPackage: parentEvent.Prefab.ContentPackage);
        }
    }

    protected override bool? DetermineSuccess()
    {
        if (Level.Loaded == null) { return false; }
        return Level.Loaded.Difficulty >= MinDifficulty && Level.Loaded.Difficulty <= MaxDifficulty;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(CheckDifficultyAction)} -> (min: {MinDifficulty}, max: {MaxDifficulty}" +
               $" Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})";
    }
}