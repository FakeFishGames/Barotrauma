#nullable enable
using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

public partial class Plugin
{
    public partial void InitProjectSpecific()
    {
        HookService.RegisterHook<PluginRoundEndedDelegate>(OutpostManagementUI.RoundEnded);
        HookService.RegisterHook<PluginGameModeUpdateDelegate>(OutpostManagementUI.UpdateUI);
        HookService.RegisterHook<PluginGameModeAddToGUIUpdateListDelegate>(OutpostManagementUI.AddToGUIUpdateList);
        HookService.RegisterHook<PluginHandleEscapeKeyDelegate>(OutpostManagementUI.HandleEscapeKey);

        HookService.RegisterHook<PluginRoundStartedDelegate>(LocationShortcutManager.OnRoundStarted);
        HookService.RegisterHook<PluginRoundEndedDelegate>(LocationShortcutManager.OnRoundEnded);

        HookService.RegisterHook<PluginCampaignMapDrawLocationDelegate>(LocationShortcutManager.OnDrawMapLocation);
        HookService.RegisterHook<PluginCampaignMapBeforeDrawDelegate>(LocationShortcutManager.OnBeforeDrawMap);
        HookService.RegisterHook<PluginCampaignMapAfterDrawDelegate>(LocationShortcutManager.OnAfterDrawMap);
        HookService.RegisterHook<PluginCampaignMapUpdateDelegate>(LocationShortcutManager.OnUpdateMap);
        HookService.RegisterHook<PluginCampaignSelectTabDelegate>(LocationShortcutManager.OnCampaignSelectTab);
        HookService.RegisterHook<PluginCampaignDrawUIDelegate>(LocationShortcutManager.OnDrawCampaignUI);

        DebugConsole.RegisterCommand(
            command: "toggleoutpostownership",
            helpMessage: "Toggle whether the player owns the current outpost.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: (args) => { OnOutpostOwnershipCommandExecuted(); });
    }

    private static void OnOutpostOwnershipCommandExecuted()
    {
        if (GameMain.GameSession.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError("No campaign running.");
            return;
        }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
        outpostData.SetPlayerOwned(!outpostData.IsPlayerOwned);
        //reset previous faction so the faction doesn't try to reclaim the outpost
        campaign.CurrentLocation.PreviousFaction = null;
        DebugConsole.NewMessage((outpostData.IsPlayerOwned ? "Set" : "Unset") + $" the player as the owner of the location {campaign.CurrentLocation.DisplayName}.");
    }
}