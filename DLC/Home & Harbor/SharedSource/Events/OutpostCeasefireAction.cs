using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Starts or ends a ceasefire between the NPCs of the current outpost and the player crew (see <see cref="HumanAIController.Ceasefire"/>).
/// The ceasefire lasts until this action is used to end it, or until the end of the round.
/// </summary>
internal class OutpostCeasefireAction : EventAction
{
    [Serialize(true, IsPropertySaveable.Yes, description: "Should the ceasefire start or end?")]
    public bool Enabled { get; set; }

    private bool isFinished = false;

    public OutpostCeasefireAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
    }

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

        if (Level.Loaded?.StartOutpost?.Info is { } outpostInfo)
        {
            foreach (Character npc in outpostInfo.OutpostNPCs.SelectMany(kvp => kvp.Value).Distinct())
            {
                //the list can also include e.g. enemies spawned by missions
                if (npc.TeamID != CharacterTeamType.FriendlyNPC) { continue; }
                if (npc.IsDead || npc.Removed) { continue; }
                if (npc.AIController is not HumanAIController humanAI) { continue; }

                humanAI.Ceasefire = Enabled;
                if (Enabled)
                {
                    //combat objectives already in progress don't re-check whether the target is hostile, so they need to be removed
                    humanAI.ObjectiveManager.CreateAutonomousObjectives();
                    humanAI.ObjectiveManager.SortObjectives();
                }
            }
        }

        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(OutpostCeasefireAction)} -> (Enabled: {Enabled.ColorizeObject()})";
    }
}
