#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma;

namespace EosInterfacePrivate;

static class RemoteSessionsPrivate
{
    /// <summary>
    /// Largest number that can be passed to CreateSessionSearchOptions.MaxSearchResults
    /// before it will immediately result in an InvalidParameters error.
    /// </summary>
    private const uint MaxResultsUpperBound = Epic.OnlineServices.Sessions.SessionsInterface.MaxSearchResults;

    public static async Task<Result<ImmutableArray<EosInterface.Sessions.RemoteSession>, EosInterface.Sessions.RemoteSession.Query.Error>> RunQuery(EosInterface.Sessions.RemoteSession.Query query)
    {
        if (CorePrivate.SessionsInterface is not { } sessionsInterface)
        {
            return Result.Failure(EosInterface.Sessions.RemoteSession.Query.Error.EosNotInitialized);
        }

        using var janitor = Janitor.Start();

        var createSessionSearchOptions = new Epic.OnlineServices.Sessions.CreateSessionSearchOptions
        {
            MaxSearchResults = query.MaxResults
        };
        var createSessionSearchResult = sessionsInterface.CreateSessionSearch(ref createSessionSearchOptions, out var sessionSearchHandle);
        if (createSessionSearchResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(
                createSessionSearchResult switch
                {
                    Epic.OnlineServices.Result.InvalidParameters when query.MaxResults > MaxResultsUpperBound
                        => EosInterface.Sessions.RemoteSession.Query.Error.ExceededMaxAllowedResults,
                    Epic.OnlineServices.Result.InvalidParameters
                        => EosInterface.Sessions.RemoteSession.Query.Error.InvalidParameters,
                    _
                        => createSessionSearchResult.FailAndLogUnhandledError(EosInterface.Sessions.RemoteSession.Query.Error.UnhandledErrorCondition)
                });
        }
        janitor.AddAction(sessionSearchHandle.Release);

        var setParameterOptions = new Epic.OnlineServices.Sessions.SessionSearchSetParameterOptions
        {
            Parameter = new Epic.OnlineServices.Sessions.AttributeData
            {
                Key = Epic.OnlineServices.Sessions.SessionsInterface.SearchBucketId,
                Value = new Epic.OnlineServices.Sessions.AttributeDataValue
                {
                    AsUtf8 = EosInterface.Sessions.DefaultBucketName + query.BucketIndex
                }
            },
            ComparisonOp = Epic.OnlineServices.ComparisonOp.Equal
        };
        sessionSearchHandle.SetParameter(ref setParameterOptions);

        var findOptions = new Epic.OnlineServices.Sessions.SessionSearchFindOptions
        {
            LocalUserId = Epic.OnlineServices.ProductUserId.FromString(query.LocalUserId.Value)
        };

        var findCallbackWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.SessionSearchFindCallbackInfo>();
        sessionSearchHandle.Find(options: ref findOptions, clientData: null, completionDelegate: findCallbackWaiter.OnCompletion);
        var findResultOption = await findCallbackWaiter.Task;
        if (!findResultOption.TryUnwrap(out var findResult))
        {
            return Result.Failure(EosInterface.Sessions.RemoteSession.Query.Error.TimedOut);
        }
        if (findResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(
                findResult.ResultCode switch
                {
                    Epic.OnlineServices.Result.NotFound
                        => EosInterface.Sessions.RemoteSession.Query.Error.NotFound,
                    Epic.OnlineServices.Result.InvalidParameters
                        => EosInterface.Sessions.RemoteSession.Query.Error.InvalidParameters,
                    _
                        => EosInterface.Sessions.RemoteSession.Query.Error.EosNotInitialized
                });
        }

        var boilerplate1 = new Epic.OnlineServices.Sessions.SessionSearchGetSearchResultCountOptions();
        uint resultCount = sessionSearchHandle.GetSearchResultCount(ref boilerplate1);

        var sessions = new List<EosInterface.Sessions.RemoteSession>();
        foreach (int sessionIndex in Enumerable.Range(0, (int)resultCount))
        {
            var attributes = new Dictionary<Identifier, string>();

            var copySessionDetailsOptions = new Epic.OnlineServices.Sessions.SessionSearchCopySearchResultByIndexOptions
            {
                SessionIndex = (uint)sessionIndex
            };
            var detailsCopyResult = sessionSearchHandle.CopySearchResultByIndex(ref copySessionDetailsOptions, out var sessionDetails);
            if (detailsCopyResult != Epic.OnlineServices.Result.Success) { break; }
            janitor.AddAction(sessionDetails.Release);

            var copyInfoOptions = new Epic.OnlineServices.Sessions.SessionDetailsCopyInfoOptions();
            var infoCopyResult = sessionDetails.CopyInfo(ref copyInfoOptions, out var sessionInfo);
            if (infoCopyResult != Epic.OnlineServices.Result.Success) { break; }

            if (sessionInfo is not
                {
                    Settings:
                    {
                        BucketId: { } bucketId,
                        NumPublicConnections: var numPublicConnections
                    },
                    NumOpenPublicConnections: var numOpenPublicConnections,
                    SessionId: { } sessionId,
                    HostAddress: { } hostAddress
                })
            {
                break;
            }

            var boilerplate2 = new Epic.OnlineServices.Sessions.SessionDetailsGetSessionAttributeCountOptions();
            var attributeCount = sessionDetails.GetSessionAttributeCount(ref boilerplate2);

            foreach (var attributeIndex in Enumerable.Range(0, (int)attributeCount))
            {
                var copyAttributeOptions =
                    new Epic.OnlineServices.Sessions.SessionDetailsCopySessionAttributeByIndexOptions
                    {
                        AttrIndex = (uint)attributeIndex
                    };

                var attributeCopyResult = sessionDetails.CopySessionAttributeByIndex(ref copyAttributeOptions, out var attributeNullable);
                if (attributeCopyResult != Epic.OnlineServices.Result.Success) { break; }
                if (attributeNullable?.Data is not { } attributeData
                    || attributeData.Value.ValueType != Epic.OnlineServices.AttributeType.String)
                {
                    break;
                }
                attributes.Add(attributeData.Key.ToIdentifier(), attributeData.Value.AsUtf8);
            }
            sessions.Add(new EosInterface.Sessions.RemoteSession(
                SessionId: sessionId,
                HostAddress: hostAddress,
                CurrentPlayers: (int)(numPublicConnections - numOpenPublicConnections),
                MaxPlayers: (int)numPublicConnections,
                Attributes: attributes.ToImmutableDictionary(),
                BucketId: bucketId));
        }

        return Result.Success(sessions.ToImmutableArray());
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<ImmutableArray<EosInterface.Sessions.RemoteSession>, EosInterface.Sessions.RemoteSession.Query.Error>> RunRemoteSessionQuery(EosInterface.Sessions.RemoteSession.Query query)
        => TaskScheduler.Schedule(() => RemoteSessionsPrivate.RunQuery(query));
}