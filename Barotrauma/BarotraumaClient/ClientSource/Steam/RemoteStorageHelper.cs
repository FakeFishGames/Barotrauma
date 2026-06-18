#nullable enable
using Steamworks;
using System;
namespace Barotrauma.Steam;

internal static partial class RemoteStorageHelper
{
    /// <summary>
    /// Asks the user if they wish to enable remote storage. Accepting enables it automatically.
    /// </summary>
    /// <param name="onAccepted">Invoked when the user accepts enabling remote storage.</param>
    /// <param name="onRejected">Invoked when the user rejects enabling remote storage.</param>
    /// <remarks>Closes automatically if remote storage was enabled outside of the game, or if remote storage can not be enabled.</remarks>
    public static void AskToEnable(Action? onAccepted = null, Action? onRejected = null)
    {
        GUIMessageBox confirmBox = new GUIMessageBox(
            TextManager.Get("RemoteStorageEnablePopup.Header"), 
            TextManager.Get("RemoteStorageEnablePopup.Text"),
            [TextManager.Get("Yes"), TextManager.Get("No")], 
            autoCloseCondition: () => !SteamRemoteStorage.IsCloudEnabledForAccount || SteamRemoteStorage.IsCloudEnabledForApp);

        confirmBox.Buttons[0].OnClicked += (btn, data) =>
        {
            SteamRemoteStorage.IsCloudEnabledForApp = true;
            onAccepted?.Invoke();
            return confirmBox.Close(btn, data);
        };
        confirmBox.Buttons[1].OnClicked += (btn, data) =>
        {
            onRejected?.Invoke();
            return confirmBox.Close(btn, data);
        };
    }
}