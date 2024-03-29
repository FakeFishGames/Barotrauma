#nullable enable
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma.Eos;

/// <summary>
/// Handles a player that owns a copy of Barotrauma on Steam (therefore they
/// will use their SteamID as their primary identity) logging into EOS.
/// </summary>
public static class EosSteamPrimaryLogin
{
    public static bool IsNewEosPlayer = false;

    public enum CrossplayChoice
    {
        Unknown,
        Enabled,
        Disabled
    }

    public static CrossplayChoice EnableCrossplay
    {
        get => GameSettings.CurrentConfig.CrossplayChoice;
        set
        {
            GameSettings.SetCurrentConfig(GameSettings.CurrentConfig with { CrossplayChoice = value });
            GameAnalyticsManager.AddDesignEvent("Crossplay:" + value);
            GameSettings.SaveCurrentConfig();
        }
    }

    public static void Start()
    {
        TaskPool.Add(
            "EosSteamPrimaryLogin",
            Initialize(),
            OnTaskComplete);
    }

    private static void OnTaskComplete(Task t)
    {
        if (t.Exception?.GetInnermost() is { } exception)
        {
            DebugConsole.ThrowError($"{nameof(EosSteamPrimaryLogin)}.{nameof(Initialize)} failed with exception {exception.Message} {exception.StackTrace.CleanupStackTrace()}");
        }
        if (!t.TryGetResult(out Action? action)) { return; }
        action();
    }

    private static void Success()
    {
        Eos.EosAccount.CloseMessageBox();
        Eos.EosAccount.RefreshSelfAccountIds();
        EosAccount.OnLoginSuccess();
    }

    private static async Task<Action> Initialize()
    {
        static void retry() => Start();
        static void cancel() => EosInterface.Core.CleanupAndQuit();
        var failedToInitializeIntro = TextManager.Get("EosFailedToInitialize");

        if (EnableCrossplay is CrossplayChoice.Unknown)
        {
            // Don't even try to initialize EOS until we get the user's consent
            return SteamAccountHasNoLinkedPuid();
        }
        if (EnableCrossplay is CrossplayChoice.Disabled)
        {
            // Crossplay is disabled, return immediately
            return Success;
        }

        if (!SteamManager.IsInitialized)
        {
            return EosAccount.RetryAction(failedToInitializeIntro, "Steamworks is not initialized", retry, cancel);
        }

        Result<Unit, EosInterface.Core.InitError> initResult = Result.Failure(EosInterface.Core.InitError.UnhandledErrorCondition);
        CrossThread.RequestExecutionOnMainThread(() => initResult = EosInterface.Core.Init(EosInterface.ApplicationCredentials.Client, enableOverlay: false));
        if (initResult.TryUnwrapFailure(out var initError))
        {
            return EosAccount.RetryAction(failedToInitializeIntro, GetErrorMessage(initError), retry, cancel);
        }

        var steamPuidResult = await EosInterface.Login.LoginSteam();

        if (!steamPuidResult.TryUnwrapSuccess(out var steamPuidOrContToken))
        {
            return EosAccount.RetryAction(failedToInitializeIntro, $"Failed to log into EOS with Steam account: {steamPuidResult}", retry, cancel);
        }

        if (steamPuidOrContToken.TryGet(out EosInterface.ProductUserId puid))
        {
            return await SteamAccountHasLinkedPuid(puid);
        }
        else if (steamPuidOrContToken.TryGet(out EosInterface.EosConnectContinuanceToken _))
        {
            return SteamAccountHasNoLinkedPuid();
        }
        throw new UnreachableCodeException();
    }

    private static async Task<Action> SteamAccountHasLinkedPuid(EosInterface.ProductUserId _)
    {
        await EosEpicSecondaryLogin.ProbeLinkedEpicAccount();
        return Success;
    }

    private static Action SteamAccountHasNoLinkedPuid()
    {
        return () => GameMain.ExecuteAfterContentFinishedLoading(AskPlayerToEnableCrossplay);
    }

