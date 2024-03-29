#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;

namespace Barotrauma.Eos;

internal static class EosAccount
{
    /// <summary>
    /// The user can have several account IDs if they've linked their Steam account to an Epic Games account
    /// </summary>
    public static ImmutableHashSet<AccountId> SelfAccountIds { get; private set; } = ImmutableHashSet<AccountId>.Empty;

    private static readonly Queue<Action> postLoginActions = new();
    private static bool isLoggedIn;

    public static void RefreshSelfAccountIds(Action? onRefreshComplete = null)
    {
        SelfAccountIds = ImmutableHashSet<AccountId>.Empty;
        var selfPuids = EosInterface.IdQueries.GetLoggedInPuids();

        if (selfPuids.Length == 0)
        {
            onRefreshComplete?.Invoke();
            return;
        }

        var collectedIds = new Option<ImmutableArray<AccountId>>[selfPuids.Length];

        Action<Task> taskDoneHandler(int index)
        {
            void countDoneTask(Task t)
            {
                try
                {
                    if (!t.TryGetResult(out Result<ImmutableArray<AccountId>, EosInterface.IdQueries.GetSelfExternalIdError>? result)) { return; }
                    if (!result.TryUnwrapSuccess(out var ids)) { return; }
                    collectedIds[index] = Option.Some(ids);
                }
                finally
                {
                    // If we failed to get IDs from this query, fill in the relevant slot in the collectedIds array
                    // to indicate that the task is done anyway
                    collectedIds[index] = Option.Some(collectedIds[index].Fallback(ImmutableArray<AccountId>.Empty));

                    // If all of the tasks are done, merge all of the collected IDs into the hashset
                    if (collectedIds.All(o => o.IsSome()))
                    {
                        SelfAccountIds = collectedIds.NotNone().SelectMany(a => a).ToImmutableHashSet();
                        onRefreshComplete?.Invoke();
                    }
                }
            }

            return countDoneTask;
        }

        for (int i = 0; i < selfPuids.Length; i++)
        {
            TaskPool.Add($"SelfPlayerRowWithExternalAccountIds{i}",
                EosInterface.IdQueries.GetSelfExternalAccountIds(selfPuids[i]),
                taskDoneHandler(i));
        }
    }

    #region Message box stuff
    private static GUIMessageBox? messageBox;

    public static void ReplaceMessageBox(GUIMessageBox? newMessageBox)
    {
        messageBox?.Close();
        messageBox = newMessageBox;
    }

    public static void CloseMessageBox()
        => ReplaceMessageBox(null);

    public static GUIMessageBox CreateLoadingMessageBox((Func<bool> CanCancel, Action Cancel)? actions = null)
    {
        var relativeSize = messageBox?.InnerFrame.RectTransform.RelativeSize ?? (0.35f, 0.3f);
        var newMessageBox = new GUIMessageBox(
            headerText: LocalizedString.EmptyString,
            text: LocalizedString.EmptyString,
            relativeSize: relativeSize,
            buttons: actions != null ? new[] { TextManager.Get("Cancel") } : Array.Empty<LocalizedString>());

        if (actions != null)
        {
            newMessageBox.Buttons[0].Visible = false;
            newMessageBox.Buttons[0].OnClicked = (_, _) =>
            {
                actions.Value.Cancel.Invoke();
                return false;
            };
            new GUICustomComponent(new RectTransform(Vector2.Zero, newMessageBox.InnerFrame.RectTransform), onUpdate:
                (_, _) =>
            {
                bool canCancel = actions.Value.CanCancel.Invoke();
                newMessageBox.Buttons[0].Visible |= canCancel;
                newMessageBox.Buttons[0].Enabled = canCancel;
            });
        }

        new GUICustomComponent(
            new RectTransform(Vector2.One * 0.25f, newMessageBox.InnerFrame.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.Smallest),
            onDraw: static (sb, component) =>
            {
                GUIStyle.GenericThrobber.Draw(
                    sb,
                    spriteIndex: (int)(Timing.TotalTime * 20f) % GUIStyle.GenericThrobber.FrameCount,
                    pos: component.Rect.Center.ToVector2(),
                    color: Color.White,
                    origin: GUIStyle.GenericThrobber.FrameSize.ToVector2() * 0.5f,
                    rotate: 0f,
                    scale: component.Rect.Size.ToVector2() / GUIStyle.GenericThrobber.FrameSize.ToVector2());
            });
        ReplaceMessageBox(newMessageBox);
        return newMessageBox;
    }

    public static Action RetryAction(LocalizedString intro, LocalizedString reason, Action retryAction, Action cancelAction)
    {
        return () => GameMain.ExecuteAfterContentFinishedLoading(() => AskRetry(intro, reason, retryAction, cancelAction));
    }

    private static void AskRetry(LocalizedString intro, LocalizedString failureReason, Action retryAction, Action cancelAction)
    {
        var options = new[]
        {
            TextManager.Get("Retry"),
            TextManager.Get("Cancel")
        };
        var askHowToProceed = TextManager.Get("AskHowToProceed");

        GUIMessageBox msgBox = new GUIMessageBox(
            headerText: TextManager.Get("EosIntroHeader"),
            text: intro + "\n\n" + failureReason + "\n\n" + askHowToProceed,
            options,
            relativeSize: (0.4f, 0.4f));

        msgBox.Buttons[0].OnClicked = delegate
        {
            retryAction();
            CloseMessageBox();
            return false;
        };

        msgBox.Buttons[1].OnClicked = delegate
        {
            cancelAction();
            CloseMessageBox();
            return false;
        };
        
        ReplaceMessageBox(msgBox);
    }
    #endregion

    public static void LoginPlatformSpecific()
    {
        if (GameMain.Instance.EgsExchangeCode.TryUnwrap(out var exchangeCode))
        {
            LoginEpic(exchangeCode);
        }
        else if (SteamManager.IsInitialized)
        {
            LoginSteam();
        }
    }

    private static void LoginSteam()
        => EosSteamPrimaryLogin.Start();

    private static void LoginEpic(string exchangeCode)
        => EosEpicPrimaryLogin.Start(exchangeCode);

    public static void OnLoginSuccess()
    {
        isLoggedIn = true;
        while (postLoginActions.TryDequeue(out var action))
        {
            action();
        }
    }

    public static void ExecuteAfterLogin(Action action)
    {
        if (isLoggedIn)
        {
            action();
            return;
        }
        postLoginActions.Enqueue(action);
    }
}