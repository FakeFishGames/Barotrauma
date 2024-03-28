#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    sealed class SteamFriendProvider : FriendProvider
    {
        private FriendInfo FromSteamFriend(Steamworks.Friend steamFriend)
            => new FriendInfo(
                name: steamFriend.Name ?? "",
                id: new SteamId(steamFriend.Id),
                status: steamFriend.State switch
                {
                    Steamworks.FriendState.Offline => FriendStatus.Offline,
                    Steamworks.FriendState.Invisible => FriendStatus.Offline,
                    _ when steamFriend.IsPlayingThisGame => FriendStatus.PlayingBarotrauma,
                    _ when steamFriend.GameInfo is { GameID: > 0 } => FriendStatus.PlayingAnotherGame,
                    _ => FriendStatus.NotPlaying
                },
                serverName: steamFriend.GetRichPresence("servername") ?? "",
                connectCommand: steamFriend.GetRichPresence("connect") is { } connectCmd
                    ? ConnectCommand.Parse(ToolBox.SplitCommand(connectCmd))
                    : Option.None,
                this);

        public override Task<Option<FriendInfo>> RetrieveFriend(AccountId id)
            => Task.FromResult(id is SteamId steamId
                ? Option.Some(FromSteamFriend(new Steamworks.Friend(steamId.Value)))
                : Option.None);

        public override Task<ImmutableArray<FriendInfo>> RetrieveFriends()
            => Task.FromResult(SteamManager.IsInitialized
                ? Steamworks.SteamFriends.GetFriends().Select(FromSteamFriend).ToImmutableArray()
                : ImmutableArray<FriendInfo>.Empty);

        public override async Task<Option<Sprite>> RetrieveAvatar(FriendInfo friend, int avatarSize)
        {
            if (friend.Id is not SteamId steamId) { return Option.None; }

            Func<Steamworks.SteamId, Task<Steamworks.Data.Image?>> avatarFunc = avatarSize switch
            {
                <= 24 => Steamworks.SteamFriends.GetSmallAvatarAsync,
                <= 48 => Steamworks.SteamFriends.GetMediumAvatarAsync,
                _ => Steamworks.SteamFriends.GetLargeAvatarAsync
            };

            var img = await avatarFunc(steamId.Value).ToOptionTask();
            if (!img.TryUnwrap(out var avatarImage)) { return Option.None; }

            if (friend.Avatar.TryUnwrap(out var prevAvatar))
            {
                prevAvatar.Remove();
            }

            var avatarTexture = new Texture2D(GameMain.Instance.GraphicsDevice, (int)avatarImage.Width, (int)avatarImage.Height);
            avatarTexture.SetData(avatarImage.Data);
            return Option.Some(new Sprite(texture: avatarTexture, sourceRectangle: null, newOffset: null));
        }

        public override Task<string> GetSelfUserName()
            => Task.FromResult(SteamManager.GetUsername());
    }
}