#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma;
using Barotrauma.Extensions;
using EpicAccountId = Barotrauma.Networking.EpicAccountId;
using Result = Barotrauma.Result;

namespace EosInterfacePrivate;

static class PresencePrivate
{
    internal static readonly NamedEvent<EosInterface.Presence.JoinGameInfo> OnJoinGame = new NamedEvent<EosInterface.Presence.JoinGameInfo>();
    private static ulong joinGameAcceptedNotificationId = Epic.OnlineServices.Common.InvalidNotificationid;

    internal static readonly NamedEvent<EosInterface.Presence.AcceptInviteInfo> OnInviteAccepted = new NamedEvent<EosInterface.Presence.AcceptInviteInfo>();
    private static ulong inviteAcceptedNotificationId = Epic.OnlineServices.Common.InvalidNotificationid;
    private static ulong inviteRejectedNotificationId = Epic.OnlineServices.Common.InvalidNotificationid;

    internal static readonly NamedEvent<EosInterface.Presence.ReceiveInviteInfo> OnInviteReceived = new NamedEvent<EosInterface.Presence.ReceiveInviteInfo>();
    private static ulong inviteReceivedNotificationId = Epic.OnlineServices.Common.InvalidNotificationid;

    public static void Init(ImplementationPrivate implementation)
    {
        var presenceInterface = CorePrivate.EgsPresenceInterface;
        var customInvitesInterface = CorePrivate.EgsCustomInvitesInterface;
        if (presenceInterface is null
            || customInvitesInterface is null)
        {
            return;
        }

        var boilerplate0 = new Epic.OnlineServices.Presence.AddNotifyJoinGameAcceptedOptions();
        joinGameAcceptedNotificationId = presenceInterface.AddNotifyJoinGameAccepted(ref boilerplate0, null, OnJoinGameAcceptedEos);

        var boilerplate1 = new Epic.OnlineServices.CustomInvites.AddNotifyCustomInviteAcceptedOptions();
        inviteAcceptedNotificationId = customInvitesInterface.AddNotifyCustomInviteAccepted(ref boilerplate1, implementation, OnInviteAcceptedEos);
        
        var boilerplate2 = new Epic.OnlineServices.CustomInvites.AddNotifyCustomInviteRejectedOptions();
        inviteRejectedNotificationId = customInvitesInterface.AddNotifyCustomInviteRejected(ref boilerplate2, null, OnInviteRejectedEos);

        var boilerplate3 = new Epic.OnlineServices.CustomInvites.AddNotifyCustomInviteReceivedOptions();
        inviteReceivedNotificationId = customInvitesInterface.AddNotifyCustomInviteReceived(ref boilerplate3, implementation, OnInviteReceivedEos);
    }

    public static void Quit()
    {
        OnJoinGame.Dispose();
        OnInviteAccepted.Dispose();

        var presenceInterface = CorePrivate.EgsPresenceInterface;
        var customInvitesInterface = CorePrivate.EgsCustomInvitesInterface;
        if (presenceInterface is null
            || customInvitesInterface is null)
        {
            return;
        }

        static void callRemover(Action<ulong> remover, ref ulong id)
        {
            remover(id);
            id = Epic.OnlineServices.Common.InvalidNotificationid;
        }

        callRemover(presenceInterface.RemoveNotifyJoinGameAccepted, ref joinGameAcceptedNotificationId);
        callRemover(customInvitesInterface.RemoveNotifyCustomInviteAccepted, ref inviteAcceptedNotificationId);
        callRemover(customInvitesInterface.RemoveNotifyCustomInviteRejected, ref inviteRejectedNotificationId);
        callRemover(customInvitesInterface.RemoveNotifyCustomInviteReceived, ref inviteReceivedNotificationId);
    }

