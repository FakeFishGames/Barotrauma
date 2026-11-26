using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

partial class ResearchManager
{
    public void ClientSync()
    {
        GameNetwork.Send(Plugin.NetworkHeaders.ClientSetResearch, new ClientSetActiveResearchNetworkData(ActiveResearch?.Prefab.Identifier ?? Identifier.Empty));
    }

    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.UpdateResearch)]
    public static void ClientUpdateResearch(UpdateResearchNetworkData researchData)
    {
        if (GameMain.GameSession == null)
        {
            return;
        }

        if (!TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
        {
            return;
        }

        if (!TechnologyPrefab.Prefabs.TryGet(researchData.TechnologyIdentifier, out TechnologyPrefab? researchPrefab))
        {
            DebugConsole.AddWarning($"Failed to update research {researchData.TechnologyIdentifier}");
            return;
        }

        Research? research = researchManager.GetOrAddResearch(researchPrefab);

        research.Progress = researchData.Progress;

        if (researchData.IsActiveResearch)
        {
            researchManager.ActiveResearch = research;
        }
        else if (researchManager.ActiveResearch == research)
        {
            researchManager.ActiveResearch = null;
        }

        if (OutpostManagementUI.Instance is { } instance)
        {
            foreach (TechnologyUI techUi in instance.TechnologyUIs)
            {
                techUi.RecreateFrames();
            }
            instance.OutpostOverviewUI?.Refresh();
        }
    }

    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.SyncResearch)]
    public static void ClientSyncResearch(SyncResearchNetworkData syncResearchData)
    {
        if (!TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
        {
            return;
        }

        syncResearchData.ResearchData.ForEach(data => ClientUpdateResearch(data));
    }
}
