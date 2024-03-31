#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma;

namespace EosInterfacePrivate;

static class IdQueriesPrivate
{
    public static ImmutableArray<EosInterface.ProductUserId> GetLoggedInPuids()
    {
        if (CorePrivate.ConnectInterface is not { } connectInterface) { return ImmutableArray<EosInterface.ProductUserId>.Empty; }

        int count = connectInterface.GetLoggedInUsersCount();
        var ids = new List<EosInterface.ProductUserId>();
        foreach (int i in Enumerable.Range(0, count))
        {
            if (connectInterface.GetLoggedInUserByIndex(i) is not { } userId) { return ImmutableArray<EosInterface.ProductUserId>.Empty; }
            var newPuid = new EosInterface.ProductUserId(userId.ToString());
            if (!LoginPrivate.PuidToPrimaryExternalId.ContainsKey(newPuid)) { continue; }
            ids.Add(newPuid);
        }

        return ids.ToImmutableArray();
    }

    public static ImmutableArray<EpicAccountId> GetLoggedInEpicIds()
    {
        if (CorePrivate.EgsAuthInterface is not { } egsAuthInterface) { return ImmutableArray<EpicAccountId>.Empty; }

        int count = egsAuthInterface.GetLoggedInAccountsCount();
        var ids = new List<EpicAccountId>();
        foreach (int i in Enumerable.Range(0, count))
        {
            if (egsAuthInterface.GetLoggedInAccountByIndex(i) is not { } userId) { return ImmutableArray<EpicAccountId>.Empty; }
            var newEpicIdOption = EpicAccountId.Parse(userId.ToString());
            if (!newEpicIdOption.TryUnwrap(out var newEpicId)) { return ImmutableArray<EpicAccountId>.Empty; }
            ids.Add(newEpicId);
        }

        return ids.ToImmutableArray();
    }

    public static Task<Result<ImmutableArray<AccountId>, EosInterface.IdQueries.GetSelfExternalIdError>>
        GetSelfExternalAccountIds(
            EosInterface.ProductUserId productUserId)
        => GetExternalAccountIds(productUserId, productUserId);

    internal static async Task<Result<ImmutableArray<AccountId>, EosInterface.IdQueries.GetSelfExternalIdError>>
        GetExternalAccountIds(
            EosInterface.ProductUserId selfPuid,
            EosInterface.ProductUserId puidToGetIdsFor)
    {
        // If logged only into an Epic account, you cannot fetch SteamIDs.
        // See Epic.OnlineServices.Connect.ExternalAccountInfo.AccountId

        var (success, failure) = Result<ImmutableArray<AccountId>, EosInterface.IdQueries.GetSelfExternalIdError>.GetFactoryMethods();

        if (CorePrivate.ConnectInterface is not { } connectInterface)
        {
            return failure(EosInterface.IdQueries.GetSelfExternalIdError.EosNotInitialized);
        }
        if (!LoginPrivate.PuidToPrimaryExternalId.ContainsKey(selfPuid))
        {
            return failure(EosInterface.IdQueries.GetSelfExternalIdError.Inaccessible);
        }

        var selfPuidInternal = Epic.OnlineServices.ProductUserId.FromString(selfPuid.Value);
        var otherPuidInternal = Epic.OnlineServices.ProductUserId.FromString(puidToGetIdsFor.Value);

        var queryProductUserIdMappingsOptions = new Epic.OnlineServices.Connect.QueryProductUserIdMappingsOptions
        {
            LocalUserId = selfPuidInternal,
            ProductUserIds = new[] { otherPuidInternal }
        };

        var queryWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.QueryProductUserIdMappingsCallbackInfo>();
        connectInterface.QueryProductUserIdMappings(options: ref queryProductUserIdMappingsOptions, clientData: null, completionDelegate: queryWaiter.OnCompletion);
        var queryResultOption = await queryWaiter.Task;
        if (!queryResultOption.TryUnwrap(out var queryResult))
        {
            return failure(EosInterface.IdQueries.GetSelfExternalIdError.Timeout);
        }

        if (queryResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return failure(queryResult.ResultCode switch
            {
                Epic.OnlineServices.Result.NotFound => EosInterface.IdQueries.GetSelfExternalIdError.InvalidUser,
                Epic.OnlineServices.Result.InvalidUser => EosInterface.IdQueries.GetSelfExternalIdError.InvalidUser,
                var unhandled => unhandled.FailAndLogUnhandledError(EosInterface.IdQueries.GetSelfExternalIdError.UnhandledErrorCondition)
            });
        }

        var getProductUserExternalAccountCountOptions = new Epic.OnlineServices.Connect.GetProductUserExternalAccountCountOptions
        {
            TargetUserId = otherPuidInternal
        };

        uint count = connectInterface.GetProductUserExternalAccountCount(ref getProductUserExternalAccountCountOptions);
        var accountIds = new AccountId[count];

        foreach (int i in Enumerable.Range(0, (int)count))
        {
            var copyProductUserExternalAccountByIndexOptions = new Epic.OnlineServices.Connect.CopyProductUserExternalAccountByIndexOptions
            {
                TargetUserId = otherPuidInternal,
                ExternalAccountInfoIndex = (uint)i
            };

            connectInterface.CopyProductUserExternalAccountByIndex(
                ref copyProductUserExternalAccountByIndexOptions,
                out var externalAccountInfoNullable);
            if (!externalAccountInfoNullable.TryGetValue(out var externalAccountInfo))
            {
                return failure(EosInterface.IdQueries.GetSelfExternalIdError.InvalidUser);
            }

            var accountIdOption =
                EosStringToAccountId(externalAccountInfo.AccountId, externalAccountInfo.AccountIdType);
            if (!accountIdOption.TryUnwrap(out var accountId))
            {
                return failure(EosInterface.IdQueries.GetSelfExternalIdError.ParseError);
            }

            accountIds[i] = accountId;
        }

        return success(accountIds.ToImmutableArray());
    }

