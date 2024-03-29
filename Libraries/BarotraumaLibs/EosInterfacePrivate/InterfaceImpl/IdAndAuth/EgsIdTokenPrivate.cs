#nullable enable
using System.Text.Json;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma;

namespace EosInterfacePrivate;

public sealed class EgsIdTokenPrivate : EosInterface.EgsIdToken
{
    public override EpicAccountId AccountId { get; }

    internal static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    internal readonly record struct TokenStruct(
        string AccountId,
        string JsonWebToken);
    
    internal readonly Epic.OnlineServices.Auth.IdToken InternalToken;
    internal EgsIdTokenPrivate(EpicAccountId accountId, Epic.OnlineServices.Auth.IdToken internalToken)
    {
        AccountId = accountId;
        InternalToken = internalToken;
    }

    public new static Option<EgsIdTokenPrivate> Parse(string str)
    {
        try
        {
            if (JsonSerializer.Deserialize(
                    str,
                    returnType: typeof(TokenStruct),
                    options: JsonSerializerOptions)
                is not TokenStruct tokenStruct)
            {
                return Option.None;
            }
            
            if (!EpicAccountId.Parse(tokenStruct.AccountId).TryUnwrap(out var accountId)) { return Option.None; }

            var internalToken = new Epic.OnlineServices.Auth.IdToken
            {
                AccountId = Epic.OnlineServices.EpicAccountId.FromString(tokenStruct.AccountId),
                JsonWebToken = tokenStruct.JsonWebToken
            };

            return Option.Some(new EgsIdTokenPrivate(accountId, internalToken));
        }
        catch
        {
            return Option.None;
        }
    }
    
    public override string ToString()
    {
        var tokenStruct = new TokenStruct(
            AccountId: InternalToken.AccountId.ToString(),
            JsonWebToken: InternalToken.JsonWebToken);
        return JsonSerializer.Serialize(tokenStruct, options: JsonSerializerOptions);
    }

    public static Result<EosInterface.EgsIdToken, EosInterface.GetEgsSelfIdTokenError> GetEgsIdTokenForEpicAccountId(EpicAccountId accountId)
    {
        var (success, failure) = Result<EosInterface.EgsIdToken, EosInterface.GetEgsSelfIdTokenError>.GetFactoryMethods();
        
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return failure(EosInterface.GetEgsSelfIdTokenError.EosNotInitialized); }

        var copyIdTokenOptions = new Epic.OnlineServices.Auth.CopyIdTokenOptions
        {
            AccountId = Epic.OnlineServices.EpicAccountId.FromString(accountId.EosStringRepresentation)
        };
        var copyIdTokenResult = egsAuthInterface.CopyIdToken(ref copyIdTokenOptions, out var idTokenNullable);

        if (copyIdTokenResult is Epic.OnlineServices.Result.NotFound) { return failure(EosInterface.GetEgsSelfIdTokenError.InvalidToken); }
        if (copyIdTokenResult != Epic.OnlineServices.Result.Success) { return failure(EosInterface.GetEgsSelfIdTokenError.UnhandledErrorCondition); }
        if (idTokenNullable is not { } idToken) { return failure(EosInterface.GetEgsSelfIdTokenError.UnhandledErrorCondition); }

        return success(new EgsIdTokenPrivate(accountId, idToken));
    }

    public override async Task<EosInterface.VerifyEgsIdTokenResult> Verify(AccountId accountId)
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return EosInterface.VerifyEgsIdTokenResult.Failed; }

        var verifyIdTokenOptions = new Epic.OnlineServices.Auth.VerifyIdTokenOptions
        {
            IdToken = InternalToken
        };
        var verifyIdTokenWaiter = new CallbackWaiter<Epic.OnlineServices.Auth.VerifyIdTokenCallbackInfo>();
        egsAuthInterface.VerifyIdToken(options: ref verifyIdTokenOptions, clientData: null, completionDelegate: verifyIdTokenWaiter.OnCompletion);
        var result = await verifyIdTokenWaiter.Task;
        if (!result.TryUnwrap(out var callbackInfo)) { return EosInterface.VerifyEgsIdTokenResult.Failed; }

        if (callbackInfo.ResultCode != Epic.OnlineServices.Result.Success
            || callbackInfo.ProductId != CorePrivate.PlatformInterfaceOptions.ProductId)
        {
            return EosInterface.VerifyEgsIdTokenResult.Failed;
        }

        var resultAccountId = IdQueriesPrivate.EosStringToAccountId(callbackInfo.ExternalAccountId, callbackInfo.ExternalAccountIdType);

        return resultAccountId.TryUnwrap(out var resultId) && resultId == accountId
            ? EosInterface.VerifyEgsIdTokenResult.Verified
            : EosInterface.VerifyEgsIdTokenResult.Failed;
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Option<EosInterface.EgsIdToken> ParseEgsIdToken(string str)
        => EgsIdTokenPrivate.Parse(str).Select(t => (EosInterface.EgsIdToken)t);

    public override Result<EosInterface.EgsIdToken, EosInterface.GetEgsSelfIdTokenError> GetEgsIdTokenForEpicAccountId(EpicAccountId accountId)
        => EgsIdTokenPrivate.GetEgsIdTokenForEpicAccountId(accountId);
}