    private static void AskPlayerToEnableCrossplay()
    {
        LocalizedString[] options =
        {
            TextManager.Get("EnableCrossplay"),
            TextManager.Get("DisableCrossplay")
        };

        var introText = "\n" + LocalizedString.Join(
            "\n\n",
            Enumerable.Range(0, 3).Select(static i => TextManager.Get($"EosIntro{i}"))) + "\n";

        GUIMessageBox msgBox = new GUIMessageBox(
            headerText: TextManager.Get("EosIntroHeader"),
            text: introText,
            Array.Empty<LocalizedString>(),
            relativeSize: (0.8f, 0.5f));
        msgBox.Content.ChildAnchor = Anchor.TopCenter;
        msgBox.Content.Stretch = true;
        msgBox.InnerFrame.RectTransform.ScaleBasis = ScaleBasis.Smallest;

        int? selectedRadioButton = null;
        var radioButtonLayout = new GUILayoutGroup(new RectTransform(Vector2.One, msgBox.Content.RectTransform)) { Stretch = true };
        var radioButtonGroup = new GUIRadioButtonGroup();
        for (int i = 0; i < options.Length; i++)
        {
            var radioButton = new GUITickBox(
                new RectTransform(Vector2.One, radioButtonLayout.RectTransform),
                label: options[i],
                style: "GUIRadioButton");
            radioButtonGroup.AddRadioButton(
                key: i,
                radioButton: radioButton);
            radioButton.RectTransform.MinSize = Point.Zero;
            radioButton.RectTransform.MaxSize = new Point(int.MaxValue);
            radioButton.RectTransform.ScaleBasis = ScaleBasis.Normal;
            radioButton.RectTransform.RelativeSize = Vector2.One;
        }

        //spacing
        new GUIFrame(new RectTransform(new Point(0), radioButtonLayout.RectTransform) { MinSize = new Point(0, GUI.IntScale(30)) }, style: null);

        var submitButton = new GUIButton(new RectTransform(Vector2.One, radioButtonLayout.RectTransform),
            TextManager.Get("Submit").Fallback("Submit")) { Enabled = false };

        radioButtonGroup.OnSelect = (rbg, val) =>
        {
            selectedRadioButton = val;
            submitButton.Enabled = true;
        };
        msgBox.ForceLayoutRecalculation();
        var maxOptionWidth = options.Select(o => GUIStyle.Font.MeasureString(o).X).Max();
        int extraWidth = (int)(GUIStyle.Font.LineHeight * 4f);
        radioButtonLayout.RectTransform.IsFixedSize = true;
        radioButtonLayout.RectTransform.NonScaledSize = new Point((int)maxOptionWidth + extraWidth, (int)(GUIStyle.Font.LineHeight * options.Length * 1.5f) + submitButton.Rect.Height * 2);
        msgBox.ForceLayoutRecalculation();

       static void textSizeFixHack(GUITextBlock textBlock, int width)
        {
            textBlock.RectTransform.IsFixedSize = true;
            textBlock.RectTransform.MinSize = Point.Zero;
            textBlock.RectTransform.MaxSize = new Point(int.MaxValue);
            textBlock.RectTransform.NonScaledSize = new Point(width, 0);
            textBlock.CalculateHeightFromText();
        }
        textSizeFixHack(msgBox.Header, (int)(msgBox.InnerFrame.Rect.Width * 0.9f));
        textSizeFixHack(msgBox.Text, (int)(msgBox.InnerFrame.Rect.Width * 0.9f));
        msgBox.ForceLayoutRecalculation();

        msgBox.InnerFrame.RectTransform.IsFixedSize = true;
        msgBox.InnerFrame.RectTransform.NonScaledSize = new Point(
            msgBox.InnerFrame.Rect.Width,
            (int)((msgBox.Content.Children.Select(c => c.Rect.Height + GUI.IntScale(5)).Sum() + GUIStyle.Font.LineHeight) / 0.9f));

        submitButton.OnClicked = delegate
        {
            switch (selectedRadioButton)
            {
                case 0:
                    PlayerWantsToEnableCrossplay();
                    return false;
                case 1:
                    PlayerWantsToDisableCrossplay();
                    return false;
                default:
                    throw new UnreachableCodeException();
            }
        };

        Eos.EosAccount.ReplaceMessageBox(msgBox);
    }

