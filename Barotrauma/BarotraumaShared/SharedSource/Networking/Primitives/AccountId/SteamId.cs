#nullable enable
using System;

namespace Barotrauma.Networking
{
    sealed class SteamId : AccountId
    {
        public readonly UInt64 Value;
        
        public override string StringRepresentation { get; }

        /// Based on information found here: https://developer.valvesoftware.com/wiki/SteamID
        /// ------------------------------------------------------------------------------------
        /// A SteamID is a 64-bit value (16 hexadecimal digits) that's broken up as follows:
        ///
        ///                    | a  | b |   c   |    d     |
        /// Most significant - | 01 | 1 | 00001 | 0546779D | - Least significant
        ///
        /// a) 8 bits representing the universe the account belongs to.
        /// b) 4 bits representing the type of account. Typically 1.
        /// c) 20 bits representing the instance of the account. Typically 1.
        /// d) 32 bits representing the account number.
        ///
        /// The account number is additionally broken up as follows:
        ///
        ///                    |                e                | f |
        /// Most significant - | 0000010101000110011101111001110 | 1 | - Least significant
        ///
        /// e) These are the 31 most significant bits of the account number.
        /// f) This is the least significant bit of the account number, discriminated under the name Y for some reason.
        ///
        /// Barotrauma supports two textual representations of SteamIDs:
        /// 1. STEAM40: Given this name as it represents 40 of the 64 bits in the ID. The account type and instance both
        ///             have an implied value of 1. The format is "STEAM_{universe}:{Y}:{restOfAccountNumber}".
        /// 2. STEAM64: If STEAM40 does not suffice to represent an ID (i.e. the account type or instance were different
        ///             from 1), we use "STEAM64_{fullId}" where fullId is the 64-bit decimal representation of the full
        ///             ID.

        private const string steam64Prefix = "STEAM64_";
        private const string steam40Prefix = "STEAM_";

        private const UInt64 usualAccountInstance = 1;
        private const UInt64 usualAccountType = 1;

        static UInt64 ExtractBits(UInt64 id, int offset, int numberOfBits)
            => (id >> offset) & ((1ul << numberOfBits) - 1ul);

        static UInt64 ExtractY(UInt64 id)
            => ExtractBits(id, offset: 0, numberOfBits: 1);
        static UInt64 ExtractAccountNumberRemainder(UInt64 id)
            => ExtractBits(id, offset: 1, numberOfBits: 31);
        static UInt64 ExtractAccountInstance(UInt64 id)
            => ExtractBits(id, offset: 32, numberOfBits: 20);
        static UInt64 ExtractAccountType(UInt64 id)
            => ExtractBits(id, offset: 52, numberOfBits: 4);
        static UInt64 ExtractUniverse(UInt64 id)
            => ExtractBits(id, offset: 56, numberOfBits: 8);

        public SteamId(UInt64 value)
        {
            Value = value;

            if (ExtractAccountInstance(Value) == usualAccountInstance
                && ExtractAccountType(Value) == usualAccountType)
            {
                UInt64 y = ExtractY(Value);
                UInt64 accountNumberRemainder = ExtractAccountNumberRemainder(Value);
                UInt64 universe = ExtractUniverse(Value);
                StringRepresentation = $"{steam40Prefix}{universe}:{y}:{accountNumberRemainder}";
            }
            else
            {
                StringRepresentation = $"{steam64Prefix}{Value}";
            }
        }

        public override string ToString() => StringRepresentation;

        public new static Option<SteamId> Parse(string str)
        {
            if (str.IsNullOrWhiteSpace()) { return Option<SteamId>.None(); }

            if (str.StartsWith(steam64Prefix, StringComparison.InvariantCultureIgnoreCase)) { str = str[steam64Prefix.Length..]; }
            if (UInt64.TryParse(str, out UInt64 retVal) && ExtractAccountInstance(retVal) > 0)
            {
                return Option<SteamId>.Some(new SteamId(retVal));
            }
            
            if (!str.StartsWith(steam40Prefix, StringComparison.InvariantCultureIgnoreCase)) { return Option<SteamId>.None(); }
            string[] split = str[steam40Prefix.Length..].Split(':');
            if (split.Length != 3) { return Option<SteamId>.None(); }

            if (!UInt64.TryParse(split[0], out UInt64 universe)) { return Option<SteamId>.None(); }
            if (!UInt64.TryParse(split[1], out UInt64 y)) { return Option<SteamId>.None(); }
            if (!UInt64.TryParse(split[2], out UInt64 accountNumber)) { return Option<SteamId>.None(); }

            return Option<SteamId>.Some(
                new SteamId((universe << 56)
                            | usualAccountType << 52
                            | usualAccountInstance << 32
                            | (accountNumber << 1)
                            | y));
        }

        public override bool Equals(object? obj)
            => obj switch
            {
                SteamId otherId => this == otherId,
                _ => false
            };

        public override int GetHashCode()
            => Value.GetHashCode();

        public static bool operator ==(SteamId a, SteamId b)
            => a.Value == b.Value;

        public static bool operator !=(SteamId a, SteamId b)
            => !(a == b);
    }
}
