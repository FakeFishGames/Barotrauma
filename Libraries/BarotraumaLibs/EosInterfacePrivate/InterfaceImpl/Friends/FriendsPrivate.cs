#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma;
using Barotrauma.Extensions;

namespace EosInterfacePrivate;

static class FriendsPrivate
{
    internal static async Task<Result<Epic.OnlineServices.UserInfo.UserInfoData, EosInterface.Friends.GetFriendsError>> GetUserInfoData(
        EpicAccountId selfEaid, EpicAccountId friendEaid)
    {
        if (CorePrivate.EgsUserInfoInterface is not { } egsUserInfoInterface) { return Result.Failure(EosInterface.Friends.GetFriendsError.EosNotInitialized); }

        var selfEaidInternal = Epic.OnlineServices.EpicAccountId.FromString(selfEaid.EosStringRepresentation);
        var friendEaidInternal = Epic.OnlineServices.EpicAccountId.FromString(friendEaid.EosStringRepresentation);

        var queryUserInfoOptions = new Epic.OnlineServices.UserInfo.QueryUserInfoOptions
        {
            LocalUserId = selfEaidInternal,
            TargetUserId = friendEaidInternal
        };
        var queryUserInfoWaiter = new CallbackWaiter<Epic.OnlineServices.UserInfo.QueryUserInfoCallbackInfo>();
        egsUserInfoInterface.QueryUserInfo(options: ref queryUserInfoOptions, clientData: null, completionDelegate: queryUserInfoWaiter.OnCompletion);
        var queryUserInfoResult = await queryUserInfoWaiter.Task;
        if (!queryUserInfoResult.TryUnwrap(out var queryUserInfo))
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.UserInfoQueryTimedOut);
        }
        if (queryUserInfo.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.UserInfoQueryFailed);
        }

        var copyUserInfoOptions = new Epic.OnlineServices.UserInfo.CopyUserInfoOptions
        {
            LocalUserId = selfEaidInternal,
            TargetUserId = friendEaidInternal
        };
        var copyUserInfoResult = egsUserInfoInterface.CopyUserInfo(ref copyUserInfoOptions, out var friendInfoNullable);
        if (copyUserInfoResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.CopyUserInfoFailed);
        }
        if (friendInfoNullable is not { } friendInfo)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.CopyUserInfoFailed);
        }

        string displayName = friendInfo.Nickname ?? friendInfo.DisplayName ?? "";
        if (string.IsNullOrEmpty(displayName))
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.DisplayNameIsEmpty);
        }

        return Result.Success(friendInfo);
    }

    internal static async Task<Result<EosInterface.EgsFriend, EosInterface.Friends.GetFriendsError>> GetPresenceFromUserInfoData(
        EpicAccountId selfEaid, EpicAccountId friendEaid, Epic.OnlineServices.UserInfo.UserInfoData friendInfo)
    {
        if (CorePrivate.EgsPresenceInterface is not { } egsPresenceInterface) { return Result.Failure(EosInterface.Friends.GetFriendsError.EosNotInitialized); }

        var selfEaidInternal = Epic.OnlineServices.EpicAccountId.FromString(selfEaid.EosStringRepresentation);
        var friendEaidInternal = friendInfo.UserId;

        string displayName = friendInfo.Nickname ?? friendInfo.DisplayName ?? "";

        var queryPresenceOptions = new Epic.OnlineServices.Presence.QueryPresenceOptions
        {
            LocalUserId = selfEaidInternal,
            TargetUserId = friendEaidInternal
        };
        var queryPresenceWaiter = new CallbackWaiter<Epic.OnlineServices.Presence.QueryPresenceCallbackInfo>();
        egsPresenceInterface.QueryPresence(options: ref queryPresenceOptions, clientData: null, completionDelegate: queryPresenceWaiter.OnCompletion);
        var queryPresenceResult = await queryPresenceWaiter.Task;
        if (!queryPresenceResult.TryUnwrap(out var queryPresence))
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.EgsPresenceQueryTimedOut);
        }
        if (queryPresence.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.EgsPresenceQueryFailed);
        }

        var copyPresenceOptions = new Epic.OnlineServices.Presence.CopyPresenceOptions
        {
            LocalUserId = selfEaidInternal,
            TargetUserId = friendEaidInternal
        };
        var copyPresenceResult = egsPresenceInterface.CopyPresence(ref copyPresenceOptions, out var friendPresenceNullable);
        if (copyPresenceResult != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.CopyPresenceFailed);
        }
        if (friendPresenceNullable is not { } friendPresence)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.CopyPresenceFailed);
        }

        string productId = friendPresence.ProductId ?? "";
        var friendStatus = friendPresence.Status switch
        {
            Epic.OnlineServices.Presence.Status.Offline
                => FriendStatus.Offline,
            _
                => productId == PlatformInterfaceOptionsPrivate.BasePlatformInterfaceOptions.ProductId
                    ? FriendStatus.PlayingBarotrauma
                    : !string.IsNullOrEmpty(productId)
                        ? FriendStatus.PlayingAnotherGame
                        : FriendStatus.NotPlaying
        };

        var records = friendPresence.Records ?? Array.Empty<Epic.OnlineServices.Presence.DataRecord>();

        string getRecordValue(string key)
            => records
                .FirstOrNone(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .Fallback("");
        var connectCommand = getRecordValue("connectcommand");
        var serverName = getRecordValue("servername");

        return Result.Success(new EosInterface.EgsFriend(
            DisplayName: displayName,
            EpicAccountId: friendEaid,
            Status: friendStatus,
            ConnectCommand: connectCommand,
            ServerName: serverName));
    }

    public static async Task<Result<EosInterface.EgsFriend, EosInterface.Friends.GetFriendsError>> GetSelfUserInfo(EpicAccountId epicAccount)
    {
        var getUserInfoDataResult = await GetUserInfoData(epicAccount, epicAccount);
        if (getUserInfoDataResult.TryUnwrapFailure(out var error))
        {
            return Result.Failure(error);
        }
        if (!getUserInfoDataResult.TryUnwrapSuccess(out var friendInfo))
        {
            throw new UnreachableCodeException();
        }

        string displayName = friendInfo.Nickname ?? friendInfo.DisplayName ?? "";

        return Result.Success(new EosInterface.EgsFriend(
            DisplayName: displayName,
            EpicAccountId: epicAccount,
            Status: FriendStatus.PlayingBarotrauma,
            ConnectCommand: "",
            ServerName: ""));
    }

    public static async Task<Result<EosInterface.EgsFriend, EosInterface.Friends.GetFriendsError>> GetFriend(EpicAccountId selfEaid, EpicAccountId friendEaid)
    {
        var getUserInfoDataResult = await GetUserInfoData(selfEaid, friendEaid);
        if (getUserInfoDataResult.TryUnwrapFailure(out var error))
        {
            return Result.Failure(error);
        }
        if (!getUserInfoDataResult.TryUnwrapSuccess(out var friendInfo))
        {
            throw new UnreachableCodeException();
        }

        return await GetPresenceFromUserInfoData(selfEaid, friendEaid, friendInfo);
    }
    
    public static async Task<Result<ImmutableArray<EosInterface.EgsFriend>, EosInterface.Friends.GetFriendsError>> GetFriends(EpicAccountId epicAccount)
    {
        if (CorePrivate.EgsFriendsInterface is not { } egsFriendsInterface) { return Result.Failure(EosInterface.Friends.GetFriendsError.EosNotInitialized); }

        var selfEaidInternal = Epic.OnlineServices.EpicAccountId.FromString(epicAccount.EosStringRepresentation);

        var queryFriendsOptions = new Epic.OnlineServices.Friends.QueryFriendsOptions
        {
            LocalUserId = selfEaidInternal
        };
        var queryFriendsWaiter = new CallbackWaiter<Epic.OnlineServices.Friends.QueryFriendsCallbackInfo>();
        egsFriendsInterface.QueryFriends(options: ref queryFriendsOptions, clientData: null, completionDelegate: queryFriendsWaiter.OnCompletion);
        var queryFriendsInfoResult = await queryFriendsWaiter.Task;
        if (!queryFriendsInfoResult.TryUnwrap(out var queryFriendsInfo))
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.EgsFriendsQueryTimedOut);
        }

        if (queryFriendsInfo.ResultCode != Epic.OnlineServices.Result.Success)
        {
            return Result.Failure(EosInterface.Friends.GetFriendsError.EgsFriendsQueryFailed);
        }

        var getFriendsCountOptions = new Epic.OnlineServices.Friends.GetFriendsCountOptions
        {
            LocalUserId = selfEaidInternal
        };
        var friendCount = egsFriendsInterface.GetFriendsCount(ref getFriendsCountOptions);
        var friends = new List<EosInterface.EgsFriend>();

        for (int i = 0; i < friendCount; i++)
        {
            var getFriendAtIndexOptions = new Epic.OnlineServices.Friends.GetFriendAtIndexOptions
            {
                LocalUserId = selfEaidInternal,
                Index = i
            };
            var friendId = egsFriendsInterface.GetFriendAtIndex(ref getFriendAtIndexOptions);
            if (friendId == null)
            {
                continue;
            }

            if (!EpicAccountId.Parse(friendId.ToString()).TryUnwrap(out var friendIdPublic))
            {
                continue;
            }

            var getUserInfoDataResult = await GetUserInfoData(epicAccount, friendIdPublic);
            if (!getUserInfoDataResult.TryUnwrapSuccess(out var friendInfo))
            {
                continue;
            }

            var egsFriendPublicResult = await GetPresenceFromUserInfoData(epicAccount, friendIdPublic, friendInfo);
            if (!egsFriendPublicResult.TryUnwrapSuccess(out var egsFriendPublic))
            {
                continue;
            }

            friends.Add(egsFriendPublic);
        }
        return Result.Success(friends.ToImmutableArray());
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Task<Result<EosInterface.EgsFriend, EosInterface.Friends.GetFriendsError>> GetFriend(EpicAccountId selfEaid, EpicAccountId friendEaid)
        => TaskScheduler.Schedule(() => FriendsPrivate.GetFriend(selfEaid, friendEaid));

    public override Task<Result<ImmutableArray<EosInterface.EgsFriend>, EosInterface.Friends.GetFriendsError>> GetFriends(EpicAccountId epicAccountId)
        => TaskScheduler.Schedule(() => FriendsPrivate.GetFriends(epicAccountId));

    public override Task<Result<EosInterface.EgsFriend, EosInterface.Friends.GetFriendsError>> GetSelfUserInfo(EpicAccountId epicAccountId)
        => TaskScheduler.Schedule(() => FriendsPrivate.GetSelfUserInfo(epicAccountId));
}
