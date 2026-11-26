#nullable enable

using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

public partial class Plugin
{
    public partial void InitProjectSpecific()
    {
        DebugConsole.RegisterCommand(
            command: "toggleoutpostownership",
            helpMessage: "Toggle whether the player owns the current outpost.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: (args) => { OnOutpostOwnershipCommandExecuted(); },
            onCommandExecutedByClient: (client, cursorPos, args) => { OnOutpostOwnershipCommandExecuted(client); });

        HookService.RegisterHook<PluginClientJoinedRoundDelegate>(OutpostBuilder.ClientJoined);        
        HookService.RegisterHook<PluginClientJoinedRoundDelegate>(LocationShortcutManager.OnClientJoined);
    }

    private static void OnOutpostOwnershipCommandExecuted(Client? client = null)
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
        if (client != null)
        {
            GameMain.Server.SendChatMessage($"{client.Name} {(outpostData.IsPlayerOwned ? "Set" : "Unset")} the player as the owner of the location {campaign.CurrentLocation.DisplayName}.",
                ChatMessageType.Console);
        }
    }
}