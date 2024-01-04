#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    #warning TODO: turn this into INetSerializableStruct
    partial class BannedPlayer
    {
        public readonly string Name;
        public readonly Either<Address, AccountId> AddressOrAccountId;

        public readonly string Reason;
        public Option<SerializableDateTime> ExpirationTime;
        public readonly UInt32 UniqueIdentifier;

        public bool MatchesClient(Client client)
        {
            if (client == null) { return false; }
            if (AddressOrAccountId.TryGet(out AccountId bannedAccountId) && client.AccountId.TryUnwrap(out AccountId? accountId))
            {
                return bannedAccountId.Equals(accountId);
            }
            return false;
        }
    }

    partial class BanList
    {
        private readonly List<BannedPlayer> bannedPlayers;

        public IReadOnlyList<BannedPlayer> BannedPlayers => bannedPlayers;
        
        public IEnumerable<string> BannedNames
        {
            get { return bannedPlayers.Select(bp => bp.Name); }
        }

        public IEnumerable<Either<Address, AccountId>> BannedAddresses
        {
            get { return bannedPlayers.Select(bp => bp.AddressOrAccountId); }
        }

        partial void InitProjectSpecific();


        public BanList()
        {
            bannedPlayers = new List<BannedPlayer>();
            InitProjectSpecific();
        }
    }
}
