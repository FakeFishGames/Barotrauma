#nullable enable

using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract class FriendProvider
    {
        public async Task<Option<FriendInfo>> RetrieveFriendWithAvatar(AccountId id, int size)
        {
            var friendOption = await RetrieveFriend(id);
            if (!friendOption.TryUnwrap(out var friend)) { return Option.None; }

            friend.Avatar = await RetrieveAvatar(friend, size);
            return Option.Some(friend);
        }

        public abstract Task<Option<FriendInfo>> RetrieveFriend(AccountId id);
        public abstract Task<ImmutableArray<FriendInfo>> RetrieveFriends();
        public abstract Task<Option<Sprite>> RetrieveAvatar(FriendInfo friend, int avatarSize);
        public abstract Task<string> GetSelfUserName();
    }
}