namespace HomeAndHarbor;

using Barotrauma;
using Barotrauma.Plugins;

/// <summary>
/// Resets the DLC state when the end of the campaign is reached. The base game clears the campaign metadata (including the DLC story progress),
/// so player-owned outposts, shortcuts and Descendants spreading (which are unlocked by the story) are reset here.
/// Research is kept, except the technologies that depend on the metadata.
/// </summary>
[RunConstructorOnInit]
public static class CampaignEndHooks
{
    static CampaignEndHooks()
    {
        HookService.RegisterHook<PluginCampaignEndDelegate>(OnCampaignEnd);
    }

    private static void OnCampaignEnd(CampaignMode campaign)
    {
        //done on both the server and the clients, so no network events needed
        foreach (Location location in campaign.Map.Locations)
        {
            PersistentOutpostData.ResetPlayerOwnedLocation(campaign, location);
        }
        LocationShortcutManager.SetShortcutsUnlocked(campaign, false, createNetworkEvent: false);
        EnableDescendantsSpreadingAction.ResetSpreading(campaign, campaign.Map);
        if (ResearchManager.TryGetResearchManagerForCampaign(campaign, out ResearchManager? researchManager))
        {
            researchManager.ResetMetadataDependentResearch();
        }
    }
}
