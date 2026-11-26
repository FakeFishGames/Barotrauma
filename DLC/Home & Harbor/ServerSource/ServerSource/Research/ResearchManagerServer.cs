using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

partial class ResearchManager
{
    static partial void InitProjectSpecific()
    {
        HookService.RegisterHook<PluginClientJoinedRoundDelegate>(SyncResearchData);
    }

    private static void SyncResearchData(Client client)
    {
        if (!TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
        {
            return;
        }

        GameNetwork.SendToClient(client, Plugin.NetworkHeaders.SyncResearch,
            new SyncResearchNetworkData(researchManager.currentResearches, researchManager.ActiveResearch));
    }

    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.ClientSetResearch)]
    public static void OnClientRequestSetActiveResearch(ClientSetActiveResearchNetworkData data, Client client)
    {
        if (!TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager)) { return; }

        if (!CampaignMode.AllowedToManageCampaign(client, ClientPermissions.ManageCampaign)) { return; }

        if (data.ResearchPrefab is { } prefab)
        {
            if (!researchManager.HasAllPrerequisites(prefab)) { return; }
            researchManager.SelectActiveResearch(prefab);

            GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.clientselectedresearch",
                ("[research]", prefab.Name),
                ("[client]", NetworkMember.ClientLogName(client))));
        }
        else
        {
            researchManager.ClearActiveResearch();
        }
    }
}
