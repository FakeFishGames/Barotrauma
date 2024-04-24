#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;

namespace Barotrauma.Eos;

/// <summary>
/// Handles a player that owns a copy of Barotrauma on Steam,
/// and wishes to link their Steam account to an Epic account
/// to interact with friends on Epic Games' account system.
/// </summary>
static class EosEpicSecondaryLogin
{
    public enum ProbeResult
    {
        NoAccount,
        LinkedExternalAccountsButNoPuid,
        LoggedIn
    }

    public static async Task<ProbeResult> ProbeLinkedEpicAccount()
    {
        var loginResult = await EosInterface.Login.LoginEpicWithLinkedSteamAccount(EosInterface.Login.LoginEpicFlags.FailWithoutOpeningBrowser);
        if (!loginResult.TryUnwrapSuccess(out var success)) { return ProbeResult.NoAccount; }

        if (success.TryGet(out EosInterface.ProductUserId _))
        {
            // Make Steam account the primary external account just in case
            await EosInterface.Login.LoginSteam();

            return ProbeResult.LoggedIn;
        }
        if (success.TryGet(out EosInterface.EosConnectContinuanceToken? _))
        {
            return ProbeResult.LinkedExternalAccountsButNoPuid;
        }

        return ProbeResult.NoAccount;
    }

    public enum LoginErrorDesc
    {
        NoPrimaryPuid,

        FailedToLogInViaLinkedEpicAccount,

        FailedToForceSteamAsPrimaryExternalAccountId,
        SteamIsNoLongerLinkedToPuid,
        SteamPuidMismatchedPreviousPrimaryPuid,

        FailedToLinkSteamAccountToEpicAccount,
        FailedToCreatePuidForEpicAccount,

        UnhandledErrorCondition
    }

    public readonly record struct LoginError(
        LoginErrorDesc ErrorDesc,
        Option<EosInterface.Login.LoginError> LoginEosConnectError = default,
        Option<EosInterface.Login.LinkExternalAccountToEpicAccountError> LinkExternalToEpicError = default,
        Option<EosInterface.Login.CreateProductAccountError> CreatePuidError = default)
    {
        public override string ToString()
        {
            string error = $"LoginError ({ErrorDesc}";
            if (LoginEosConnectError.TryUnwrap(out var connectError))
            {
                error += $", {connectError}";
            }
            if (LinkExternalToEpicError.TryUnwrap(out var externalToEpicError))
            {
                error += $", {externalToEpicError}";
            }
            if (CreatePuidError.TryUnwrap(out var createPuidError))
            {
                error += $", {createPuidError}";
            }
            return error + ")";
        }
    }

