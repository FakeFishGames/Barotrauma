#nullable enable
using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Can be used to check whether a specific technology has been researched/unlocked by the player.
/// </summary>
public class CheckTechnologyAction : BinaryOptionAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the technology to check.")]
    public Identifier TechnologyIdentifier { get; set; } = Identifier.Empty;

    public CheckTechnologyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    {
        if (string.IsNullOrEmpty(TechnologyIdentifier.Value))
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(CheckTechnologyAction)} is missing the required attribute '{nameof(TechnologyIdentifier)}'.");
        }
        if (!TechnologyPrefab.Prefabs.ContainsKey(TechnologyIdentifier.Value))
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier} ({nameof(CheckTechnologyAction)}): could not find the technology \"{TechnologyIdentifier.Value}\".");

        }
    }

    protected override bool? DetermineSuccess()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            return false;
        }
        if (ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager))
        {
            return researchManager.IsFinished(TechnologyIdentifier);
        }
        else
        {

            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: failed to find an active {nameof(ResearchManager)}.");
            return false;
        }
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckTechnologyAction)} " +
            $"-> (Technology: {TechnologyIdentifier}, Success: {succeeded.ColorizeObject()})";
    }
}
