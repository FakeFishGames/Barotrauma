#nullable enable
using System;
using Barotrauma.Networking;

namespace Barotrauma;

sealed class FriendInfo : IDisposable
{
    public readonly string Name;
    public readonly AccountId Id;

    public readonly FriendStatus CurrentStatus;
    public readonly string ServerName;
    public readonly Option<ConnectCommand> ConnectCommand;

    public readonly FriendProvider Provider;
    public Option<Sprite> Avatar { get; set; }

    public bool IsInServer
        => CurrentStatus == FriendStatus.PlayingBarotrauma && ConnectCommand.IsSome();

    public bool IsOnline
        => CurrentStatus != FriendStatus.Offline;

    public LocalizedString StatusText
        => CurrentStatus switch
        {
            FriendStatus.Offline => "",
            _ when ConnectCommand.IsSome()
                => TextManager.GetWithVariable("FriendPlayingOnServer", "[servername]", ServerName),
            _ => TextManager.Get($"Friend{CurrentStatus}")
        };

    public FriendInfo(string name, AccountId id, FriendStatus status, string serverName, Option<ConnectCommand> connectCommand, FriendProvider provider)
    {
        Name = name;
        Id = id;
        CurrentStatus = status;
        ServerName = serverName;
        ConnectCommand = connectCommand;
        Provider = provider;
        Avatar = Option.None;
    }

    public void RetrieveOrInheritAvatar(Option<Sprite> inheritableAvatar, int size)
    {
        if (Avatar.IsSome()) { return; }

        if (inheritableAvatar.IsSome())
        {
            Avatar = inheritableAvatar;
            return;
        }

        TaskPool.Add(
            "RetrieveAvatar",
            Provider.RetrieveAvatar(this, size),
            t =>
            {
                if (!t.TryGetResult(out Option<Sprite> spr)) { return; }
                Avatar = Avatar.Fallback(spr);
            });
    }

    public void Dispose()
    {
        if (Avatar.TryUnwrap(out var avatar))
        {
            avatar.Remove();
        }
        Avatar = Option.None;
    }
}