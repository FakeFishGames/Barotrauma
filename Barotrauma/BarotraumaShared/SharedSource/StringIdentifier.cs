using System.Collections.Generic;

namespace Barotrauma
{
    public struct StringIdentifier
    {
        static Dictionary<int, string> AllIdentifiers = new Dictionary<int, string>();
        static readonly int ProbingLimit = 30;

        public readonly string IdentifierString;
        string LowerCasedIdentifierString;

        bool HasNoHash;

        int Hash;

        public static bool operator ==(StringIdentifier A, string B)
        {
            return A.LowerCasedIdentifierString == B;
        }
        public static bool operator !=(StringIdentifier A, string B) => !(A == B);

        public static bool operator ==(string A, StringIdentifier B)
        {
            return B == A;
        }

        public static bool operator !=(string A, StringIdentifier B) => !(A == B);

        public static bool operator ==(StringIdentifier A, StringIdentifier B)
        {
            if (A.HasNoHash || B.HasNoHash)
            {
                return A.LowerCasedIdentifierString == B.LowerCasedIdentifierString;
            }

            return A.Hash == B.Hash;
        }
        public static bool operator !=(StringIdentifier A, StringIdentifier B) => !(A == B);

        public static StringIdentifier Empty = new StringIdentifier("");
        public static StringIdentifier Structure = new StringIdentifier("structure");
        public static StringIdentifier Item = new StringIdentifier("item");
        public static StringIdentifier ItemComponent = new StringIdentifier("itemcomponent");
        public static StringIdentifier Character = new StringIdentifier("character");

        public StringIdentifier(string IdentifierString)
        {
            HasNoHash = false;

            this.IdentifierString = IdentifierString.Trim();
            LowerCasedIdentifierString = this.IdentifierString.ToLowerInvariant();

            Hash = LowerCasedIdentifierString.GetHashCode();
            int NativeHash = Hash;

            string OutString = null;

            AllIdentifiers.TryGetValue(Hash, out OutString);

            if (OutString == null)
            {
                AllIdentifiers.Add(Hash, LowerCasedIdentifierString);
                return;
            }

            if (OutString == LowerCasedIdentifierString)
            {
                return;
            }

            while (AllIdentifiers.ContainsKey(Hash))
            {
                Hash++;
            }

            int HashDifference = Hash - NativeHash;
            HasNoHash = HashDifference > ProbingLimit || HashDifference < 0;
        }
    }
}