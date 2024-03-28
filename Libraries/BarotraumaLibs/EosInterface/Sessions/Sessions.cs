using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Sessions
    {
        public const string DefaultBucketName = "BBucket";
        public const int MinBucketIndex = 0;
        public const int MaxBucketIndex = 9;

        public sealed record OwnedSession(
            string BucketId,
            Identifier InternalId,
            Identifier GlobalId,
            Dictionary<Identifier, string> Attributes) : IDisposable
        {
            public Option<string> HostAddress = Option.None;

            public ImmutableDictionary<Identifier, string> SyncedAttributes =
                ImmutableDictionary<Identifier, string>.Empty;

            public async Task<Result<Unit, AttributeUpdateError>> UpdateAttributes()
                => Core.LoadedImplementation is { } implementation
                    ? await implementation.UpdateOwnedSessionAttributes(this)
                    : Result.Failure(AttributeUpdateError.EosNotInitialized);

            public async Task<Result<Unit, CloseError>> Close()
                => Core.LoadedImplementation is { } implementation
                    ? await implementation.CloseOwnedSession(this)
                    : Result.Failure(CloseError.EosNotInitialized);

            public void Dispose()
            {
                if (!Core.IsInitialized)
                {
                    return;
                }

                var _ = Close();
            }
        }

        public readonly record struct RemoteSession(
            string SessionId,
            string HostAddress,
            int CurrentPlayers,
            int MaxPlayers,
            ImmutableDictionary<Identifier, string> Attributes,
            string BucketId)
        {
            public readonly record struct Query(
                int BucketIndex,
                ProductUserId LocalUserId,
                uint MaxResults,
                ImmutableDictionary<Identifier, string> Attributes)
            {
                public enum Error
                {
                    EosNotInitialized,

                    ExceededMaxAllowedResults,

                    InvalidParameters,
                    TimedOut,
                    NotFound,

                    UnhandledErrorCondition
                }

                public async Task<Result<ImmutableArray<RemoteSession>, Error>> Run()
                    => Core.LoadedImplementation is { } loadedImplementation
                        ? await loadedImplementation.RunRemoteSessionQuery(this)
                        : Result.Failure(Error.EosNotInitialized);
            }
        }

        public enum CreateError
        {
            EosNotInitialized,
            TimedOut,

            SessionAlreadyExists,

            InvalidParametersForAddAttribute,
            IncompatibleVersionForAddAttribute,
            UnhandledErrorConditionForAddAttribute,

            InvalidUser,

            UnhandledErrorCondition
        }

        public enum AttributeUpdateError
        {
            EosNotInitialized,
            TimedOut,

            FailedToCreateSessionModificationHandle,

            InvalidParametersForRemoveAttribute,
            IncompatibleVersionForRemoveAttribute,
            UnhandledErrorConditionForRemoveAttribute,

            InvalidParametersForAddAttribute,
            IncompatibleVersionForAddAttribute,
            UnhandledErrorConditionForAddAttribute,

            InvalidParametersForSessionUpdate,
            SessionsOutOfSync,
            SessionNotFound,
            NoConnection,

            UnhandledErrorCondition
        }

        public enum CloseError
        {
            EosNotInitialized,
            TimedOut,

            InvalidParameters,
            AlreadyPending,
            NotFound,
            UnhandledErrorCondition
        }

        public enum RegisterError
        {
            EosNotInitialized,
            TimedOut,
            UnhandledErrorCondition
        }

        public enum UnregisterError
        {
            EosNotInitialized,
            TimedOut,
            UnhandledErrorCondition
        }

        public static async Task<Result<OwnedSession, CreateError>> CreateSession(Option<ProductUserId> puidOption,
            Identifier internalId, int maxPlayers)
            => Core.LoadedImplementation.IsInitialized()
                ? await Core.LoadedImplementation.CreateSession(puidOption, internalId, maxPlayers)
                : Result.Failure(CreateError.EosNotInitialized);
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Result<Sessions.OwnedSession, Sessions.CreateError>> CreateSession(
            Option<ProductUserId> selfUserIdOption, Identifier internalId, int maxPlayers);

        public abstract Task<Result<Unit, Sessions.AttributeUpdateError>> UpdateOwnedSessionAttributes(
            Sessions.OwnedSession session);

        public abstract Task<Result<Unit, Sessions.CloseError>> CloseOwnedSession(Sessions.OwnedSession session);
        public abstract Task CloseAllOwnedSessions();

        public abstract Task<Result<ImmutableArray<Sessions.RemoteSession>, Sessions.RemoteSession.Query.Error>>
            RunRemoteSessionQuery(Sessions.RemoteSession.Query query);
    }
}