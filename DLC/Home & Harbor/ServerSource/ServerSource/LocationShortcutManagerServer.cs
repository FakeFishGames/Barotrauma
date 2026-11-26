#nullable enable

using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

public static partial class LocationShortcutManager
{
    static partial void ServerSendShortcutsUnlocked()
    {
        if (GameMain.GameSession?.Campaign is not CampaignMode campaign) { return; }
        GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateShortcutsUnlocked,
            new ShortcutsUnlockedNetworkData(AreShortcutsUnlocked(campaign)));
    }

    public static void ServerSendShortcutsUnlocked(Client client)
    {
        if (GameMain.GameSession?.Campaign is not CampaignMode campaign) { return; }
        GameNetwork.SendToClient(client, Plugin.NetworkHeaders.UpdateShortcutsUnlocked,
            new ShortcutsUnlockedNetworkData(AreShortcutsUnlocked(campaign)));
    }

    public static void OnClientJoined(Client client)
    {
        ServerSendShortcutsUnlocked(client);
    }
}