    private static void PlayerWantsToEnableCrossplay()
    {
        Eos.EosAccount.CreateLoadingMessageBox();
        TaskPool.Add(
            nameof(EnableCrossplayAndCreatePuidWithOneToken),
            EnableCrossplayAndCreatePuidWithOneToken(),
            OnTaskComplete);
    }

    private static void PlayerWantsToDisableCrossplay()
    {
        EosInterface.Core.CleanupAndQuit();
        var action = DisableCrossplay();
        action();
    }

    private static async Task<Action> EnableCrossplayAndCreatePuidWithOneToken()
    {
        void retry() => PlayerWantsToEnableCrossplay();
        static void cancel() => EosInterface.Core.CleanupAndQuit();
        var failedToCreatePuidIntro = TextManager.Get("FailedToCreatePuid");

        EnableCrossplay = CrossplayChoice.Enabled;

        if (!SteamManager.IsInitialized)
        {
            return EosAccount.RetryAction(failedToCreatePuidIntro, "Steamworks is not initialized", retry, cancel);
        }

        Result<Unit, EosInterface.Core.InitError> initResult = Result.Failure(EosInterface.Core.InitError.UnhandledErrorCondition);
        CrossThread.RequestExecutionOnMainThread(() => initResult = EosInterface.Core.Init(EosInterface.ApplicationCredentials.Client, enableOverlay: false));
        if (initResult.TryUnwrapFailure(out var initError))
        {
            return EosAccount.RetryAction(failedToCreatePuidIntro, GetErrorMessage(initError), retry, cancel);
        }

        EosInterface.EosConnectContinuanceToken steamEosContinuanceToken;
        var steamLoginResult = await EosInterface.Login.LoginSteam();
        if (steamLoginResult.TryUnwrapSuccess(out var either))
        {
            if (either.TryGet(out EosInterface.EosConnectContinuanceToken newSteamCt))
            {
                steamEosContinuanceToken = newSteamCt;
            }
            else
            {
                await EosEpicSecondaryLogin.ProbeLinkedEpicAccount();
                SocialOverlay.Instance?.DisplayBindHintToPlayer();
                return Success;
            }
        }
        else
        {
            return EosAccount.RetryAction(failedToCreatePuidIntro, $"Failed to refresh continuance token: {steamLoginResult}", retry, cancel);
        }

        var newPuidResult = await EosInterface.Login.CreateProductAccount(steamEosContinuanceToken);
        if (newPuidResult.IsFailure)
        {
            return EosAccount.RetryAction(failedToCreatePuidIntro, $"Failed to create PUID: {newPuidResult}", retry, cancel);
        }

        IsNewEosPlayer = true;
        SocialOverlay.Instance?.DisplayBindHintToPlayer();
        return Success;
    }

    private static LocalizedString GetErrorMessage(EosInterface.Core.InitError errorCode)
    {
        return TextManager.Get($"EosInterface.Core.InitError.{errorCode}").Fallback($"Failed to initialize Epic Online Services (error code {errorCode})");
    }

    private static Action DisableCrossplay()
    {
        EnableCrossplay = CrossplayChoice.Disabled;

        return Success;
    }

    public static void HandleCrossplayChoiceChange(CrossplayChoice newChoice)
    {
        if (StoreIntegration.CurrentStore != StoreIntegration.Store.Steam) { return; }
        if (GameSettings.CurrentConfig.CrossplayChoice == newChoice) { return; }

        switch (newChoice)
        {
            case CrossplayChoice.Disabled:
                EosInterface.Core.CleanupAndQuit();
                break;
            case CrossplayChoice.Enabled:
                if (EosInterface.Core.CurrentStatus == EosInterface.Core.Status.ShutDown)
                {
                    var msgBox = new GUIMessageBox(TextManager.Get("EosAllowCrossplay"),
                        TextManager.Get("RestartRequiredGeneric"), new[] { TextManager.Get("ok") })
                    {
                        DrawOnTop = true
                    };
                    msgBox.Buttons[0].OnClicked = (_, _) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                }
                else
                {
                    PlayerWantsToEnableCrossplay();
                }
                break;
        }
    }
}
