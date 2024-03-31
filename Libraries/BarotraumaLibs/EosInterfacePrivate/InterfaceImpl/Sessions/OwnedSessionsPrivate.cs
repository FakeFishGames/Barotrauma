#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma;

namespace EosInterfacePrivate;

static class OwnedSessionsPrivate
{
    private static readonly Random rng = new Random();
    private static readonly ConcurrentDictionary<Identifier, EosInterface.Sessions.OwnedSession> liveOwnedSessions = new ConcurrentDictionary<Identifier, EosInterface.Sessions.OwnedSession>();

    private static Epic.OnlineServices.Utf8String IdentifierToAttributeKey(Identifier id)
    {
        // Attribute keys are always uppercase in the EOS developer page,
        // so to minimize surprises let's match that here
        return id.Value.ToUpperInvariant();
    }

    public static async Task<Result<EosInterface.Sessions.OwnedSession, EosInterface.Sessions.CreateError>> Create(Option<EosInterface.ProductUserId> selfUserIdOption, Identifier internalId, int maxPlayers)
    {
        var (success, failure) = Result<EosInterface.Sessions.OwnedSession, EosInterface.Sessions.CreateError>.GetFactoryMethods();

        if (CorePrivate.SessionsInterface is not { } sessionsInterface) { return failure(EosInterface.Sessions.CreateError.EosNotInitialized); }

        if (liveOwnedSessions.ContainsKey(internalId)) { return failure(EosInterface.Sessions.CreateError.SessionAlreadyExists); }

        using var janitor = Janitor.Start();

        var bucketIndex = rng.Next(EosInterface.Sessions.MinBucketIndex, EosInterface.Sessions.MaxBucketIndex + 1);
        string bucketName = EosInterface.Sessions.DefaultBucketName + bucketIndex;
        var createSessionModificationOptions = new Epic.OnlineServices.Sessions.CreateSessionModificationOptions
        {
            SessionName = internalId.Value.ToUpperInvariant(),
            BucketId = bucketName,
            MaxPlayers = (uint)maxPlayers,
            LocalUserId = selfUserIdOption.TryUnwrap(out var selfUserId)
                ? Epic.OnlineServices.ProductUserId.FromString(selfUserId.Value)
                : null,
            PresenceEnabled = false,
            SessionId = null,
            SanctionsEnabled = false
        };
        var sessionCreateResult = sessionsInterface.CreateSessionModification(ref createSessionModificationOptions, out var sessionModificationHandle);
        if (sessionCreateResult != Epic.OnlineServices.Result.Success)
        {
            return failure(sessionCreateResult switch
            {
                Epic.OnlineServices.Result.InvalidUser => EosInterface.Sessions.CreateError.InvalidUser,
                Epic.OnlineServices.Result.SessionsSessionAlreadyExists => EosInterface.Sessions.CreateError.SessionAlreadyExists,
                _ => EosInterface.Sessions.CreateError.UnhandledErrorCondition
            });
        }
        janitor.AddAction(sessionModificationHandle.Release);

        var updateSessionOptions = new Epic.OnlineServices.Sessions.UpdateSessionOptions
        {
            SessionModificationHandle = sessionModificationHandle
        };

        var updateSessionWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.UpdateSessionCallbackInfo>();
        sessionsInterface.UpdateSession(options: ref updateSessionOptions, clientData: null, completionDelegate: updateSessionWaiter.OnCompletion);
        var updateSessionResultOption = await updateSessionWaiter.Task;

        if (!updateSessionResultOption.TryUnwrap(out var updateSessionResult)) { return failure(EosInterface.Sessions.CreateError.TimedOut); }

        if (updateSessionResult.ResultCode == Epic.OnlineServices.Result.Success)
        {
            var newSession = new EosInterface.Sessions.OwnedSession(
                BucketId: bucketName,
                InternalId: updateSessionResult.SessionName.ToIdentifier(),
                GlobalId: updateSessionResult.SessionId.ToIdentifier(),
                Attributes: new Dictionary<Identifier, string>());
            liveOwnedSessions.TryAdd(internalId, newSession);
            return success(newSession);
        }
        return failure(updateSessionResult.ResultCode.FailAndLogUnhandledError(EosInterface.Sessions.CreateError.UnhandledErrorCondition));
    }