    private static void OnJoinGameAcceptedEos(ref Epic.OnlineServices.Presence.JoinGameAcceptedCallbackInfo data)
    {
        if (data.UiEventId != Epic.OnlineServices.UI.UIInterface.EventidInvalid)
        {
            // What is this for? I have no idea.
            // Documentation says it's important tho:
            // https://dev.epicgames.com/docs/epic-account-services/social-overlay-overview/sdk-integration#invite-lifecycle-and-caveats
            var egsUiInterface = CorePrivate.EgsUiInterface;
            if (egsUiInterface != null)
            {
                var ack = new Epic.OnlineServices.UI.AcknowledgeEventIdOptions
                {
                    UiEventId = data.UiEventId,
                    Result = Epic.OnlineServices.Result.Success
                };
                egsUiInterface.AcknowledgeEventId(ref ack);
            }
        }

        var selfEpicIdOption = EpicAccountId.Parse(data.LocalUserId.ToString());
        if (!selfEpicIdOption.TryUnwrap(out var selfEpicId)) { return; }

        var joinCommandStr = data.JoinInfo;

        OnJoinGame.Invoke(new EosInterface.Presence.JoinGameInfo(selfEpicId, joinCommandStr));
    }

    private static void OnInviteAcceptedEos(ref Epic.OnlineServices.CustomInvites.OnCustomInviteAcceptedCallbackInfo data)
    {
        if (data.LocalUserId is null) { return; }
        if (data.ClientData is not ImplementationPrivate implementation) { return; }

        RemoveInvite(
            recipientPuid: new EosInterface.ProductUserId(data.LocalUserId.ToString()),
            senderPuid: new EosInterface.ProductUserId(data.TargetUserId.ToString()));

        var joinCommandStr = data.Payload;

        var selfPuid = new EosInterface.ProductUserId(data.LocalUserId.ToString());

        async Task<Option<EosInterface.Presence.AcceptInviteInfo>> prepareCallbackInfo()
        {
            var selfExternalAccountIdsTask = IdQueriesPrivate.GetExternalAccountIds(selfPuid, selfPuid);

            await Task.WhenAll(selfExternalAccountIdsTask, selfExternalAccountIdsTask);

            var selfExternalAccountIdsResult = await selfExternalAccountIdsTask;

            if (!selfExternalAccountIdsResult.TryUnwrapSuccess(out var selfExternalAccountIds)
                || !selfExternalAccountIds.OfType<EpicAccountId>().FirstOrNone().TryUnwrap(out var selfEpicAccountId))
            {
                return Option.None;
            }

            return Option.Some(new EosInterface.Presence.AcceptInviteInfo(
                selfEpicAccountId,
                joinCommandStr));
        }

        TaskPool.Add(
            $"AcceptedInviteFor{selfPuid.Value}",
            implementation.TaskScheduler.Schedule(prepareCallbackInfo),
            t =>
            {
                if (!t.TryGetResult(out Option<EosInterface.Presence.AcceptInviteInfo> infoOption)) { return; }
                if (!infoOption.TryUnwrap(out var info)) { return; }

                OnInviteAccepted.Invoke(info);
            });
    }

    private static void OnInviteRejectedEos(ref Epic.OnlineServices.CustomInvites.CustomInviteRejectedCallbackInfo data)
    {
        if (data.LocalUserId is null) { return; }

        RemoveInvite(
            recipientPuid: new EosInterface.ProductUserId(data.LocalUserId.ToString()),
            senderPuid: new EosInterface.ProductUserId(data.TargetUserId.ToString()));
    }

    private readonly record struct InviteId(
        EpicAccountId RecipientEpicId,
        EpicAccountId SenderEpicId,
        EosInterface.ProductUserId RecipientPuid,
        EosInterface.ProductUserId SenderPuid,
        string IdValue);

    private static readonly List<InviteId> ReceivedInviteIds = new List<InviteId>();

    private static void RemoveInvite(EpicAccountId recipientEpicId, EpicAccountId senderEpicId)
    {
        RemoveInvites(ReceivedInviteIds.Where(id => id.RecipientEpicId == recipientEpicId && id.SenderEpicId == senderEpicId).ToImmutableArray());
    }

    private static void RemoveInvite(EosInterface.ProductUserId recipientPuid, EosInterface.ProductUserId senderPuid)
    {
        RemoveInvites(ReceivedInviteIds.Where(id => id.RecipientPuid == recipientPuid && id.SenderPuid == senderPuid).ToImmutableArray());
    }

