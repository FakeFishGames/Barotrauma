#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Barotrauma
{
    // Identifier struct to eliminate case-sensitive comparisons
    public readonly struct Identifier : IComparable, IEquatable<Identifier>
    {
        public readonly static Identifier Empty = default;

        private readonly static int emptyHash = "".GetHashCode(StringComparison.OrdinalIgnoreCase);

        private readonly string? value;
        private readonly Lazy<int>? hashCode;

        public string Value => value ?? "";
        public int HashCode => hashCode?.Value ?? emptyHash;

        public Identifier(string? str)
        {
            value = str;
            hashCode = new Lazy<int>(() => (str ?? "").GetHashCode(StringComparison.OrdinalIgnoreCase));
        }

        public bool IsEmpty => Value.IsNullOrEmpty();

        public Identifier IfEmpty(in Identifier id)
            => IsEmpty ? id : this;

        public Identifier Replace(in Identifier subStr, in Identifier newStr)
            => Replace(subStr.Value ?? "", newStr.Value ?? "");
        
        public Identifier Replace(string subStr, string newStr)
            => (Value?.Replace(subStr, newStr, StringComparison.OrdinalIgnoreCase)).ToIdentifier();

        public Identifier Remove(Identifier subStr)
            => Remove(subStr.Value ?? "");

        public Identifier Remove(string subStr)
            => (Value?.Remove(subStr, StringComparison.OrdinalIgnoreCase)).ToIdentifier();

        public override bool Equals(object? obj) =>
            obj switch
            {
                Identifier i => this == i,
                string s => this == s,
                _ => base.Equals(obj)
            };

        public bool StartsWith(string str) => Value?.StartsWith(str, StringComparison.OrdinalIgnoreCase) ?? str.IsNullOrEmpty();

        public bool StartsWith(Identifier id) => StartsWith(id.Value ?? "");

        public bool EndsWith(string str) => Value?.EndsWith(str, StringComparison.OrdinalIgnoreCase) ?? str.IsNullOrEmpty();

        public bool EndsWith(Identifier id) => EndsWith(id.Value ?? "");

        public Identifier AppendIfMissing(string suffix)
            => EndsWith(suffix) ? this : $"{this}{suffix}".ToIdentifier();

        public Identifier RemoveFromEnd(string suffix)
            => (Value?.RemoveFromEnd(suffix, StringComparison.OrdinalIgnoreCase)).ToIdentifier();

        public bool Contains(string str) => Value?.Contains(str, StringComparison.OrdinalIgnoreCase) ?? str.IsNullOrEmpty();

        public bool Contains(in Identifier id) => Contains(id.Value ?? "");

        public override string ToString() => Value ?? "";

        public override int GetHashCode() => HashCode;

        public int CompareTo(object? obj)
        {
            return string.Compare(Value, obj?.ToString() ?? "", StringComparison.InvariantCultureIgnoreCase);
        }

        public bool Equals([AllowNull] Identifier other)
        {
            return this == other;
        }

        private static bool StringEquality(string? a, string? b)
            => (a.IsNullOrEmpty() && b.IsNullOrEmpty()) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        
        public static bool operator ==(in Identifier a, in Identifier b) =>
            StringEquality(a.Value, b.Value);

        public static bool operator !=(in Identifier a, in Identifier b) =>
            !(a == b);

        public static bool operator ==(in Identifier identifier, string? str) =>
            StringEquality(identifier.Value, str);

        public static bool operator !=(in Identifier identifier, string? str) =>
            !(identifier == str);

        public static bool operator ==(string? str, in Identifier identifier) =>
            identifier == str;

        public static bool operator !=(string? str, in Identifier identifier) =>
            !(identifier == str);

        public static bool operator ==(in Identifier? a, in Identifier? b) =>
            StringEquality(a?.Value, b?.Value);

        public static bool operator !=(in Identifier? a, in Identifier? b) =>
            !(a == b);

        public static bool operator ==(in Identifier? a, string? b) =>
            StringEquality(a?.Value, b);

        public static bool operator !=(in Identifier? a, string? b) =>
            !(a == b);

        public static bool operator ==(string str, in Identifier? identifier) =>
            identifier == str;

        public static bool operator !=(string str, in Identifier? identifier) =>
            !(identifier == str);

        internal int IndexOf(char c) => Value.IndexOf(c);

        internal Identifier this[Range range] => Value[range].ToIdentifier();
        internal Char this[int i] => Value[i];
    }

    public static class IdentifierExtensions
    {
        public static IEnumerable<Identifier> ToIdentifiers(this IEnumerable<string> strings)
        {
            foreach (string s in strings)
            {
                if (string.IsNullOrEmpty(s)) { continue; }
                yield return new Identifier(s);
            }
        }

        public static Identifier[] ToIdentifiers(this string[] strings)
            => ((IEnumerable<string>)strings).ToIdentifiers().ToArray();

        public static Identifier ToIdentifier(this string? s)
        {
            return new Identifier(s);
        }

        public static Identifier ToIdentifier<T>(this T t) where T: notnull
        {
            return t.ToString().ToIdentifier();
        }

        public static bool Contains(this ISet<Identifier> set, string identifier)
        {
            return set.Contains(identifier.ToIdentifier());
        }

        public static bool ContainsKey<T>(this IReadOnlyDictionary<Identifier, T> dictionary, string key)
        {
            return dictionary.ContainsKey(key.ToIdentifier());
        }
    }
}