    public static async Task<Result<Unit, EosInterface.Sessions.AttributeUpdateError>> UpdateOwnedSessionAttributes(EosInterface.Sessions.OwnedSession session)
    {
        if (CorePrivate.SessionsInterface is not { } sessionsInterface) { return Result.Failure(EosInterface.Sessions.AttributeUpdateError.EosNotInitialized); }

        using var janitor = Janitor.Start();

        var updateSessionModificationOptions = new Epic.OnlineServices.Sessions.UpdateSessionModificationOptions
        {
            SessionName = session.InternalId.Value.ToUpperInvariant()
        };
        var sessionCreateResult = sessionsInterface.UpdateSessionModification(ref updateSessionModificationOptions, out var sessionModificationHandle);
        if (sessionCreateResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Sessions.AttributeUpdateError.FailedToCreateSessionModificationHandle);
        }
        janitor.AddAction(() => sessionModificationHandle.Release());

        var keysToRemove = session.SyncedAttributes
            .Except(session.Attributes)
            .Select(kvp => kvp.Key)
            .ToArray();

        var attributesToAdd = session.Attributes
            .Except(session.SyncedAttributes)
            .ToArray();

        var setBucketIdOptions = new Epic.OnlineServices.Sessions.SessionModificationSetBucketIdOptions
        {
            BucketId = session.BucketId
        };
        sessionModificationHandle.SetBucketId(ref setBucketIdOptions);

        if (session.HostAddress.TryUnwrap(out var hostAddress))
        {
            var setHostAddressOptions = new Epic.OnlineServices.Sessions.SessionModificationSetHostAddressOptions
            {
                HostAddress = hostAddress
            };
            sessionModificationHandle.SetHostAddress(ref setHostAddressOptions);
        }

        foreach (Identifier key in keysToRemove)
        {
            var removeAttributeOptions = new Epic.OnlineServices.Sessions.SessionModificationRemoveAttributeOptions
            {
                Key = IdentifierToAttributeKey(key)
            };
            var removeResult = sessionModificationHandle.RemoveAttribute(ref removeAttributeOptions);
            if (removeResult != Epic.OnlineServices.Result.Success)
            {
                return Result.Failure(
                    removeResult switch
                    {
                        Epic.OnlineServices.Result.InvalidParameters
                            => EosInterface.Sessions.AttributeUpdateError.InvalidParametersForRemoveAttribute,
                        Epic.OnlineServices.Result.IncompatibleVersion
                            => EosInterface.Sessions.AttributeUpdateError.IncompatibleVersionForRemoveAttribute,
                        _
                            => EosInterface.Sessions.AttributeUpdateError.UnhandledErrorConditionForRemoveAttribute
                    });
            }
        }

        foreach (var kvp in attributesToAdd)
        {
            // EOS doesn't like empty values so let's skip those
            if (kvp.Value.IsNullOrEmpty()) { continue; }

            var addAttributeOptions = new Epic.OnlineServices.Sessions.SessionModificationAddAttributeOptions
            {
                SessionAttribute = new Epic.OnlineServices.Sessions.AttributeData
                {
                    Key = IdentifierToAttributeKey(kvp.Key),
                    Value = kvp.Value
                },
                AdvertisementType = Epic.OnlineServices.Sessions.SessionAttributeAdvertisementType.Advertise
            };
            var addResult = sessionModificationHandle.AddAttribute(ref addAttributeOptions);
            if (addResult != Epic.OnlineServices.Result.Success)
            {
                return Result.Failure(
                    addResult switch
                    {
                        Epic.OnlineServices.Result.InvalidParameters
                            => EosInterface.Sessions.AttributeUpdateError.InvalidParametersForAddAttribute,
                        Epic.OnlineServices.Result.IncompatibleVersion
                            => EosInterface.Sessions.AttributeUpdateError.IncompatibleVersionForAddAttribute,
                        _
                            => EosInterface.Sessions.AttributeUpdateError.UnhandledErrorConditionForAddAttribute
                    });
            }
        }

