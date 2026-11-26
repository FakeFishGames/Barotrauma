using Barotrauma.Networking;
using Barotrauma.Steam;

namespace Barotrauma;

static class SocialExtensions
{
    public static LocalizedString ViewProfileLabel(this AccountId accountId)
        => accountId switch
        {
            SteamId => TextManager.Get("ViewSteamProfile"),
            _ => "View profile of unknown origin"
        };

    public static void OpenProfile(this AccountId accountId)
    {
        string url = accountId switch
        {
            SteamId steamId => $"https://steamcommunity.com/profiles/{steamId.Value}",
            _ => ""
        };

        if (SteamManager.IsInitialized)
        {
            SteamManager.OverlayCustomUrl(url);
        }
        else
        {
            GameMain.ShowOpenUriPrompt(url);
        }
    }
}