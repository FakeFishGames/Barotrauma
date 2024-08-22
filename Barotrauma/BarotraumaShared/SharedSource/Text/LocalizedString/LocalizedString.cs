#nullable enable
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    public abstract class LocalizedString : IComparable
    {
        protected enum LoadedSuccessfully
        {
            Unknown,
            No,
            Yes
        }
        
        public LanguageIdentifier Language { get; private set; } = LanguageIdentifier.None;
        private int languageVersion = 0;

        protected string cachedValue = "";
        public string Value
        {
            get
            {
                if (MustRetrieveValue()) { RetrieveValue(); }
                return cachedValue;
            }
        }

        public int Length => Value.Length;

        public abstract bool Loaded { get; }

        protected void UpdateLanguage()
        {
            Language = GameSettings.CurrentConfig.Language;
            languageVersion = TextManager.LanguageVersion;
        }
        
        protected virtual bool MustRetrieveValue() //this can't be called on other LocalizedStrings by derived classes
        {
            return Language != GameSettings.CurrentConfig.Language || languageVersion != TextManager.LanguageVersion;
        }

        protected static bool MustRetrieveValue(LocalizedString str) //this can be called by derived classes
        {
            return str.MustRetrieveValue();
        }

        public abstract void RetrieveValue();

        public static readonly RawLString EmptyString = new RawLString("");
        public static implicit operator LocalizedString(string value)
            => !value.IsNullOrEmpty()
                ? new RawLString(value)
                : EmptyString;
        public static implicit operator LocalizedString(char value) => new RawLString(value.ToString());

        public static LocalizedString operator+(LocalizedString left, LocalizedString right)
        {
            // If either side of the concatenation is an empty string,
            // return the other string instead of creating a new object
            if (left is RawLString { Value.Length: 0 }) { return right; }
            if (right is RawLString { Value.Length: 0 }) { return left; }

            return new ConcatLString(left, right);
        }

        public static LocalizedString operator+(LocalizedString left, object right) => left + (right.ToString() ?? "");
        public static LocalizedString operator+(object left, LocalizedString right) => (left.ToString() ?? "") + right;

        public static bool operator==(LocalizedString? left, LocalizedString? right)
        {
            return left?.Value == right?.Value;
        }

        public static bool operator!=(LocalizedString? left, LocalizedString? right)
        {
            return !(left == right);
        }

        public override string ToString()
        {
            return Value;
        }

        public bool Contains(string subStr, StringComparison comparison = StringComparison.Ordinal)
        {
            return !Value.IsNullOrEmpty() && Value.Contains(subStr, comparison);
        }

        public bool Contains(char chr, StringComparison comparison = StringComparison.Ordinal)
        {
            return Value.Contains(chr, comparison);
        }

        public virtual LocalizedString ToUpper()
        {
            return new UpperLString(this);
        }

        public static LocalizedString Join(string separator, params LocalizedString[] subStrs)
        {
            return Join(separator, (IEnumerable<LocalizedString>)subStrs);
        }

        public static LocalizedString Join(string separator, IEnumerable<LocalizedString> subStrs)
        {
            return new JoinLString(separator, subStrs);
        }

        /// <summary>
        /// Use this text instead if the original text cannot be found.
        /// </summary>
        /// <param name="fallback">The text to use as a fallback</param>
        /// <param name="useDefaultLanguageIfFound">Should the default language (English) text be used instead of this fallback if there is a text available in the default language?</param>
        public LocalizedString Fallback(LocalizedString fallback, bool useDefaultLanguageIfFound = true)
        {
            return new FallbackLString(this, fallback, useDefaultLanguageIfFound);
        }

        public IReadOnlyList<LocalizedString> Split(params char[] separators)
        {
            var splitter = new LStringSplitter(this, separators);
            return splitter.Substrings;
        }

        public LocalizedString Replace(Identifier find, LocalizedString replace, StringComparison stringComparison = StringComparison.Ordinal)
        {
            return new ReplaceLString(this, stringComparison, (find, replace));
        }

        public LocalizedString Replace(string find, LocalizedString replace, StringComparison stringComparison = StringComparison.Ordinal)
        {
            return new ReplaceLString(this, stringComparison, (find.ToIdentifier(), replace));
        }

        public LocalizedString Replace(LocalizedString find, LocalizedString replace,
            StringComparison stringComparison = StringComparison.Ordinal)
        {
            return new ReplaceLString(this, stringComparison, (find, replace));
        }

        public LocalizedString TrimStart()
        {
            return new TrimLString(this, TrimLString.Mode.Start);
        }
        
        public LocalizedString TrimEnd()
        {
            return new TrimLString(this, TrimLString.Mode.End);
        }

        public LocalizedString ToLower()
        {
            return new LowerLString(this);
        }

        public override bool Equals(object? obj)
        {
            if (obj is LocalizedString lStr) { return Equals(lStr, StringComparison.Ordinal); }
            if (obj is string str) { return Equals(str, StringComparison.Ordinal); }
            return base.Equals(obj);
        }

        public bool Equals(LocalizedString other, StringComparison comparison = StringComparison.Ordinal)
        {
            return Equals(other.Value, comparison);
        }

        public bool Equals(string other, StringComparison comparison = StringComparison.Ordinal)
        {
            return string.Equals(Value, other, comparison);
        }

        public bool StartsWith(LocalizedString other, StringComparison comparison = StringComparison.Ordinal)
        {
            return StartsWith(other.Value, comparison);
        }

        public bool StartsWith(string other, StringComparison comparison = StringComparison.Ordinal)
        {
            return Value.StartsWith(other, comparison);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public int CompareTo(object? obj)
        {
            return Value.CompareTo(obj?.ToString() ?? "");
        }
    }
}