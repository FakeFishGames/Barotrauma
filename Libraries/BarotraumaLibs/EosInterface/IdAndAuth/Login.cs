using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Login
    {
        private static Implementation? LoadedImplementation => Core.LoadedImplementation;

        public enum CreateProductAccountError
        {
            EosNotInitialized,
            InvalidContinuanceToken,
            Timeout,
            UnhandledErrorCondition
        }

        public static async Task<Result<ProductUserId, CreateProductAccountError>> CreateProductAccount(
            EosConnectContinuanceToken eosContinuanceToken)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.CreateProductAccount(eosContinuanceToken)
                : Result.Failure(CreateProductAccountError.EosNotInitialized);

        public enum LinkExternalAccountError
        {
            EosNotInitialized,
            InvalidContinuanceToken,
            Timeout,
            CannotLink,
            UnhandledErrorCondition
        }

        public static async Task<Result<Unit, LinkExternalAccountError>> LinkExternalAccount(ProductUserId puid,
            EosConnectContinuanceToken eosContinuanceToken)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LinkExternalAccount(puid, eosContinuanceToken)
                : Result.Failure(LinkExternalAccountError.EosNotInitialized);

        public enum UnlinkExternalAccountError
        {
            EosNotInitialized,
            FailedToGetExternalAccounts,
            NotLoggedInToGivenAccount,
            Timeout,
            CannotLink,
            InvalidUser,
            UnhandledErrorCondition
        }

        public static async Task<Result<Unit, UnlinkExternalAccountError>> UnlinkExternalAccount(ProductUserId puid)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.UnlinkExternalAccount(puid)
                : Result.Failure(UnlinkExternalAccountError.EosNotInitialized);

        public enum LoginError
        {
            EosNotInitialized,

            SteamNotLoggedIn,
            FailedToGetSteamSessionTicket,

            EgsLoginTimeout,
            EgsAccountNotFound,
            FailedToParseEgsId,
            FailedToGetEgsIdToken,
            AuthExchangeCodeNotFound,
            AuthRequiresOpeningBrowser,

            Timeout,
            InvalidUser,
            EgsAccessDenied,
            EosAccessDenied,
            UnexpectedContinuanceToken,

            UnhandledFailureCondition
        }

        [Flags]
        public enum LoginEpicFlags
        {
            None = 0x0,
            FailWithoutOpeningBrowser = 0x1
        }

        public static async
            Task<Result<OneOf<ProductUserId, EosConnectContinuanceToken, EgsAuthContinuanceToken>, LoginError>>
            LoginEpicWithLinkedSteamAccount(LoginEpicFlags flags)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LoginEpicWithLinkedSteamAccount(flags)
                : Result.Failure(LoginError.EosNotInitialized);

        public static async Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, LoginError>>
            LoginEpicExchangeCode(string exchangeCode)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LoginEpicExchangeCode(exchangeCode)
                : Result.Failure(LoginError.EosNotInitialized);

        public static async Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, LoginError>>
            LoginEpicIdToken(EgsIdToken token)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LoginEpicIdToken(token)
                : Result.Failure(LoginError.EosNotInitialized);

        public static async Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, LoginError>> LoginSteam()
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LoginSteam()
                : Result.Failure(LoginError.EosNotInitialized);

        public enum LinkExternalAccountToEpicAccountError
        {
            EosNotInitialized,

            TimedOut,
            FailedToParseEgsAccountId,

            UnhandledErrorCondition
        }

        public static async Task<Result<EpicAccountId, LinkExternalAccountToEpicAccountError>>
            LinkExternalAccountToEpicAccount(EgsAuthContinuanceToken continuanceToken)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LinkExternalAccountToEpicAccount(continuanceToken)
                : Result.Failure(LinkExternalAccountToEpicAccountError.EosNotInitialized);

        public enum LogoutEpicAccountError
        {
            EosNotInitialized,
            TimedOut,
            UnhandledErrorCondition
        }

        public static async Task<Result<Unit, LogoutEpicAccountError>> LogoutEpicAccount(EpicAccountId egsId)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.LogoutEpicAccount(egsId)
                : Result.Failure(LogoutEpicAccountError.EosNotInitialized);

        /// <summary>
        /// This is essentially a function for logging out, except EOS has no EOS_Connect_Logout function
        /// so instead we have this to fake it. Once you use this, no methods should return this PUID
        /// until you log into it again.
        /// </summary>
        public static void MarkAsInaccessible(ProductUserId puid)
        {
            if (LoadedImplementation.IsInitialized())
            {
                LoadedImplementation.MarkAsInaccessible(puid);
            }
        }

        public static Option<string> ParseEgsExchangeCode(IReadOnlyList<string> args)
        {
            if (args.Contains("-AUTH_TYPE=exchangecode", StringComparer.OrdinalIgnoreCase))
            {
                return args.FirstOrNone(arg =>
                        arg.StartsWith("-AUTH_PASSWORD=", StringComparison.OrdinalIgnoreCase))
                    .Select(arg => arg["-AUTH_PASSWORD=".Length..]);
            }

            return Option.None;
        }

        public static void TestEosSessionTimeoutRecovery(ProductUserId puid)
        {
            if (LoadedImplementation.IsInitialized())
            {
                LoadedImplementation.TestEosSessionTimeoutRecovery(puid);
            }
        }
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Result<ProductUserId, Login.CreateProductAccountError>> CreateProductAccount(
            EosConnectContinuanceToken eosContinuanceToken);

        public abstract Task<Result<Unit, Login.LinkExternalAccountError>> LinkExternalAccount(ProductUserId puid,
            EosConnectContinuanceToken eosContinuanceToken);

        public abstract Task<Result<Unit, Login.UnlinkExternalAccountError>> UnlinkExternalAccount(ProductUserId puid);

        public abstract Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, Login.LoginError>>
            LoginEpicExchangeCode(string exchangeCode);

        public abstract
            Task<Result<OneOf<ProductUserId, EosConnectContinuanceToken, EgsAuthContinuanceToken>, Login.LoginError>>
            LoginEpicWithLinkedSteamAccount(Login.LoginEpicFlags flags);

        public abstract Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, Login.LoginError>>
            LoginEpicIdToken(EgsIdToken token);

        public abstract Task<Result<Either<ProductUserId, EosConnectContinuanceToken>, Login.LoginError>> LoginSteam();

        public abstract Task<Result<EpicAccountId, Login.LinkExternalAccountToEpicAccountError>>
            LinkExternalAccountToEpicAccount(EgsAuthContinuanceToken continuanceToken);

        public abstract Task<Result<Unit, Login.LogoutEpicAccountError>> LogoutEpicAccount(EpicAccountId egsId);
        public abstract void MarkAsInaccessible(ProductUserId puid);
        public abstract void TestEosSessionTimeoutRecovery(ProductUserId puid);
    }
}