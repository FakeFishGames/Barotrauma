using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public readonly record struct EgsFriend(
        string DisplayName,
        EpicAccountId EpicAccountId,
        FriendStatus Status,
        string ConnectCommand,
        string ServerName);
}