    private static void RemoveInvites(ImmutableArray<InviteId> invites)
    {
        var customInvitesInterface = CorePrivate.EgsCustomInvitesInterface;
        if (customInvitesInterface == null) { return; }

        foreach (var invite in invites)
        {
            ReceivedInviteIds.Remove(invite);
            var targetUserId = Epic.OnlineServices.ProductUserId.FromString(invite.SenderPuid.Value);
            var localUserId = Epic.OnlineServices.ProductUserId.FromString(invite.RecipientPuid.Value);
            var finalizeInviteOptions = new Epic.OnlineServices.CustomInvites.FinalizeInviteOptions
            {
                TargetUserId = targetUserId,
                LocalUserId = localUserId,
                CustomInviteId = invite.IdValue,
                ProcessingResult = Epic.OnlineServices.Result.Success
            };
            customInvitesInterface.FinalizeInvite(ref finalizeInviteOptions);
        }
    }

    private static void OnInviteReceivedEos(ref Epic.OnlineServices.CustomInvites.OnCustomInviteReceivedCallbackInfo data)
    {
        if (data.ClientData is not ImplementationPrivate implementation) { return; }
        var joinCommandStr = data.Payload;
        
        var selfPuid = new EosInterface.ProductUserId(data.LocalUserId.ToString());
        var senderPuid = new EosInterface.ProductUserId(data.TargetUserId.ToString());
        var inviteIdValue = data.CustomInviteId;

        // We can only have one invite for the same recipient-sender pair
        RemoveInvite(
            recipientPuid: selfPuid,
            senderPuid: senderPuid);

        async Task<Option<EosInterface.Presence.ReceiveInviteInfo>> prepareCallbackInfo()
        {
            var selfExternalAccountIdsTask = IdQueriesPrivate.GetExternalAccountIds(selfPuid, selfPuid);
            var senderExternalAccountIdsTask = IdQueriesPrivate.GetExternalAccountIds(selfPuid, senderPuid);

            await Task.WhenAll(selfExternalAccountIdsTask, selfExternalAccountIdsTask);

            var selfExternalAccountIdsResult = await selfExternalAccountIdsTask;
            var senderExternalAccountIdsResult = await senderExternalAccountIdsTask;

            if (!selfExternalAccountIdsResult.TryUnwrapSuccess(out var selfExternalAccountIds)
                || !selfExternalAccountIds.OfType<EpicAccountId>().FirstOrNone().TryUnwrap(out var selfEpicAccountId))
            {
                return Option.None;
            }

            if (!senderExternalAccountIdsResult.TryUnwrapSuccess(out var senderExternalAccountIds)
                || !senderExternalAccountIds.OfType<EpicAccountId>().FirstOrNone().TryUnwrap(out var senderEpicAccountId))
            {
                return Option.None;
            }

            return Option.Some(new EosInterface.Presence.ReceiveInviteInfo(
                selfEpicAccountId,
                senderEpicAccountId,
                joinCommandStr));
        }

        TaskPool.Add(
            $"ReceivedInviteFrom{senderPuid.Value}",
            implementation.TaskScheduler.Schedule(prepareCallbackInfo),
            t =>
            {
                if (!t.TryGetResult(out Option<EosInterface.Presence.ReceiveInviteInfo> infoOption)) { return; }

                if (!infoOption.TryUnwrap(out var info)) { return; }

                ReceivedInviteIds.Add(new InviteId(
                    RecipientEpicId: info.RecipientId,
                    SenderEpicId: info.SenderId,
                    RecipientPuid: selfPuid,
                    SenderPuid: senderPuid,
                    IdValue: inviteIdValue));

                OnInviteReceived.Invoke(info);
            });
    }

