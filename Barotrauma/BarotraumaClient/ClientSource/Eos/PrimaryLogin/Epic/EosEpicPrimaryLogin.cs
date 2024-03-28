#nullable enable
using System;
using System.Threading.Tasks;

namespace Barotrauma.Eos;

/// <summary>
/// Handles a player that owns a copy of Barotrauma on Epic Games Store (therefore they
/// will use their Epic Account ID as their primary identity) logging into EOS.
/// </summary>
static class EosEpicPrimaryLogin
{
    public static void Start(string exchangeCode)
    {
        TaskPool.Add("Eos.Core.LoginEpic", Initialize(exchangeCode), t =>
        {
            if (!t.TryGetResult(out Action? action)) { return; }
            action();
        });
    }

    private static void Success()
    {
        Eos.EosAccount.CloseMessageBox();
        Eos.EosAccount.RefreshSelfAccountIds();
        EosAccount.OnLoginSuccess();
    }

    private static async Task<Action> Initialize(string exchangeCode)
    {
        void retry() => Start(exchangeCode);
        static void cancel() => EosInterface.Core.CleanupAndQuit();

        var failedToInitializeIntro = TextManager.Get("EosFailedToInitialize");

        var loginResult = await EosInterface.Login.LoginEpicExchangeCode(exchangeCode);
        if (!loginResult.TryUnwrapSuccess(out var either))
        {
            LocalizedString localizedError = $"Login failed with unknown error code.";
            if (loginResult.TryUnwrapFailure(out EosInterface.Login.LoginError errorCode))
            {
                localizedError = TextManager
                    .Get($"EosInterface.Core.InitError.{errorCode}")
                    .Fallback($"Failed to initialize Epic Online Services (error code {errorCode})");
            }
            return EosAccount.RetryAction(failedToInitializeIntro, localizedError, retry, cancel);
        }

        if (either.TryGet(out EosInterface.EosConnectContinuanceToken eosContinuanceToken))
        {
            var createProductAccountResult = await EosInterface.Login.CreateProductAccount(eosContinuanceToken);
            if (!createProductAccountResult.TryUnwrapSuccess(out var puid))
            {
                return EosAccount.RetryAction(
                    failedToInitializeIntro,
                    $"Failed to create product user account: {(createProductAccountResult.TryUnwrapFailure(out var failure) ? failure : "unknown")}",
                    retry, cancel);
            }
            DebugConsole.NewMessage($"Logged into EOS for the first time with Epic as primary external account ID: {puid}");
            return Success;
        }
        else if (either.TryGet(out EosInterface.ProductUserId puid))
        {
            DebugConsole.NewMessage($"Logged into EOS with Epic as primary external account ID: {puid}");
            return Success;
        }

        throw new UnreachableCodeException();
    }
}