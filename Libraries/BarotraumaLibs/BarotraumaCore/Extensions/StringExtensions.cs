#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Barotrauma
{
    public static class StringExtensions
    {
        [return: NotNullIfNotNull("fallback")]
        public static string? FallbackNullOrEmpty(this string? s, string? fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this string? s) => string.IsNullOrEmpty(s);
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this string? s) => string.IsNullOrWhiteSpace(s);
        public static string RemoveFromEnd(this string s, string substr, StringComparison stringComparison = StringComparison.Ordinal)
            => s.EndsWith(substr, stringComparison) ? s.Substring(0, s.Length - substr.Length) : s;

        public static bool IsTrueString(this string s)
            => s.Length == 4
               && s[0] is 'T' or 't'
               && s[1] is 'R' or 'r'
               && s[2] is 'U' or 'u'
               && s[3] is 'E' or 'e';

        public static string JoinEscaped(this IEnumerable<string> strings, char joiner)
        {
            return string.Join(
                joiner,
                strings.Select(s => s
                    .Replace("\\", "\\\\")
                    .Replace(joiner.ToString(), $"\\{joiner}")));
        }

        public static IReadOnlyList<string> SplitEscaped(this string str, char joiner)
        {
            bool isEscape(int i)
            {
                return i >= 0 && str[i] == '\\' && !isEscape(i - 1);
            }

            var retVal = new List<string>();
            int lastSplitIndex = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == joiner && !isEscape(i - 1))
                {
                    retVal.Add(str[lastSplitIndex..i]);
                    lastSplitIndex = i + 1;
                }
                if (isEscape(i) && (i >= str.Length - 1 || (str[i+1] != joiner && str[i+1] != '\\')))
                {
                    throw new ArgumentOutOfRangeException($"The string \"{str}\" could not have been produced by a call to {nameof(JoinEscaped)} with joiner '{joiner}'");
                }
            }
            retVal.Add(str[lastSplitIndex..]);
            for (int i = 0; i < retVal.Count; i++)
            {
                retVal[i] = retVal[i]
                    .Replace($"\\{joiner}", joiner.ToString())
                    .Replace("\\\\", "\\");
            }
            return retVal;
        }
    }
}