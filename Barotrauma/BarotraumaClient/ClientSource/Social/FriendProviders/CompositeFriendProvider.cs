using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma;

sealed class CompositeFriendProvider : FriendProvider
{
    private readonly ImmutableArray<FriendProvider> providers;

    public CompositeFriendProvider(params FriendProvider[] providers)
    {
        this.providers = providers.ToImmutableArray();
    }

    public override async Task<Option<FriendInfo>> RetrieveFriend(AccountId id)
    {
        return (await Task.WhenAll(providers
                .Select(p => p.RetrieveFriend(id))))
            .NotNone().FirstOrNone();
    }

    public override async Task<ImmutableArray<FriendInfo>> RetrieveFriends()
    {
        var friends = await Task.WhenAll(providers.Select(p => p.RetrieveFriends()));
        return friends.SelectMany(a => a).ToImmutableArray();
    }

    public override async Task<Option<Sprite>> RetrieveAvatar(FriendInfo friend, int avatarSize)
    {
        var subTasks = await Task.WhenAll(providers.Select(p => p.RetrieveAvatar(friend, avatarSize)));
        return subTasks.FirstOrDefault(t => t.IsSome());
    }

    public override async Task<string> GetSelfUserName()
    {
        foreach (var provider in providers)
        {
            string userName = await provider.GetSelfUserName();
            if (userName is { Length: > 0 }) { return userName; }
        }
        return "";
    }
}