using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public static class Friends
    {
        private static Implementation? LoadedImplementation => Core.LoadedImplementation;

        public enum GetFriendsError
        {
            EosNotInitialized,

            EgsFriendsQueryTimedOut,
            EgsFriendsQueryFailed,

            UserInfoQueryTimedOut,
            UserInfoQueryFailed,
            CopyUserInfoFailed,
            DisplayNameIsEmpty,

            EgsPresenceQueryTimedOut,
            EgsPresenceQueryFailed,
            CopyPresenceFailed,

            UnhandledErrorCondition
        }

        public static async Task<Result<EgsFriend, GetFriendsError>> GetFriend(
            EpicAccountId selfEaid,
            EpicAccountId friendEaid)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.GetFriend(selfEaid, friendEaid)
                : Result.Failure(GetFriendsError.EosNotInitialized);

        public static async Task<Result<ImmutableArray<EgsFriend>, GetFriendsError>> GetFriends(
            EpicAccountId epicAccountId)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.GetFriends(epicAccountId)
                : Result.Failure(GetFriendsError.EosNotInitialized);

        public static async Task<Result<EgsFriend, GetFriendsError>> GetSelfUserInfo(EpicAccountId epicAccountId)
            => LoadedImplementation.IsInitialized()
                ? await LoadedImplementation.GetSelfUserInfo(epicAccountId)
                : Result.Failure(GetFriendsError.EosNotInitialized);
    }

    internal abstract partial class Implementation
    {
        public abstract Task<Result<EgsFriend, Friends.GetFriendsError>> GetFriend(
            EpicAccountId selfEaid,
            EpicAccountId friendEaid);

        public abstract Task<Result<ImmutableArray<EgsFriend>, Friends.GetFriendsError>> GetFriends(
            EpicAccountId epicAccountId);

        public abstract Task<Result<EgsFriend, Friends.GetFriendsError>> GetSelfUserInfo(EpicAccountId epicAccountId);
    }
}