    public static async Task<Result<Unit, LoginError>> LoginToLinkedEpicAccount()
    {
        var primaryPuidOption = EosInterface.IdQueries.GetLoggedInPuids().FirstOrNone();
        if (!primaryPuidOption.TryUnwrap(out var primaryPuid)) { return Result.Failure(new LoginError(LoginErrorDesc.NoPrimaryPuid)); }

        // No matter what happens, refresh account IDs when returning
        using var janitor = Janitor.Start();
        janitor.AddAction(static () => EosAccount.RefreshSelfAccountIds());

        async Task<Result<Unit, LoginError>> makeSteamPrimaryExternalAccount()
        {
            // By logging into EOS connect via Steam, we make sure that it's
            // treated as the primary external account, which means that it's
            // prioritized over the Epic account ID in other EOS functions
            var loginSteamResult = await EosInterface.Login.LoginSteam();
            if (!loginSteamResult.TryUnwrapSuccess(out var loginSteamSuccess))
            {
                return Result.Failure(new LoginError(LoginErrorDesc.FailedToForceSteamAsPrimaryExternalAccountId));
            }

            if (!loginSteamSuccess.TryGet(out EosInterface.ProductUserId primaryPuidAgain))
            {
                return Result.Failure(new LoginError(LoginErrorDesc.SteamIsNoLongerLinkedToPuid));
            }

            if (primaryPuid != primaryPuidAgain)
            {
                return Result.Failure(new LoginError(LoginErrorDesc.SteamPuidMismatchedPreviousPrimaryPuid));
            }

            return Result.Success(Unit.Value);
        }

        const int MaxLoginPasses = 5;
        for (int loginPass = 0; loginPass < MaxLoginPasses; loginPass++)
        {
            // Try to log into EOS via Epic via Steam several times,
            // only stop once we get a PUID that's linked only to the Epic account

            if (EosInterface.IdQueries.GetLoggedInEpicIds() is { Length: > 0 } loggedInEpicIds)
            {
                // Log out of any Epic accounts to reduce chances of ending up in an inconsistent state
                await Task.WhenAll(loggedInEpicIds.Select(EosInterface.Login.LogoutEpicAccount));
            }

            var loginResult = await EosInterface.Login.LoginEpicWithLinkedSteamAccount(
                loginPass == 0
                 ? EosInterface.Login.LoginEpicFlags.None
                 : EosInterface.Login.LoginEpicFlags.FailWithoutOpeningBrowser);

            if (loginResult.TryUnwrapFailure(out var loginEpicFailure))
            {
                return Result.Failure(new LoginError(LoginErrorDesc.FailedToLogInViaLinkedEpicAccount, LoginEosConnectError: Option.Some(loginEpicFailure)));
            }
            if (!loginResult.TryUnwrapSuccess(out var loginEpicSuccess))
            {
                throw new UnreachableCodeException();
            }

            if (loginEpicSuccess.TryGet(out EosInterface.ProductUserId secondPuid))
            {
                if (primaryPuid == secondPuid)
                {
                    // Somehow we've got two external accounts linked
                    // to the same PUID, let's yank them apart

                    // Given that the latest ID used to log into this account was
                    // the Epic account, this call to UnlinkExternalAccount will
                    // keep the SteamID linked to this PUID and only unlink the
                    // Epic account, which is what we want.
                    await EosInterface.Login.UnlinkExternalAccount(secondPuid);

                    // Once that's done, log back into EOS Connect with
                    // the SteamID as the primary ID
                    if ((await makeSteamPrimaryExternalAccount()).TryUnwrapFailure(out var loginSteamError))
                    {
                        return Result.Failure(loginSteamError);
                    }
                }
                else
                {
                    // We already have a PUID for this Epic account. We're done here!

                    return Result.Success(Unit.Value);
                }
            }
            else if (loginEpicSuccess.TryGet(out EosInterface.EgsAuthContinuanceToken? egsAuthContinuanceToken))
            {
                // We got an EGS Auth continuance token, which means that the player
                // has provided an Epic account to link the current Steam account to.
                var linkExternalToEpicResult = await EosInterface.Login.LinkExternalAccountToEpicAccount(egsAuthContinuanceToken);
                if (linkExternalToEpicResult.TryUnwrapFailure(out var linkExternalToEpicError))
                {
                    return Result.Failure(new LoginError(LoginErrorDesc.FailedToLinkSteamAccountToEpicAccount, LinkExternalToEpicError: Option.Some(linkExternalToEpicError)));
                }
            }
            else if (loginEpicSuccess.TryGet(out EosInterface.EosConnectContinuanceToken? eosConnectContinuanceToken))
            {
                // We got an EOS Connect continuance token, we need
                // a new Product User ID for the given Epic account.

                var createPuidResult = await EosInterface.Login.CreateProductAccount(eosConnectContinuanceToken);
                if (createPuidResult.TryUnwrapFailure(out var createPuidError))
                {
                    return Result.Failure(new LoginError(LoginErrorDesc.FailedToCreatePuidForEpicAccount, CreatePuidError: Option.Some(createPuidError)));
                }

                // PUID has been created! We're done here!
                return Result.Success(Unit.Value);
            }
            else
            {
                throw new UnreachableCodeException();
            }
        }

        return Result.Failure(new LoginError(LoginErrorDesc.UnhandledErrorCondition));
    }
}