#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma
{
    static class StringExtensions
    {
        public static string FallbackNullOrEmpty(this string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
        
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this string? s) => string.IsNullOrEmpty(s);
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this string? s) => string.IsNullOrWhiteSpace(s);
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this ContentPath? p) => p?.IsNullOrEmpty() ?? true;
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this ContentPath? p) => p?.IsNullOrWhiteSpace() ?? true;
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this LocalizedString? s) => s is null || string.IsNullOrEmpty(s.Value);
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this LocalizedString? s) => s is null || string.IsNullOrWhiteSpace(s.Value);
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this RichString? s) => s is null || s.NestedStr.IsNullOrEmpty();
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this RichString? s) => s is null || s.NestedStr.IsNullOrWhiteSpace();

        public static string RemoveFromEnd(this string s, string substr, StringComparison stringComparison = StringComparison.Ordinal)
            => s.EndsWith(substr, stringComparison) ? s.Substring(0, s.Length - substr.Length) : s;

        public static bool IsTrueString(this string s)
            => s.Length == 4
                && s[0] is 'T' or 't'
                && s[1] is 'R' or 'r'
                && s[2] is 'U' or 'u'
                && s[3] is 'E' or 'e';
    }
}