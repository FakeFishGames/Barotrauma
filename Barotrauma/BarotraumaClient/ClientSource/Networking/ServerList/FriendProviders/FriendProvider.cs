#nullable enable

namespace Barotrauma
{
    abstract class FriendProvider
    {
        public abstract ServerListScreen.FriendInfo[] RetrieveFriends();
        public abstract void RetrieveAvatar(ServerListScreen.FriendInfo friend, ServerListScreen.AvatarSize avatarSize);
        public abstract string GetUserName();
    }
}