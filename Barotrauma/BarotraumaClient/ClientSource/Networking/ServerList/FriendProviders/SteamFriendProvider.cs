#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class SteamFriendProvider : FriendProvider
    {
        private static ServerListScreen.FriendInfo FromSteamFriend(Steamworks.Friend steamFriend)
            => new ServerListScreen.FriendInfo(
                steamFriend.Name,
                new SteamId(steamFriend.Id),
                steamFriend.State switch
                {
                    Steamworks.FriendState.Offline => ServerListScreen.FriendInfo.Status.Offline,
                    Steamworks.FriendState.Invisible => ServerListScreen.FriendInfo.Status.Offline,
                    _ when steamFriend.IsPlayingThisGame => ServerListScreen.FriendInfo.Status.PlayingBarotrauma,
                    _ when steamFriend.GameInfo is { GameID: var gameId } && gameId > 0 => ServerListScreen.FriendInfo.Status.PlayingAnotherGame,
                    _ => ServerListScreen.FriendInfo.Status.NotPlaying
                })
            {
                ServerName = steamFriend.GetRichPresence("servername"),
                ConnectCommand = steamFriend.GetRichPresence("connect") is { } connectCmd
                    ? ToolBox.ParseConnectCommand(ToolBox.SplitCommand(connectCmd))
                    : Option<ConnectCommand>.None()
            };
        
        public override ServerListScreen.FriendInfo[] RetrieveFriends()
            => SteamManager.IsInitialized
                ? Steamworks.SteamFriends.GetFriends().Select(FromSteamFriend).ToArray()
                : Array.Empty<ServerListScreen.FriendInfo>();

        public override void RetrieveAvatar(ServerListScreen.FriendInfo friend, ServerListScreen.AvatarSize avatarSize)
        {
            if (!(friend.Id is SteamId steamId)) { return; }

            Func<Steamworks.SteamId, Task<Steamworks.Data.Image?>> avatarFunc = avatarSize switch
            {
                ServerListScreen.AvatarSize.Small => Steamworks.SteamFriends.GetSmallAvatarAsync,
                ServerListScreen.AvatarSize.Medium => Steamworks.SteamFriends.GetMediumAvatarAsync,
                ServerListScreen.AvatarSize.Large => Steamworks.SteamFriends.GetLargeAvatarAsync,
            };
            TaskPool.Add($"Get{avatarSize}AvatarAsync", avatarFunc(steamId.Value), task =>
            {
                if (!task.TryGetResult(out Steamworks.Data.Image? img)) { return; }
                if (!(img is { } avatarImage)) { return; }

                if (friend.Avatar.TryUnwrap(out var prevAvatar))
                {
                    prevAvatar.Remove();
                }
                
                #warning TODO: create an avatar atlas?
                var avatarTexture = new Texture2D(GameMain.Instance.GraphicsDevice, (int)avatarImage.Width, (int)avatarImage.Height);
                avatarTexture.SetData(avatarImage.Data);
                friend.Avatar = Option<Sprite>.Some(new Sprite(avatarTexture, null, null));
            });
        }

        public override string GetUserName()
            => SteamManager.GetUsername();
    }
}