        var updateSessionOptions = new Epic.OnlineServices.Sessions.UpdateSessionOptions
        {
            SessionModificationHandle = sessionModificationHandle
        };

        var updateSessionWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.UpdateSessionCallbackInfo>();
        sessionsInterface.UpdateSession(options: ref updateSessionOptions, clientData: null, completionDelegate: updateSessionWaiter.OnCompletion);
        var updateSessionResultOption = await updateSessionWaiter.Task;

        if (!updateSessionResultOption.TryUnwrap(out var updateSessionResult)) { Result.Failure(EosInterface.Sessions.AttributeUpdateError.TimedOut); }

        if (updateSessionResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return updateSessionResult.ResultCode switch
            {
                Epic.OnlineServices.Result.InvalidParameters
                    => Result.Failure(EosInterface.Sessions.AttributeUpdateError.InvalidParametersForSessionUpdate),
                Epic.OnlineServices.Result.SessionsOutOfSync
                    => Result.Failure(EosInterface.Sessions.AttributeUpdateError.SessionsOutOfSync),
                Epic.OnlineServices.Result.NotFound
                    => Result.Failure(EosInterface.Sessions.AttributeUpdateError.SessionNotFound),
                Epic.OnlineServices.Result.NoConnection
                    => Result.Failure(EosInterface.Sessions.AttributeUpdateError.NoConnection),
                var unhandled
                    => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.Sessions.AttributeUpdateError.UnhandledErrorCondition))
            };
        }

        session.SyncedAttributes = session.Attributes.ToImmutableDictionary();
        return Result.Success(Unit.Value);
    }

    public static async Task<Result<Unit, EosInterface.Sessions.CloseError>> CloseOwnedSession(EosInterface.Sessions.OwnedSession session)
    {
        if (CorePrivate.SessionsInterface is not { } sessionsInterface) { return Result.Failure(EosInterface.Sessions.CloseError.EosNotInitialized); }

        liveOwnedSessions.TryRemove(session.InternalId, out _);

        var options = new Epic.OnlineServices.Sessions.DestroySessionOptions
        {
            SessionName = session.InternalId.Value.ToUpperInvariant()
        };

        var callbackWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.DestroySessionCallbackInfo>();
        sessionsInterface.DestroySession(options: ref options, clientData: null, completionDelegate: callbackWaiter.OnCompletion);
        var resultOption = await callbackWaiter.Task;

        if (!resultOption.TryUnwrap(out var result)) { return Result.Failure(EosInterface.Sessions.CloseError.TimedOut); }

        return result.ResultCode switch
        {
            Epic.OnlineServices.Result.Success
                => Result.Success(Unit.Value),
            Epic.OnlineServices.Result.InvalidParameters
                => Result.Failure(EosInterface.Sessions.CloseError.InvalidParameters),
            Epic.OnlineServices.Result.AlreadyPending
                => Result.Failure(EosInterface.Sessions.CloseError.AlreadyPending),
            Epic.OnlineServices.Result.NotFound
                => Result.Failure(EosInterface.Sessions.CloseError.NotFound),
            var unhandled
                => Result.Failure(unhandled.FailAndLogUnhandledError(EosInterface.Sessions.CloseError.UnhandledErrorCondition))
        };
    }

    public static Task CloseAllOwnedSessions()
    {
        return Task.WhenAll(liveOwnedSessions.Values
            .ToArray()
            .Select(CloseOwnedSession));
    }

    public static Task ForceUpdateAllOwnedSessions()
    {
        var sessionsToUpdate = liveOwnedSessions.Values.ToArray();
        foreach (var session in sessionsToUpdate)
        {
            session.SyncedAttributes = ImmutableDictionary<Identifier, string>.Empty;
        }
        return Task.WhenAll(sessionsToUpdate
            .Select(UpdateOwnedSessionAttributes));
    }

    public static async Task<Result<ImmutableArray<EosInterface.ProductUserId>, EosInterface.Sessions.RegisterError>> RegisterPlayers(EosInterface.Sessions.OwnedSession session, params EosInterface.ProductUserId[] puids)
    {
        if (CorePrivate.SessionsInterface is not { } sessionsInterface) { return Result.Failure(EosInterface.Sessions.RegisterError.EosNotInitialized); }

        var registerPlayersOptions = new Epic.OnlineServices.Sessions.RegisterPlayersOptions
        {
            SessionName = session.InternalId.Value.ToUpperInvariant(),
            PlayersToRegister = puids.Select(puid => Epic.OnlineServices.ProductUserId.FromString(puid.Value)).ToArray()
        };
        var registerPlayersWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.RegisterPlayersCallbackInfo>();
        sessionsInterface.RegisterPlayers(options: ref registerPlayersOptions, clientData: null, completionDelegate: registerPlayersWaiter.OnCompletion);
        var registerResultOption = await registerPlayersWaiter.Task;

        if (!registerResultOption.TryUnwrap(out var registerResult)) { return Result.Failure(EosInterface.Sessions.RegisterError.TimedOut); }

        if (registerResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(registerResult.ResultCode.FailAndLogUnhandledError(EosInterface.Sessions.RegisterError.UnhandledErrorCondition));
        }

        return Result.Success(registerResult.RegisteredPlayers.Select(puid => new EosInterface.ProductUserId(puid.ToString())).ToImmutableArray());
    }

    public static async Task<Result<ImmutableArray<EosInterface.ProductUserId>, EosInterface.Sessions.UnregisterError>> UnregisterPlayers(EosInterface.Sessions.OwnedSession session, params EosInterface.ProductUserId[] puids)
    {
        if (CorePrivate.SessionsInterface is not { } sessionsInterface) { return Result.Failure(EosInterface.Sessions.UnregisterError.EosNotInitialized); }

        var unregisterPlayersOptions = new Epic.OnlineServices.Sessions.UnregisterPlayersOptions
        {
            SessionName = session.InternalId.Value.ToUpperInvariant(),
            PlayersToUnregister = puids.Select(puid => Epic.OnlineServices.ProductUserId.FromString(puid.Value)).ToArray()
        };
        var unregisterPlayersWaiter = new CallbackWaiter<Epic.OnlineServices.Sessions.UnregisterPlayersCallbackInfo>();
        sessionsInterface.UnregisterPlayers(options: ref unregisterPlayersOptions, clientData: null, completionDelegate: unregisterPlayersWaiter.OnCompletion);
        var unregisterResultOption = await unregisterPlayersWaiter.Task;

        if (!unregisterResultOption.TryUnwrap(out var unregisterResult)) { return Result.Failure(EosInterface.Sessions.UnregisterError.TimedOut); }

        if (unregisterResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(unregisterResult.ResultCode.FailAndLogUnhandledError(EosInterface.Sessions.UnregisterError.UnhandledErrorCondition));
        }

        return Result.Success(unregisterResult.UnregisteredPlayers.Select(puid => new EosInterface.ProductUserId(puid.ToString())).ToImmutableArray());
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<EosInterface.Sessions.OwnedSession, EosInterface.Sessions.CreateError>> CreateSession(Option<EosInterface.ProductUserId> selfUserIdOption, Identifier internalId, int maxPlayers)
        => TaskScheduler.Schedule(() => OwnedSessionsPrivate.Create(selfUserIdOption, internalId, maxPlayers));

    public override Task<Result<Unit, EosInterface.Sessions.AttributeUpdateError>> UpdateOwnedSessionAttributes(EosInterface.Sessions.OwnedSession session)
        => TaskScheduler.Schedule(() => OwnedSessionsPrivate.UpdateOwnedSessionAttributes(session));

    public override Task<Result<Unit, EosInterface.Sessions.CloseError>> CloseOwnedSession(EosInterface.Sessions.OwnedSession session)
        => TaskScheduler.Schedule(() => OwnedSessionsPrivate.CloseOwnedSession(session));

    public override Task CloseAllOwnedSessions()
        => TaskScheduler.Schedule(OwnedSessionsPrivate.CloseAllOwnedSessions);
}
