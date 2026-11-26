namespace HomeAndHarbor;

using Barotrauma;
using Barotrauma.Plugins;
using System.Diagnostics.CodeAnalysis;

public partial class DlcEndingManager
{
    [NetworkSerialize]
    public readonly record struct TriggerEndingNetworkData(Identifier SlideshowPrefabIdentifier) : INetSerializableStruct;

    private static Identifier EndingItemCameraFocusTarget => "endingfocus".ToIdentifier();

    private const float TransitionDelay = 14f;
    private const float PanToRocketDelay = 6f;
    private const float FadeToBlackDelay = 10f;

    private bool startSlideShowPlayCountDown;
    private float slideShowTime;

    static DlcEndingManager()
    {
        HookService.RegisterHook<PluginOnPreUpdateDelegate>(OnPreUpdate);
    }

    public static bool TryGetEndingManagerForCampaign(CampaignMode? campaignMode, [NotNullWhen(true)] out DlcEndingManager? endingManager)
    {
        if (campaignMode is null)
        {
            endingManager = null;
            return false;
        }

        endingManager = campaignMode.GetOrAddExtraField(nameof(DlcEndingManager), new DlcEndingManager());
        return true;
    }

    private static void OnPreUpdate(float deltaTime)
    {
        if (TryGetEndingManagerForCampaign(GameMain.GameSession?.Campaign, out DlcEndingManager? endingManager))
        {
            endingManager.Update(deltaTime);
        }
    }

    public void TriggerEndingSlideshow(Identifier slideshowPrefabIdentifier)
    {
        startSlideShowPlayCountDown = true;
        slideShowTime = TransitionDelay;

        TriggerProjSpecific(slideshowPrefabIdentifier);
    }

    partial void TriggerProjSpecific(Identifier slideshowPrefabIdentifier);

    public void Update(float deltatime)
    {
#if CLIENT
        if (slideshowPrefab == null) { return; }
        UpdateEndingMusic();
        if (GameMain.Instance.Paused) { return; }
#endif

        if (startSlideShowPlayCountDown)
        {
            slideShowTime -= deltatime;

            if (slideShowTime <= 0)
            {
                startSlideShowPlayCountDown = false;

#if CLIENT
                EndingScreen = new DlcEndingScreen(slideshowPrefab);

                EndingScreen.OnFinished = () =>
                {
                    if (GameMain.Client is not null)
                    {
                        GameMain.NetLobbyScreen.Select();
                        if (GameMain.NetLobbyScreen.QuitCampaignButton != null) 
                        { 
                            GameMain.NetLobbyScreen.QuitCampaignButton.Enabled = false; 
                        }
                    }
                    else
                    {
                        GameMain.MainMenuScreen.Select();
                    }
                };
#endif

                if (GameMain.GameSession.Campaign is { } campaign)
                {
                    if (campaign is MultiPlayerCampaign mpCampaign)
                    {
#if SERVER
                        mpCampaign.SavePlayers();
#endif
                    }
                    campaign.HandleSaveAndQuit();
                    GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);
                    SaveUtil.SaveGame(GameMain.GameSession.DataPath);
                }

#if SERVER
                GameMain.Server.EndGame(CampaignMode.TransitionType.EndCustom, wasSaved: true);
#elif CLIENT
                if (GameMain.Client == null)
                {
                    // Only end round and unload everything on singleplayer, on multiplayer the server is gonna send a message that does this for us
                    GameMain.GameSession?.EndRound(endMessage: string.Empty, createRoundSummary: false);
                    Submarine.Unload();
                }
    
                EndingScreen.Select();
#endif
            }
        }
    }
}