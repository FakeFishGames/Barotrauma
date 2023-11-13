using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Steamworks
{
    public struct Clan
    {
        public SteamId Id;

        public Clan(SteamId id)
        {
            Id = id;
        }

        public string? Name => SteamFriends.Internal?.GetClanName(Id);

        public string? Tag => SteamFriends.Internal?.GetClanTag(Id);

        public int ChatMemberCount => SteamFriends.Internal?.GetClanChatMemberCount(Id) ?? 0;

        public Friend Owner => new Friend(SteamFriends.Internal?.GetClanOwner(Id) ?? 0);

        public bool Public => SteamFriends.Internal != null && SteamFriends.Internal.IsClanPublic(Id);

        /// <summary>
        /// Is the clan an official game group?
        /// </summary>
        public bool Official => SteamFriends.Internal != null && SteamFriends.Internal.IsClanOfficialGameGroup(Id);

        /// <summary>
        /// Asynchronously fetches the officer list for a given clan
        /// </summary>
        /// <returns>Whether the request was successful or not</returns>
        public async Task<bool> RequestOfficerList()
        {
            if (SteamFriends.Internal is null) { return false; }
            var req = await SteamFriends.Internal.RequestClanOfficerList(Id);
            return req.HasValue && req.Value.Success != 0x0;
        }

        public IEnumerable<Friend> GetOfficers()
        {
            if (SteamFriends.Internal is null) { yield break; }
            var officerCount = SteamFriends.Internal.GetClanOfficerCount(Id);
            for (int i = 0; i < officerCount; i++)
            {
                if (SteamFriends.Internal is null) { yield break; }
                yield return new Friend(SteamFriends.Internal.GetClanOfficerByIndex(Id, i));
            }
        }
    }
}