    internal static async Task<Result<EosInterface.ProductUserId, Epic.OnlineServices.Result>> GetPuidForExternalId(AccountId externalId)
    {
        var connectInterface = CorePrivate.ConnectInterface;
        if (connectInterface is null)
        {
            return Result.Failure(Epic.OnlineServices.Result.NotConfigured);
        }

        var externalAccountType = externalId is EpicAccountId
            ? Epic.OnlineServices.ExternalAccountType.Epic
            : Epic.OnlineServices.ExternalAccountType.Steam;
        string externalAccountEosRepresentation = externalId.EosStringRepresentation;

        Result<EosInterface.ProductUserId, Epic.OnlineServices.Result> lastError
            = Result.Failure(Epic.OnlineServices.Result.UnexpectedError);
        foreach (var selfPuid in GetLoggedInPuids()
                     .OrderByDescending(id => LoginPrivate.PuidToPrimaryExternalId[id].GetType() == externalId.GetType()))
        {
            var selfPuidInternal = Epic.OnlineServices.ProductUserId.FromString(selfPuid.Value);

            // See https://dev.epicgames.com/docs/en-US/api-ref/functions/eos-connect-query-external-account-mappings
            // to learn why we need to call this function before we call GetExternalAccountMapping
            var queryExternalAccountMappingsOptions = new Epic.OnlineServices.Connect.QueryExternalAccountMappingsOptions
            {
                LocalUserId = selfPuidInternal,
                AccountIdType = externalAccountType,
                ExternalAccountIds = new Epic.OnlineServices.Utf8String[]
                {
                    externalAccountEosRepresentation
                }
            };

            var queryExternalAccountMappingsWaiter = new CallbackWaiter<Epic.OnlineServices.Connect.QueryExternalAccountMappingsCallbackInfo>();
            connectInterface.QueryExternalAccountMappings(options: ref queryExternalAccountMappingsOptions, clientData: null, completionDelegate: queryExternalAccountMappingsWaiter.OnCompletion);
            var resultOption = await queryExternalAccountMappingsWaiter.Task;
            if (!resultOption.TryUnwrap(out var result))
            {
                lastError = Result.Failure(Epic.OnlineServices.Result.TimedOut);
                continue;
            }

            if (result.ResultCode != Epic.OnlineServices.Result.Success)
            {
                lastError = Result.Failure(result.ResultCode);
                continue;
            }

            var getExternalAccountMappingsOptions = new Epic.OnlineServices.Connect.GetExternalAccountMappingsOptions
            {
                LocalUserId = selfPuidInternal,
                AccountIdType = externalAccountType,
                TargetExternalUserId = externalAccountEosRepresentation
            };
            var otherPuid = connectInterface.GetExternalAccountMapping(ref getExternalAccountMappingsOptions);
            if (otherPuid is null)
            {
                lastError = Result.Failure(Epic.OnlineServices.Result.NotFound);
                continue;
            }
            return Result.Success(new EosInterface.ProductUserId(otherPuid.ToString()));
        }

        return lastError;
    }

    public static Option<AccountId> EosStringToAccountId(
        string stringRepresentation,
        Epic.OnlineServices.ExternalAccountType accountType)
        => accountType switch
        {
            Epic.OnlineServices.ExternalAccountType.Steam => SteamId.Parse(stringRepresentation).Select(id => (AccountId)id),
            Epic.OnlineServices.ExternalAccountType.Epic => EpicAccountId.Parse(stringRepresentation).Select(id => (AccountId)id),
            _ => Option.None
        };
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override ImmutableArray<EosInterface.ProductUserId> GetLoggedInPuids()
        => IdQueriesPrivate.GetLoggedInPuids();
    
    public override ImmutableArray<EpicAccountId> GetLoggedInEpicIds()
        => IdQueriesPrivate.GetLoggedInEpicIds();

    public override Task<Result<ImmutableArray<AccountId>, EosInterface.IdQueries.GetSelfExternalIdError>> GetSelfExternalAccountIds(EosInterface.ProductUserId puid)
        => TaskScheduler.Schedule(() => IdQueriesPrivate.GetSelfExternalAccountIds(puid));
}
