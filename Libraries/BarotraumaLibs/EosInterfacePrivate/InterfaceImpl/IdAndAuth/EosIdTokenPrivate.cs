#nullable enable
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma;

namespace EosInterfacePrivate;

static class EosIdTokenPrivate
{
    public static Result<EosInterface.EosIdToken, EosInterface.GetEosSelfIdTokenError> GetEosIdTokenForProductUserId(EosInterface.ProductUserId puid)
    {
        var (success, failure) = Result<EosInterface.EosIdToken, EosInterface.GetEosSelfIdTokenError>.GetFactoryMethods();
        
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return failure(EosInterface.GetEosSelfIdTokenError.EosNotInitialized); }

        var copyIdTokenOptions = new Epic.OnlineServices.Connect.CopyIdTokenOptions
        {
            LocalUserId = Epic.OnlineServices.ProductUserId.FromString(puid.Value)
        };
        var copyIdTokenResult = connectInterface.CopyIdToken(ref copyIdTokenOptions, out var idTokenNullable);

        if (copyIdTokenResult is Epic.OnlineServices.Result.NotFound) { return failure(EosInterface.GetEosSelfIdTokenError.InvalidToken); }
        if (copyIdTokenResult != Epic.OnlineServices.Result.Success) { return failure(EosInterface.GetEosSelfIdTokenError.UnhandledErrorCondition); }
        if (idTokenNullable is not { } idToken) { return failure(EosInterface.GetEosSelfIdTokenError.UnhandledErrorCondition); }

        if (!JsonWebToken.Parse(idToken.JsonWebToken).TryUnwrap(out var jsonWebToken)) { return failure(EosInterface.GetEosSelfIdTokenError.CouldNotParseJwt); }

        return success(new EosInterface.EosIdToken(new EosInterface.ProductUserId(idToken.ProductUserId.ToString()), jsonWebToken));
    }

    public static async Task<Result<AccountId, EosInterface.VerifyEosIdTokenError>> Verify(EosInterface.EosIdToken token)
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return Result.Failure(EosInterface.VerifyEosIdTokenError.EosNotInitialized); }

        var verifyIdTokenOptions = new Epic.OnlineServices.Connect.VerifyIdTokenOptions
        {
            IdToken = new Epic.OnlineServices.Connect.IdToken
            {
                ProductUserId = Epic.OnlineServices.ProductUserId.FromString(token.ProductUserId.Value),
                JsonWebToken = token.JsonWebToken.ToString()
            }
        };
        var verifyIdTokenWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.VerifyIdTokenCallbackInfo>();
        connectInterface.VerifyIdToken(options: ref verifyIdTokenOptions, clientData: null, completionDelegate: verifyIdTokenWaiter.OnCompletion);
        var result = await verifyIdTokenWaiter.Task;
        if (!result.TryUnwrap(out var callbackInfo)) { return Result.Failure(EosInterface.VerifyEosIdTokenError.TimedOut); }

        if (callbackInfo.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.VerifyEosIdTokenError.UnhandledErrorCondition);
        }

        if (callbackInfo.ProductId != CorePrivate.PlatformInterfaceOptions.ProductId)
        {
            return Result.Failure(EosInterface.VerifyEosIdTokenError.ProductIdDidNotMatch);
        }

        var resultAccountId = IdQueriesPrivate.EosStringToAccountId(callbackInfo.AccountId, callbackInfo.AccountIdType);

        return resultAccountId.TryUnwrap(out var resultId)
            ? Result.Success(resultId)
            : Result.Failure(EosInterface.VerifyEosIdTokenError.CouldNotParseExternalAccountId);
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<AccountId, EosInterface.VerifyEosIdTokenError>> VerifyEosIdToken(EosInterface.EosIdToken token)
        => TaskScheduler.Schedule(() => EosIdTokenPrivate.Verify(token));

    public override Result<EosInterface.EosIdToken, EosInterface.GetEosSelfIdTokenError> GetEosIdTokenForProductUserId(EosInterface.ProductUserId puid)
        => EosIdTokenPrivate.GetEosIdTokenForProductUserId(puid);
}
