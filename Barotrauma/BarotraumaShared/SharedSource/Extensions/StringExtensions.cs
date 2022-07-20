#nullable enable
using System;

namespace Barotrauma
{
    public static class StringExtensions
    {
        public static string FallbackNullOrEmpty(this string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
        
        public static bool IsNullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
        public static bool IsNullOrWhiteSpace(this string? s) => string.IsNullOrWhiteSpace(s);
        public static bool IsNullOrEmpty(this ContentPath? p) => p?.IsNullOrEmpty() ?? true;
        public static bool IsNullOrWhiteSpace(this ContentPath? p) => p?.IsNullOrWhiteSpace() ?? true;
        public static bool IsNullOrEmpty(this LocalizedString? s) => s is null || string.IsNullOrEmpty(s.Value);
        public static bool IsNullOrWhiteSpace(this LocalizedString? s) => s is null || string.IsNullOrWhiteSpace(s.Value);
        public static bool IsNullOrEmpty(this RichString? s) => s is null || s.NestedStr.IsNullOrEmpty();
        public static bool IsNullOrWhiteSpace(this RichString? s) => s is null || s.NestedStr.IsNullOrWhiteSpace();

        public static string RemoveFromEnd(this string s, string substr, StringComparison stringComparison = StringComparison.Ordinal)
            => s.EndsWith(substr, stringComparison) ? s.Substring(0, s.Length - substr.Length) : s;
    }
}