namespace HomeAndHarbor;

using Barotrauma;
using Barotrauma.Plugins;

partial class DlcEndingManager
{
    private DlcEndingScreen? EndingScreen;
    private SlideshowPrefab? slideshowPrefab;

    // Needs to be longer than the delay between pressing the launch button and the ending being triggered.
    // Refreshed every frame once the ending has been triggered, so the music only times out if the ending stops updating (e.g. the player quit).
    private const float EndingMusicTimeout = 30.0f;

    public static readonly Identifier EndingMusicType = "dlcending".ToIdentifier();

    /// <summary>
    /// Starts the ending music. Called when the launch button is pressed, and every frame after the ending has been triggered
    /// (so clients who didn't press the button themselves also get the music, and nothing else can override it).
    /// </summary>
    public static void StartEndingMusic()
    {
        SoundPlayer.OverrideMusicDuration = EndingMusicTimeout;
        if (SoundPlayer.OverrideMusicType != EndingMusicType)
        {
            SoundPlayer.OverrideMusicType = EndingMusicType;
            SoundPlayer.ForceMusicUpdate(immediate: true);
        }
    }

    /// <summary>
    /// Ending the round may override the music (e.g. with the "end round" music), so keep restarting it until the ending is over.
    /// Done here and not in <see cref="DlcEndingScreen.Update"/>, because in multiplayer the game is paused during the campaign's level transition,
    /// which stops the screen from updating. Runs before SoundPlayer.Update, so an override set during the previous frame never gets picked up.
    /// </summary>
    private void UpdateEndingMusic()
    {
        if (startSlideShowPlayCountDown || (EndingScreen != null && Screen.Selected == EndingScreen))
        {
            StartEndingMusic();
        }
    }

    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.TriggerEnding)]
    public static void ClientTriggerEnding(TriggerEndingNetworkData triggerEndingData)
    {
        if (TryGetEndingManagerForCampaign(GameMain.GameSession?.Campaign, out DlcEndingManager? endingManager))
        {
            endingManager.TriggerEndingSlideshow(triggerEndingData.SlideshowPrefabIdentifier);
        }
    }

    partial void TriggerProjSpecific(Identifier slideshowPrefabIdentifier)
    {
        StartEndingMusic();

        GUI.DisableHUD = true;

        Item? target = Item.ItemList.FirstOrDefault(item => item.HasIdentifierOrTag(EndingItemCameraFocusTarget));

        new CameraTransition(target, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: PanToRocketDelay, fadeOut: false, losFadeIn: true, startZoom: null, endZoom: 1f * GUI.yScale)
        {
            RunWhilePaused = false,
            EndWaitDuration = TransitionDelay - PanToRocketDelay,
            LosOffDuringEndWait = true
        };

        if (Screen.Selected is GameScreen gameScreen)
        {
            gameScreen.ForceFadeToBlack = true;
            gameScreen.FadeToBlackSpeed = 1f / (FadeToBlackDelay);
        }

        if (!SlideshowPrefab.Prefabs.TryGet(slideshowPrefabIdentifier, out slideshowPrefab))
        {
            DebugConsole.ThrowError($"Slideshow {slideshowPrefabIdentifier} not found");
            return;
        }
    }
}
