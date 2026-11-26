using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// Unlocks a specific technology.
/// </summary>
class UnlockTechnologyAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "The identifier of the technology to unlock.")]
    public Identifier TechnologyIdentifier { get; set; }

    public UnlockTechnologyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
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

        if (ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager))
        {
            if (TechnologyPrefab.Prefabs.TryGet(TechnologyIdentifier, out TechnologyPrefab? technology))
            {
                researchManager.Unlock(technology);
            }
            else
            {
                DebugConsole.ThrowError($"Failed to find technology with identifier {TechnologyIdentifier}");
            }
        }

        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(UnlockTechnologyAction)}";
    }
}
