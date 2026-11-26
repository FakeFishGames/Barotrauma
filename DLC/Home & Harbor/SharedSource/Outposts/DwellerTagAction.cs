using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// TagAction specific to player-owned outpost dwellers, won't work elsewhere.
/// Works exactly like regular TagAction, inheriting all its functionality, but only considers actual outpost dwellers
/// so there's no need to try and distinguish them otherwise.
/// Also forces the team filter to be FriendlyNPC (which player-owned outpost dwellers will always be), thus it doesn't
/// need to be specified explicitly for the criteria that would normally require it.
///
/// If not in a player-owned outpost, will recheck periodically if that changes, instead of polling every frame.
/// </summary>
internal class DwellerTagAction : TagAction
{
    private readonly PeriodicValidationTimer validOutpostTimer = new();
    private bool inPlayerOwnedOutpost = false;

    private bool disallowDwellersWithCampaignInteraction;

    // If set, only dwellers assigned to a workstation with one of these item identifiers are considered valid candidates
    private readonly Identifier[] requireWorkstation;

    public DwellerTagAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        disallowDwellersWithCampaignInteraction = element.GetAttributeBool(nameof(disallowDwellersWithCampaignInteraction), false);
        requireWorkstation = element.GetAttributeIdentifierArray(nameof(requireWorkstation), []);
    }

    public override void Update(float deltaTime)
    {
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
                // Not yet, try again later
                return;
            }

            // In a player-owned outpost now, proceed with the tagging
            inPlayerOwnedOutpost = true;
        }

        base.Update(deltaTime);
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        base.Reset();
        validOutpostTimer.Reset();
        inPlayerOwnedOutpost = false;
    }

    protected override bool AdditionalCharacterFilter(Character character)
    {
        if (Level.Loaded?.StartLocation is not Location startLocation ||
            Level.Loaded.Type != LevelData.LevelType.Outpost ||
            startLocation.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is not { IsPlayerOwned: true } currentOutpostData)
        {
            return false;
        }

        if (disallowDwellersWithCampaignInteraction && 
            character.CampaignInteractionType != CampaignMode.InteractionType.None)
        {
            return false;
        }

        // Hardcode the team selection since it'll only ever have one valid option in this case, and so many criteria require it to be set to something to work
        if (Team != CharacterTeam.Any && Team != CharacterTeam.FriendlyNPC)
        {
            DebugConsole.AddWarning($"{nameof(DwellerTagAction)} in {GetEventDebugName()}: Team filter cannot be anything other than FriendlyNPC for dwellers (and thus doesn't need to be set at all).");
        }
        Team = CharacterTeam.FriendlyNPC;

        // Only include characters that are registered as dwellers in this particular outpost
        if (currentOutpostData.DwellerManager.FindDwellerInfo(character) == null)
        {
            return false;
        }

        // If a workstation requirement is set, only include dwellers currently assigned to one of those workstations
        if (requireWorkstation.Length > 0)
        {
            WorkstationComponent? workstation = currentOutpostData.DwellerManager.GetAssignedWorkstation(character);
            if (workstation == null || !requireWorkstation.Contains(workstation.Item.Prefab.Identifier))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToDebugString()
    {
        return base.ToDebugString().Replace("TagAction", nameof(DwellerTagAction));
    }
}