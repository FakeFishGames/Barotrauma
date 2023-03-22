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