    public static async Task<Result<Unit, EosInterface.Presence.SetJoinCommandError>> SetJoinCommand(
        EpicAccountId epicAccountId,
        string desc,
        string serverName,
        string joinCommand)
    {
        if (string.IsNullOrWhiteSpace(joinCommand))
        {
            desc = "";
        }

        if (desc.Length > Epic.OnlineServices.Presence.PresenceInterface.RichTextMaxValueLength)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.DescTooLong);
        }
        if (joinCommand.Length > Epic.OnlineServices.Presence.PresenceModification.PresencemodificationJoininfoMaxLength)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.JoinCommandTooLong);
        }

        if (serverName.Length > Epic.OnlineServices.Presence.PresenceInterface.DataMaxValueLength)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.ServerNameTooLong);
        }
        if (joinCommand.Length > Epic.OnlineServices.Presence.PresenceInterface.DataMaxValueLength)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.JoinCommandTooLong);
        }

        using var janitor = Janitor.Start();

        var presenceInterface = CorePrivate.EgsPresenceInterface;
        var customInvitesInterface = CorePrivate.EgsCustomInvitesInterface;
        if (presenceInterface is null
            || customInvitesInterface is null)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.EosNotInitialized);
        }

        var epicAccountIdInternal = Epic.OnlineServices.EpicAccountId.FromString(epicAccountId.EosStringRepresentation);

        var puidResult = await IdQueriesPrivate.GetPuidForExternalId(epicAccountId);
        if (!puidResult.TryUnwrapSuccess(out var puid))
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToGetPuid);
        }

        var puidInternal = Epic.OnlineServices.ProductUserId.FromString(puid.Value);

        var setCustomInviteOptions = new Epic.OnlineServices.CustomInvites.SetCustomInviteOptions
        {
            LocalUserId = puidInternal,
            Payload = joinCommand
        };
        var setCustomInviteResult = customInvitesInterface.SetCustomInvite(ref setCustomInviteOptions);
        if (setCustomInviteResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToSetCustomInvite);
        }

        var createPresenceModificationOptions = new Epic.OnlineServices.Presence.CreatePresenceModificationOptions
        {
            LocalUserId = epicAccountIdInternal
        };
        var createPresenceModificationResult = presenceInterface.CreatePresenceModification(ref createPresenceModificationOptions, out var presenceModification);
        janitor.AddAction(presenceModification.Release);
        if (createPresenceModificationResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToCreatePresenceModification);
        }

        var setRichTextOptions = new Epic.OnlineServices.Presence.PresenceModificationSetRawRichTextOptions
        {
            RichText = desc
        };
        var setRichTextResult = presenceModification.SetRawRichText(ref setRichTextOptions);
        if (setRichTextResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToSetRichText);
        }

        var setDataOptions = new Epic.OnlineServices.Presence.PresenceModificationSetDataOptions
        {
            Records = new[]
            {
                new Epic.OnlineServices.Presence.DataRecord
                {
                    Key = "servername",
                    Value = serverName
                },
                new Epic.OnlineServices.Presence.DataRecord
                {
                    Key = "connectcommand",
                    Value = joinCommand
                }
            }
        };
        var setDataResult =  presenceModification.SetData(ref setDataOptions);
        if (setDataResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToSetRecords);
        }

        // This is necessary to make the SDK not choke if given an empty, but not null, joinCommand
        string? joinCommandNullable = string.IsNullOrWhiteSpace(joinCommand) ? null : joinCommand;

        var setJoinInfoOptions = new Epic.OnlineServices.Presence.PresenceModificationSetJoinInfoOptions
        {
            JoinInfo = joinCommandNullable
        };
        var setJoinInfoResult = presenceModification.SetJoinInfo(ref setJoinInfoOptions);
        if (setJoinInfoResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToSetJoinInfo);
        }

        var setPresenceOptions = new Epic.OnlineServices.Presence.SetPresenceOptions
        {
            LocalUserId = epicAccountIdInternal,
            PresenceModificationHandle = presenceModification
        };
        var setPresenceWaiter = new CallbackWaiter<Epic.OnlineServices.Presence.SetPresenceCallbackInfo>();
        presenceInterface.SetPresence(options: ref setPresenceOptions, clientData: null, completionDelegate: setPresenceWaiter.OnCompletion);
        var setPresenceResultOption = await setPresenceWaiter.Task;
        if (!setPresenceResultOption.TryUnwrap(out var setPresenceResult))
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.SetPresenceTimedOut);
        }

        if (setPresenceResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SetJoinCommandError.FailedToSetPresence);
        }

        return Result.Success(Unit.Value);
    }

    public static async Task<Result<Unit, EosInterface.Presence.SendInviteError>> SendInvite(EpicAccountId selfEpicAccountId, EpicAccountId remoteEpicAccountId)
    {
        var customInvitesInterface = CorePrivate.EgsCustomInvitesInterface;
        if (customInvitesInterface is null)
        {
            return Result.Failure(EosInterface.Presence.SendInviteError.EosNotInitialized);
        }

        var selfPuidResult = await IdQueriesPrivate.GetPuidForExternalId(selfEpicAccountId);
        if (!selfPuidResult.TryUnwrapSuccess(out var selfPuid))
        {
            return Result.Failure(EosInterface.Presence.SendInviteError.FailedToGetSelfPuid);
        }

        var selfPuidInternal = Epic.OnlineServices.ProductUserId.FromString(selfPuid.Value);

        var remotePuidResult = await IdQueriesPrivate.GetPuidForExternalId(remoteEpicAccountId);
        if (!remotePuidResult.TryUnwrapSuccess(out var remotePuid))
        {
            return Result.Failure(EosInterface.Presence.SendInviteError.FailedToGetRemotePuid);
        }

        var remotePuidInternal = Epic.OnlineServices.ProductUserId.FromString(remotePuid.Value);

        var sendCustomInviteOptions = new Epic.OnlineServices.CustomInvites.SendCustomInviteOptions
        {
            LocalUserId = selfPuidInternal,
            TargetUserIds = new[]
            {
                remotePuidInternal
            }
        };
        var callbackWaiter = new CallbackWaiter<Epic.OnlineServices.CustomInvites.SendCustomInviteCallbackInfo>();
        customInvitesInterface.SendCustomInvite(options: ref sendCustomInviteOptions, clientData: null, completionDelegate: callbackWaiter.OnCompletion);
        var callbackResultOption = await callbackWaiter.Task;
        if (!callbackResultOption.TryUnwrap(out var callbackResult))
        {
            return Result.Failure(EosInterface.Presence.SendInviteError.TimedOut);
        }

        if (callbackResult.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Presence.SendInviteError.InternalError);
        }

        return Result.Success(Unit.Value);
    }

    public static void DeclineInvite(EpicAccountId selfEpicAccountId, EpicAccountId senderEpicAccountId)
    {
        RemoveInvite(
            recipientEpicId: selfEpicAccountId,
            senderEpicId: senderEpicAccountId);
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override NamedEvent<EosInterface.Presence.JoinGameInfo> OnJoinGame => PresencePrivate.OnJoinGame;
    public override NamedEvent<EosInterface.Presence.AcceptInviteInfo> OnInviteAccepted => PresencePrivate.OnInviteAccepted;
    public override NamedEvent<EosInterface.Presence.ReceiveInviteInfo> OnInviteReceived => PresencePrivate.OnInviteReceived;

    public override Task<Result<Unit, EosInterface.Presence.SetJoinCommandError>> SetJoinCommand(
        EpicAccountId epicAccountId, string desc, string serverName, string joinCommand)
        => TaskScheduler.Schedule(() => PresencePrivate.SetJoinCommand(epicAccountId, desc, serverName, joinCommand));

    public override Task<Result<Unit, EosInterface.Presence.SendInviteError>> SendInvite(
        EpicAccountId selfEpicAccountId, EpicAccountId remoteEpicAccountId)
        => TaskScheduler.Schedule(() => PresencePrivate.SendInvite(selfEpicAccountId, remoteEpicAccountId));

    public override void DeclineInvite(EpicAccountId selfEpicAccountId, EpicAccountId senderEpicAccountId)
        => PresencePrivate.DeclineInvite(selfEpicAccountId, senderEpicAccountId);
}
