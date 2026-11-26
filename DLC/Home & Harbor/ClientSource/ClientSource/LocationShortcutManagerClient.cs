#nullable enable

using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

public partial class LocationShortcutManager
{
    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.UpdateShortcutsUnlocked)]
    public static void ClientReceiveShortcutsUnlocked(ShortcutsUnlockedNetworkData data)
    {
        if (GameMain.GameSession?.Campaign is not CampaignMode campaign) { return; }
        SetShortcutsUnlocked(campaign, data.Unlocked, createNetworkEvent: false);
    }
}
