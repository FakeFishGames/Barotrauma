#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Barotrauma.Debugging;
using Barotrauma.Networking;
using Barotrauma;

namespace EosInterfacePrivate;

static class LoginPrivate
{
    private const string EosLoginSteamIdentity = "BarotraumaEosLogin";
    private static Option<Steamworks.AuthTicketForWebApi> steamworksAuthTicket;

    private static Option<ulong> eosConnectExpirationNotifyId, eosConnectStatusChangedNotifyId;
    private static Option<ulong> egsAuthExpirationNotifyId;

    internal static void Init()
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return; }
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return; }

        ClearNotificationId(ref egsAuthExpirationNotifyId, egsAuthInterface.RemoveNotifyLoginStatusChanged);
        var authExpirationOptions = new Epic.OnlineServices.Auth.AddNotifyLoginStatusChangedOptions();
        ulong authExpirationNotifyId = egsAuthInterface.AddNotifyLoginStatusChanged(ref authExpirationOptions, null, OnEgsAuthStatusChanged);
        StoreNotificationId(out egsAuthExpirationNotifyId, authExpirationNotifyId);

        ClearNotificationId(ref eosConnectExpirationNotifyId, connectInterface.RemoveNotifyAuthExpiration);
        var connectExpirationOptions = new Epic.OnlineServices.Connect.AddNotifyAuthExpirationOptions();
        ulong connectExpirationNotifyId = connectInterface.AddNotifyAuthExpiration(ref connectExpirationOptions, null, OnConnectExpiration);
        StoreNotificationId(out eosConnectExpirationNotifyId, connectExpirationNotifyId);

        ClearNotificationId(ref eosConnectStatusChangedNotifyId, connectInterface.RemoveNotifyLoginStatusChanged);
        var addNotifyConnectStatusChangedOptions = new Epic.OnlineServices.Connect.AddNotifyLoginStatusChangedOptions();
        var connectChangedNotifyId = connectInterface.AddNotifyLoginStatusChanged(ref addNotifyConnectStatusChangedOptions, null, OnConnectStatusChanged);
        StoreNotificationId(out eosConnectStatusChangedNotifyId, connectChangedNotifyId);

        static void ClearNotificationId(ref Option<ulong> field, Action<ulong> clearAction)
        {
            if (field.TryUnwrap(out var notificationId))
            {
                clearAction(notificationId);
            }
            field = Option.None;
        }

        static void StoreNotificationId(out Option<ulong> field, ulong value)
        {
            bool isValid = value is not Epic.OnlineServices.Common.InvalidNotificationid;
            field = isValid
                ? Option.Some(value)
                : Option.None;
        }
    }

    internal static readonly ConcurrentDictionary<EosInterface.ProductUserId, AccountId> PuidToPrimaryExternalId = new();

    private readonly record struct LoginParams(
        Epic.OnlineServices.Connect.Credentials Credentials,
        AccountId ExternalAccountId);
    
    private static async Task<Result<LoginParams, EosInterface.Login.LoginError>> GenCredentialsSteam()
    {
        if (!Steamworks.SteamClient.IsValid || !Steamworks.SteamClient.IsLoggedOn) { return Result.Failure(EosInterface.Login.LoginError.SteamNotLoggedIn); }
        if (steamworksAuthTicket.TryUnwrap(out var oldTicket)) { oldTicket.Cancel(); }
        var newTicketNullable = await Steamworks.SteamUser.GetAuthTicketForWebApi(EosLoginSteamIdentity);
        if (newTicketNullable is not { Data: not null } ticket)
        {
            return Result.Failure(EosInterface.Login.LoginError.FailedToGetSteamSessionTicket);
        }
        return Result.Success(
            new LoginParams(
                Credentials: new Epic.OnlineServices.Connect.Credentials
                {
                    Token = ToolBoxCore.ByteArrayToHexString(ticket.Data),
                    Type = Epic.OnlineServices.ExternalCredentialType.SteamSessionTicket
                },
                ExternalAccountId: new SteamId(Steamworks.SteamClient.SteamId)));
    }

    private static async Task<Result<Either<LoginParams, Epic.OnlineServices.ContinuanceToken>, EosInterface.Login.LoginError>> GenCredentialsEpic(
        Epic.OnlineServices.Auth.LoginCredentialType credentialsType,
        string? credentialsId,
        string? credentialsToken,
        Epic.OnlineServices.ExternalCredentialType credentialsExternalType,
        EosInterface.Login.LoginEpicFlags flags)
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return Result.Failure(EosInterface.Login.LoginError.EosNotInitialized); }

        if (credentialsType is not (
            Epic.OnlineServices.Auth.LoginCredentialType.ExternalAuth
            or Epic.OnlineServices.Auth.LoginCredentialType.Developer
            or Epic.OnlineServices.Auth.LoginCredentialType.ExchangeCode))
        {
            return Result.Failure(EosInterface.Login.LoginError.InvalidUser);
        }

        var authLoginOptions = new Epic.OnlineServices.Auth.LoginOptions
        {
            Credentials = new Epic.OnlineServices.Auth.Credentials
            {
                Id = credentialsId,
                Token = credentialsToken,
                Type = credentialsType,
                SystemAuthCredentialsOptions = default,
                ExternalType = credentialsExternalType
            },
            ScopeFlags =
                Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile
                | Epic.OnlineServices.Auth.AuthScopeFlags.Presence
                | Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList,
            LoginFlags = flags.HasFlag(EosInterface.Login.LoginEpicFlags.FailWithoutOpeningBrowser)
                ? Epic.OnlineServices.Auth.LoginFlags.NoUserInterface
                : Epic.OnlineServices.Auth.LoginFlags.None
        };

        var authLoginWaiter = new CallbackWaiter<Epic.OnlineServices.Auth.LoginCallbackInfo>();
        egsAuthInterface.Login(options: ref authLoginOptions, clientData: null, completionDelegate: authLoginWaiter.OnCompletion);

        // This can time out if authLoginOptions.ScopeFlags is set incorrectly,
        // because the docs lied and this callback isn't guaranteed to be called
        var authLoginCallbackInfoOption = await authLoginWaiter.Task;

        if (!authLoginCallbackInfoOption.TryUnwrap(out var authLoginCallbackInfo))
        {
            return Result.Failure(EosInterface.Login.LoginError.EgsLoginTimeout);
        }
        if (authLoginCallbackInfo is { ResultCode: Epic.OnlineServices.Result.InvalidUser, ContinuanceToken: { } continuanceToken })
        {
            return Result.Success((Either<LoginParams, Epic.OnlineServices.ContinuanceToken>)continuanceToken);
        }
        
        if (authLoginCallbackInfo.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(authLoginCallbackInfo.ResultCode switch {
                Epic.OnlineServices.Result.NotFound
                    => EosInterface.Login.LoginError.EgsAccountNotFound,
                Epic.OnlineServices.Result.AuthExchangeCodeNotFound
                    => EosInterface.Login.LoginError.AuthExchangeCodeNotFound,
                Epic.OnlineServices.Result.AuthUserInterfaceRequired
                    => EosInterface.Login.LoginError.AuthRequiresOpeningBrowser,
                Epic.OnlineServices.Result.AccessDenied
                    => EosInterface.Login.LoginError.EgsAccessDenied,
                _
                    => EosInterface.Login.LoginError.UnhandledFailureCondition
            });
        }
        if (!EpicAccountId.Parse(authLoginCallbackInfo.LocalUserId.ToString()).TryUnwrap(out var externalAccountId))
        {
            return Result.Failure(EosInterface.Login.LoginError.FailedToParseEgsId);
        }

        var copyIdTokenOptions = new Epic.OnlineServices.Auth.CopyIdTokenOptions
        {
            AccountId = authLoginCallbackInfo.LocalUserId
        };

        var tokenCopyResult = egsAuthInterface.CopyIdToken(ref copyIdTokenOptions, out var tokenNullable);
        if (tokenCopyResult != Epic.OnlineServices.Result.Success)
        {
            Result.Failure(EosInterface.Login.LoginError.FailedToGetEgsIdToken);
        }
        if (tokenNullable is not { } token) { return Result.Failure(EosInterface.Login.LoginError.FailedToGetEgsIdToken); }

        return Result.Success(
            (Either<LoginParams, Epic.OnlineServices.ContinuanceToken>)new LoginParams(
                Credentials: new Epic.OnlineServices.Connect.Credentials
                {
                    Token = token.JsonWebToken,
                    Type = Epic.OnlineServices.ExternalCredentialType.EpicIdToken
                },
                ExternalAccountId: externalAccountId));
    }

    public static async Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginSteam()
    {
        var credentialsSteamResult = await GenCredentialsSteam();
        if (credentialsSteamResult.TryUnwrapFailure(out var error))
        {
            return Result.Failure(error);
        }
        if (!credentialsSteamResult.TryUnwrapSuccess(out var loginParams))
        {
            return Result.Failure(EosInterface.Login.LoginError.InvalidUser);
        }

        var result = await Login(loginParams);
        if (steamworksAuthTicket.TryUnwrap(out var ticket)) { ticket.Cancel(); }
        steamworksAuthTicket = Option.None;
        return result;
    }

    public static async Task<Result<OneOf<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken, EosInterface.EgsAuthContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicWithLinkedSteamAccount(EosInterface.Login.LoginEpicFlags flags)
    {
        if (steamworksAuthTicket.TryUnwrap(out var oldTicket)) { oldTicket.Cancel(); }
        var newTicketNullable = await Steamworks.SteamUser.GetAuthTicketForWebApi(EosLoginSteamIdentity);
        if (newTicketNullable is not { Data: not null } ticket)
        {
            return Result.Failure(EosInterface.Login.LoginError.FailedToGetSteamSessionTicket);
        }
        var epicCredentialsOption = await GenCredentialsEpic(
            credentialsType: Epic.OnlineServices.Auth.LoginCredentialType.ExternalAuth,
            credentialsId: null,
            credentialsToken: ToolBoxCore.ByteArrayToHexString(ticket.Data),
            credentialsExternalType: Epic.OnlineServices.ExternalCredentialType.SteamSessionTicket,
            flags: flags);
        if (epicCredentialsOption.TryUnwrapFailure(out var epicCredentialsFail))
        {
            return Result.Failure(epicCredentialsFail);
        }
        if (!epicCredentialsOption.TryUnwrapSuccess(out var loginParamsOrContinuanceToken))
        {
            return Result.Failure(EosInterface.Login.LoginError.UnhandledFailureCondition);
        }

        if (loginParamsOrContinuanceToken.TryGet(out Epic.OnlineServices.ContinuanceToken continuanceToken))
        {
            return Result.Success((OneOf<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken, EosInterface.EgsAuthContinuanceToken>)
                new EosInterface.EgsAuthContinuanceToken(continuanceToken.InnerHandle, ExtractExpiryTimeFromContinuanceToken(continuanceToken, EosInterface.EgsAuthContinuanceToken.Duration)));
        }
        if (!loginParamsOrContinuanceToken.TryGet(out LoginParams loginParams))
        {
            return Result.Failure(EosInterface.Login.LoginError.UnexpectedContinuanceToken);
        }

        var loginResult = await Login(loginParams);
        if (loginResult.TryUnwrapSuccess(out var loginSuccess))
        {
            return loginSuccess.TryGet(out EosInterface.EosConnectContinuanceToken eosContinuanceToken)
                ? Result.Success((OneOf<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken, EosInterface.EgsAuthContinuanceToken>)eosContinuanceToken)
                : loginSuccess.TryGet(out EosInterface.ProductUserId puid)
                    ? Result.Success((OneOf<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken, EosInterface.EgsAuthContinuanceToken>)puid)
                    : Result.Failure(EosInterface.Login.LoginError.UnhandledFailureCondition);
        }
        return loginResult.TryUnwrapFailure(out var loginFailure)
            ? Result.Failure(loginFailure)
            : Result.Failure(EosInterface.Login.LoginError.UnhandledFailureCondition);
    }

    public static async Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicExchangeCode(string exchangeCode)
    {
        var epicCredentialsOption = await GenCredentialsEpic(
            credentialsType: Epic.OnlineServices.Auth.LoginCredentialType.ExchangeCode,
            credentialsId: "",
            credentialsToken: exchangeCode,
            credentialsExternalType: Epic.OnlineServices.ExternalCredentialType.Epic,
            flags: EosInterface.Login.LoginEpicFlags.None);
        if (epicCredentialsOption.TryUnwrapFailure(out var epicCredentialsFail))
        {
            return Result.Failure(epicCredentialsFail);
        }
        if (!epicCredentialsOption.TryUnwrapSuccess(out var loginParamsOrContinuanceToken))
        {
            return Result.Failure(EosInterface.Login.LoginError.UnhandledFailureCondition);
        }
        if (!loginParamsOrContinuanceToken.TryGet(out LoginParams loginParams))
        {
            return Result.Failure(EosInterface.Login.LoginError.UnexpectedContinuanceToken);
        }

        var result = await Login(loginParams);
        return result;
    }

    public static async Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicIdToken(EosInterface.EgsIdToken egsIdToken)
    {
        if (egsIdToken is not EgsIdTokenPrivate privateEgsIdToken) { return Result.Failure(EosInterface.Login.LoginError.InvalidUser); }
        var credentials = new Epic.OnlineServices.Connect.Credentials
        {
            Token = privateEgsIdToken.InternalToken.JsonWebToken,
            Type = Epic.OnlineServices.ExternalCredentialType.EpicIdToken
        };

        return await Login(new LoginParams(credentials, privateEgsIdToken.AccountId));
    }

    private static DateTime ExtractExpiryTimeFromContinuanceToken(Epic.OnlineServices.ContinuanceToken continuanceToken, TimeSpan fallbackDuration)
    {
        // Not the exact expiry time, but it's a pretty close guess should we fail to decode the continuance token
        var expiryTime = DateTime.Now + fallbackDuration;
        
        // This method exists to replace Epic.OnlineServices.ContinuanceToken.ToString because
        // the generated code is broken, and I don't want to modify it because we risk undoing
        // a fix when we update the SDK.
        static string continuanceTokenToString(Epic.OnlineServices.ContinuanceToken continuanceToken)
        {
            int inOutBufferLength = 1024;
            System.IntPtr outBufferAddress = Epic.OnlineServices.Helper.AddAllocation(inOutBufferLength);

            var funcResult = Epic.OnlineServices.Bindings.EOS_ContinuanceToken_ToString(continuanceToken.InnerHandle, outBufferAddress, ref inOutBufferLength);
            if (funcResult == Epic.OnlineServices.Result.LimitExceeded)
            {
                // Buffer wasn't large enough to copy the string.
                // inOutBufferLength was updated by the last call to be the actual length required.
                // Generate a new buffer and try again.
                Epic.OnlineServices.Helper.Dispose(ref outBufferAddress);
                outBufferAddress = Epic.OnlineServices.Helper.AddAllocation(inOutBufferLength);
                funcResult = Epic.OnlineServices.Bindings.EOS_ContinuanceToken_ToString(continuanceToken.InnerHandle, outBufferAddress, ref inOutBufferLength);
                if (funcResult != Epic.OnlineServices.Result.Success)
                {
                    DebugConsoleCore.Log($"EOS_ContinuanceToken_ToString failed with result {funcResult}");
                }
            }

            Epic.OnlineServices.Utf8String outBuffer = "EOS_ContinuanceToken_ToString failed";
            if (funcResult == Epic.OnlineServices.Result.Success)
            {
                Epic.OnlineServices.Helper.Get(outBufferAddress, out outBuffer);
            }
            Epic.OnlineServices.Helper.Dispose(ref outBufferAddress);

            return outBuffer;
        }

        var ctDecode = JsonWebToken.Parse(continuanceTokenToString(continuanceToken));
        if (ctDecode.TryUnwrap(out var jwt))
        {
            string decodedPayload = jwt.PayloadDecoded;
            try
            {
                // Ugly regex hack to get expiry time. The right thing to do would be to parse the payload as JSON,
                // but I don't really care because we're extracting one field out of this whole thing.
                string expiryTimeUnix = Regex.Match(decodedPayload, @"""exp""\s*:\s*([0-9]+)").Groups[1].Value;
                expiryTime = UnixTime.ParseUtc(expiryTimeUnix).Fallback(UnixTime.UtcEpoch).ToLocalTime();
            }
            catch
            {
                // could not extract expiry time, oh well!
            }
        }

        return expiryTime;
    }

    private static async Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> Login(LoginParams loginParams)
    {
        static Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError> success(EosInterface.ProductUserId id)
            => Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>.Success(id);
        static Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError> continuance(EosInterface.EosConnectContinuanceToken token)
            => Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>.Success(token);
        static Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError> failure(EosInterface.Login.LoginError error)
            => Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>.Failure(error);

        if (CorePrivate.ConnectInterface is not { } connectInterface) { return failure(EosInterface.Login.LoginError.EosNotInitialized); }

        var loginOptions = new Epic.OnlineServices.Connect.LoginOptions
        {
            Credentials = loginParams.Credentials,
            UserLoginInfo = null
        };
        AccountId primaryExternalId = loginParams.ExternalAccountId;

        var loginWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.LoginCallbackInfo>();
        connectInterface.Login(options: ref loginOptions, clientData: null, completionDelegate: loginWaiter.OnCompletion);
        var callbackResultOption = await loginWaiter.Task;
        if (!callbackResultOption.TryUnwrap(out var callbackResult))
        {
            return failure(EosInterface.Login.LoginError.Timeout);
        }

        if (callbackResult.ResultCode == Epic.OnlineServices.Result.Success)
        {
            var retVal = new EosInterface.ProductUserId(callbackResult.LocalUserId.ToString());
            PuidToPrimaryExternalId[retVal] = primaryExternalId;
            return success(retVal);
        }

        if (callbackResult is { ResultCode: Epic.OnlineServices.Result.InvalidUser, ContinuanceToken: { } continuanceToken })
        {
            var expiryTime = ExtractExpiryTimeFromContinuanceToken(continuanceToken, EosInterface.EosConnectContinuanceToken.Duration);

            return continuance(new EosInterface.EosConnectContinuanceToken(callbackResult.ContinuanceToken.InnerHandle, primaryExternalId, expiryTime));
        }

        return callbackResult.ResultCode switch
        {
            Epic.OnlineServices.Result.InvalidUser
                => failure(EosInterface.Login.LoginError.InvalidUser),
            Epic.OnlineServices.Result.AccessDenied
                => failure(EosInterface.Login.LoginError.EosAccessDenied),
            var unhandled
                => failure(unhandled.FailAndLogUnhandledError(EosInterface.Login.LoginError.UnhandledFailureCondition))
        };
    }

    private static void OnEgsAuthStatusChanged(ref Epic.OnlineServices.Auth.LoginStatusChangedCallbackInfo info)
    {
        var eaidOption = EpicAccountId.Parse(info.LocalUserId.ToString());
        if (!eaidOption.TryUnwrap(out var eaid)) { return; }

        if (info.CurrentStatus == Epic.OnlineServices.LoginStatus.NotLoggedIn)
        {
            TaskPool.Add(
                "UnlogPuidLinkedToEaid",
                IdQueriesPrivate.GetPuidForExternalId(eaid),
                t =>
                {
                    if (!t.TryGetResult(out Result<EosInterface.ProductUserId, Epic.OnlineServices.Result>? result)) { return; }
                    if (!result.TryUnwrapSuccess(out var puid)) { return; }

                    MarkAsInaccessible(puid);
                });
        }
    }

    public static void OnConnectExpiration(ref Epic.OnlineServices.Connect.AuthExpirationCallbackInfo info)
    {
        var puid = new EosInterface.ProductUserId(info.LocalUserId.ToString());
        DebugConsoleCore.Log($"OnAuthExpirationNotification {puid}");
        if (!PuidToPrimaryExternalId.TryGetValue(puid, out var externalId)) { return; }

        switch (externalId)
        {
            case SteamId:
            {
                static async Task RelogSteam()
                {
                    var steamCredentialsResult = await GenCredentialsSteam();
                    if (!steamCredentialsResult.TryUnwrapSuccess(out var loginParams)) { return; }
                    await Relog(loginParams);
                }

                TaskPool.Add(
                    "EosReLoginSteam",
                    RelogSteam(),
                    TaskPool.IgnoredCallback);
                break;
            }
            case EpicAccountId epicAccountId:
            {
                if (CopyEpicIdToken(epicAccountId).TryUnwrap(out var token))
                {
                    var epicLoginCredentials = new Epic.OnlineServices.Connect.Credentials
                    {
                        Token = token.JsonWebToken,
                        Type = Epic.OnlineServices.ExternalCredentialType.EpicIdToken
                    };
                    var reLogParams = new LoginParams(Credentials: epicLoginCredentials, ExternalAccountId: externalId);
                    TaskPool.Add("OnAuthExpirationNotification", Relog(reLogParams), onCompletion: TaskPool.IgnoredCallback);
                }

                break;
            }
        }

        static async Task Relog(LoginParams loginParams)
        {
            var loginOptions = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = loginParams.Credentials,
                UserLoginInfo = null
            };

            var connectLoginWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.LoginCallbackInfo>();
            CorePrivate.ConnectInterface?.Login(options: ref loginOptions, clientData: null, completionDelegate: connectLoginWaiter.OnCompletion);
            var resultOption = await connectLoginWaiter.Task;
            if (resultOption.TryUnwrap(out var result))
            {
                string s = $"EOS relog result: {result.ResultCode}";
                if (result.LocalUserId != null)
                {
                    s += " : " + result.LocalUserId;
                }

                if (result.ContinuanceToken != null)
                {
                    s += " ; " + result.ContinuanceToken;
                }
                DebugConsoleCore.Log(s);
            }
            else
            {
                DebugConsoleCore.Log("EOS relog timed out");
            }
        }
    }

    private static void OnConnectStatusChanged(ref Epic.OnlineServices.Connect.LoginStatusChangedCallbackInfo info)
    {
        var puid = new EosInterface.ProductUserId(info.LocalUserId.ToString());
        DebugConsoleCore.Log($"OnLoginStatusChangedNotification {puid} {info.CurrentStatus}");
        if (info.CurrentStatus == Epic.OnlineServices.LoginStatus.NotLoggedIn)
        {
            PuidToPrimaryExternalId.TryRemove(puid, out _);
        }
    }

    public static async Task<Result<EpicAccountId, EosInterface.Login.LinkExternalAccountToEpicAccountError>> LinkExternalAccountToEpicAccount(EosInterface.EgsAuthContinuanceToken continuanceToken)
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return Result.Failure(EosInterface.Login.LinkExternalAccountToEpicAccountError.EosNotInitialized); }

        var linkOptions = new Epic.OnlineServices.Auth.LinkAccountOptions
        {
            LinkAccountFlags = Epic.OnlineServices.Auth.LinkAccountFlags.NoFlags,
            ContinuanceToken = new Epic.OnlineServices.ContinuanceToken(continuanceToken.Spend()),
            LocalUserId = null
        };

        var callbackWaiter = new CallbackWaiter<Epic.OnlineServices.Auth.LinkAccountCallbackInfo>(timeout: TimeSpan.FromMinutes(5));
        egsAuthInterface.LinkAccount(options: ref linkOptions, clientData: null, completionDelegate: callbackWaiter.OnCompletion);
        var resultOption = await callbackWaiter.Task;

        if (!resultOption.TryUnwrap(out var result))
        {
            return Result.Failure(EosInterface.Login.LinkExternalAccountToEpicAccountError.TimedOut);
        }

        if (result.ResultCode == Epic.OnlineServices.Result.Success)
        {
            if (!EpicAccountId.Parse(result.SelectedAccountId.ToString()).TryUnwrap(out var epicAccountId))
            {
                return Result.Failure(EosInterface.Login.LinkExternalAccountToEpicAccountError.FailedToParseEgsAccountId);
            }
            return Result.Success(epicAccountId);
        }
        return Result.Failure(EosInterface.Login.LinkExternalAccountToEpicAccountError.UnhandledErrorCondition);
    }
    
    public static async Task<Result<Unit, EosInterface.Login.LogoutEpicAccountError>> LogoutEpicAccount(EpicAccountId egsId)
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return Result.Failure(EosInterface.Login.LogoutEpicAccountError.EosNotInitialized); }

        var logoutOptions = new Epic.OnlineServices.Auth.LogoutOptions
        {
            LocalUserId = Epic.OnlineServices.EpicAccountId.FromString(egsId.EosStringRepresentation)
        };

        var callbackWaiter = new CallbackWaiter<Epic.OnlineServices.Auth.LogoutCallbackInfo>();
        egsAuthInterface.Logout(options: ref logoutOptions, clientData: null, completionDelegate: callbackWaiter.OnCompletion);
        var logoutResultOption = await callbackWaiter.Task;
        if (!logoutResultOption.TryUnwrap(out var logoutResult))
        {
            return Result.Failure(EosInterface.Login.LogoutEpicAccountError.TimedOut);
        }
        if (logoutResult.ResultCode == Epic.OnlineServices.Result.Success) { return Result.Success(Unit.Value); }

        return Result.Failure(logoutResult.ResultCode switch
        {
            _
                => EosInterface.Login.LogoutEpicAccountError.UnhandledErrorCondition
        });
    }

    public static void MarkAsInaccessible(EosInterface.ProductUserId puid)
    {
        PuidToPrimaryExternalId.TryRemove(puid, out _);
    }

    private static Option<Epic.OnlineServices.Auth.IdToken> CopyEpicIdToken(EpicAccountId epicAccountId)
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return Option.None; }

        var copyIdTokenOptions = new Epic.OnlineServices.Auth.CopyIdTokenOptions
        {
            AccountId = Epic.OnlineServices.EpicAccountId.FromString(epicAccountId.EosStringRepresentation)
        };
        var result = egsAuthInterface.CopyIdToken(ref copyIdTokenOptions, out var tokenNullable);

        if (result is Epic.OnlineServices.Result.Success && tokenNullable is { } token)
        {
            return Option.Some(token);
        }

        return Option.None;
    }

    public static async Task<Result<EosInterface.ProductUserId, EosInterface.Login.CreateProductAccountError>> CreateProductAccount(EosInterface.EosConnectContinuanceToken eosContinuanceToken)
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return Result.Failure(EosInterface.Login.CreateProductAccountError.EosNotInitialized); }
        if (eosContinuanceToken is not { IsValid: true }) { return Result.Failure(EosInterface.Login.CreateProductAccountError.InvalidContinuanceToken); }

        var internalContinuanceToken = new Epic.OnlineServices.ContinuanceToken(eosContinuanceToken.Spend());
        var options = new Epic.OnlineServices.Connect.CreateUserOptions
        {
            ContinuanceToken = internalContinuanceToken
        };

        var createUserWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.CreateUserCallbackInfo>();
        connectInterface.CreateUser(options: ref options, clientData: null, completionDelegate: createUserWaiter.OnCompletion);
        var callbackResultOption = await createUserWaiter.Task;
        if (!callbackResultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.Login.CreateProductAccountError.Timeout);
        }

        if (callbackResult.ResultCode == Epic.OnlineServices.Result.Success)
        {
            var retVal = new EosInterface.ProductUserId(callbackResult.LocalUserId.ToString());
            PuidToPrimaryExternalId[retVal] = eosContinuanceToken.ExternalAccountId;
            return Result.Success(retVal);
        }

        return Result.Failure(EosInterface.Login.CreateProductAccountError.UnhandledErrorCondition);
    }

    public static async Task<Result<Unit, EosInterface.Login.LinkExternalAccountError>> LinkExternalAccount(EosInterface.ProductUserId puid, EosInterface.EosConnectContinuanceToken eosContinuanceToken)
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return Result.Failure(EosInterface.Login.LinkExternalAccountError.EosNotInitialized); }
        if (eosContinuanceToken is not { IsValid: true }) { return Result.Failure(EosInterface.Login.LinkExternalAccountError.InvalidContinuanceToken); }

        var internalContinuanceToken = new Epic.OnlineServices.ContinuanceToken(eosContinuanceToken.Spend());
        var internalPuid = Epic.OnlineServices.ProductUserId.FromString(puid.Value);
        var options = new Epic.OnlineServices.Connect.LinkAccountOptions
        {
            LocalUserId = internalPuid,
            ContinuanceToken = internalContinuanceToken
        };

        var linkAccountAwaiter = new CallbackWaiter<Epic.OnlineServices.Connect.LinkAccountCallbackInfo>();
        connectInterface.LinkAccount(options: ref options, clientData: null, completionDelegate: linkAccountAwaiter.OnCompletion);
        var callbackResultOption = await linkAccountAwaiter.Task;
        if (!callbackResultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.Login.LinkExternalAccountError.Timeout);
        }

        if (callbackResult.ResultCode == Epic.OnlineServices.Result.Success)
        {
            return Result.Success(Unit.Value);
        }

        return Result.Failure(callbackResult.ResultCode switch
        {
            Epic.OnlineServices.Result.ConnectLinkAccountFailed
                => EosInterface.Login.LinkExternalAccountError.CannotLink,
            _
                => EosInterface.Login.LinkExternalAccountError.UnhandledErrorCondition
        });
    }

    public static async Task<Result<Unit, EosInterface.Login.UnlinkExternalAccountError>> UnlinkExternalAccount(EosInterface.ProductUserId puid)
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return Result.Failure(EosInterface.Login.UnlinkExternalAccountError.EosNotInitialized); }

        var internalPuid = Epic.OnlineServices.ProductUserId.FromString(puid.Value);
        var options = new Epic.OnlineServices.Connect.UnlinkAccountOptions
        {
            LocalUserId = internalPuid
        };

        var unlinkAccountAwaiter = new CallbackWaiter<Epic.OnlineServices.Connect.UnlinkAccountCallbackInfo>();
        connectInterface.UnlinkAccount(options: ref options, clientData: null, completionDelegate: unlinkAccountAwaiter.OnCompletion);
        var callbackResultOption = await unlinkAccountAwaiter.Task;
        if (!callbackResultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.Login.UnlinkExternalAccountError.Timeout);
        }

        if (callbackResult.ResultCode == Epic.OnlineServices.Result.Success)
        {
            PuidToPrimaryExternalId.TryRemove(puid, out _);
            return Result.Success(Unit.Value);
        }

        return Result.Failure(callbackResult.ResultCode switch
        {
            Epic.OnlineServices.Result.InvalidUser
                => EosInterface.Login.UnlinkExternalAccountError.InvalidUser,
            _
                => EosInterface.Login.UnlinkExternalAccountError.UnhandledErrorCondition
        });
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<EosInterface.ProductUserId, EosInterface.Login.CreateProductAccountError>> CreateProductAccount(EosInterface.EosConnectContinuanceToken eosContinuanceToken)
        => TaskScheduler.Schedule(() => LoginPrivate.CreateProductAccount(eosContinuanceToken));

    public override Task<Result<Unit, EosInterface.Login.LinkExternalAccountError>> LinkExternalAccount(EosInterface.ProductUserId puid, EosInterface.EosConnectContinuanceToken eosContinuanceToken)
        => TaskScheduler.Schedule(() => LoginPrivate.LinkExternalAccount(puid, eosContinuanceToken));

    public override Task<Result<Unit, EosInterface.Login.UnlinkExternalAccountError>> UnlinkExternalAccount(EosInterface.ProductUserId puid)
        => TaskScheduler.Schedule(() => LoginPrivate.UnlinkExternalAccount(puid));

    public override Task<Result<OneOf<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken, EosInterface.EgsAuthContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicWithLinkedSteamAccount(EosInterface.Login.LoginEpicFlags flags)
        => TaskScheduler.Schedule(() => LoginPrivate.LoginEpicWithLinkedSteamAccount(flags));

    public override Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicExchangeCode(string exchangeCode)
        => TaskScheduler.Schedule(() => LoginPrivate.LoginEpicExchangeCode(exchangeCode));

    public override Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginEpicIdToken(EosInterface.EgsIdToken token)
        => TaskScheduler.Schedule(() => LoginPrivate.LoginEpicIdToken(token));

    public override Task<Result<Either<EosInterface.ProductUserId, EosInterface.EosConnectContinuanceToken>, EosInterface.Login.LoginError>> LoginSteam()
        => TaskScheduler.Schedule(LoginPrivate.LoginSteam);

    public override Task<Result<EpicAccountId, EosInterface.Login.LinkExternalAccountToEpicAccountError>> LinkExternalAccountToEpicAccount(EosInterface.EgsAuthContinuanceToken continuanceToken)
        => TaskScheduler.Schedule(() => LoginPrivate.LinkExternalAccountToEpicAccount(continuanceToken));

    public override Task<Result<Unit, EosInterface.Login.LogoutEpicAccountError>> LogoutEpicAccount(EpicAccountId egsId)
        => LoginPrivate.LogoutEpicAccount(egsId);

    public override void MarkAsInaccessible(EosInterface.ProductUserId puid)
        => LoginPrivate.MarkAsInaccessible(puid);

    public override void TestEosSessionTimeoutRecovery(EosInterface.ProductUserId puid)
    {
        var info = new Epic.OnlineServices.Connect.AuthExpirationCallbackInfo
        {
            ClientData = null,
            LocalUserId = Epic.OnlineServices.ProductUserId.FromString(puid.Value)
        };
        LoginPrivate.OnConnectExpiration(ref info);
    }
}
