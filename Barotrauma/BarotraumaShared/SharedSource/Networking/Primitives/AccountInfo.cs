#nullable enable
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Networking
{
    [NetworkSerialize]
    readonly struct AccountInfo : INetSerializableStruct
    {
        public static readonly AccountInfo None = new AccountInfo(Option<AccountId>.None());
        
        /// <summary>
        /// The primary ID for a given user
        /// </summary>
        public readonly Option<AccountId> AccountId;
        
        /// <summary>
        /// Other user IDs that this user might be closely tied to,
        /// such as the owner of the current copy of Barotrauma
        /// </summary>
        #warning TODO: make ImmutableArray once feature/inetserializablestruct-improvements gets merged to dev
        public readonly AccountId[] OtherMatchingIds;

        public AccountInfo(AccountId accountId, params AccountId[] otherIds) : this(Option<AccountId>.Some(accountId), otherIds) { }
        
        public AccountInfo(Option<AccountId> accountId, params AccountId[] otherIds)
        {
            AccountId = accountId;
            OtherMatchingIds = otherIds.Where(id => !accountId.ValueEquals(id)).ToArray();
        }

        public bool Matches(AccountId accountId)
            => AccountId.ValueEquals(accountId) || OtherMatchingIds.Contains(accountId);

        public override bool Equals(object? obj)
            => obj switch
            {
                AccountInfo otherInfo => AccountId == otherInfo.AccountId && OtherMatchingIds.All(otherInfo.OtherMatchingIds.Contains),
                _ => false
            };

        public override int GetHashCode()
            => AccountId.GetHashCode();

        public static bool operator ==(AccountInfo a, AccountInfo b)
            => a.Equals(b);

        public static bool operator !=(AccountInfo a, AccountInfo b) => !(a == b);
    }
}