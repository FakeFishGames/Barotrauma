using System.Globalization;
using Microsoft.Xna.Framework;
using System.Linq;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    public static class StringFormatter
    {
        public static string Replace(this string s, string replacement, Func<char, bool> predicate)
        {
            var newString = new string[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char letter = s[i];
                string newLetter = letter.ToString();
                if (predicate(letter))
                {
                    newLetter = replacement;
                }
                newString[i] = newLetter;
            }
            return new string(newString.SelectMany(str => str.ToCharArray()).ToArray());
        }
        
        public static string Remove(this string s, string substring)
        {
            return s.Replace(substring, string.Empty);
        }

        public static string Remove(this string s, Func<char, bool> predicate)
        {
            return new string(s.ToCharArray().Where(c => !predicate(c)).ToArray());
        }

        public static string RemoveWhitespace(this string s)
        {
            return s.Remove(c => char.IsWhiteSpace(c));
        }

        public static string FormatSingleDecimal(this float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        public static string FormatDoubleDecimal(this float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        public static string FormatZeroDecimal(this float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        public static string Format(this float value, int decimalCount)
        {
            return value.ToString($"F{decimalCount.ToString()}", CultureInfo.InvariantCulture);
        }

        public static string FormatSingleDecimal(this Vector2 value)
        {
            return $"({value.X.FormatSingleDecimal()}, {value.Y.FormatSingleDecimal()})";
        }

        public static string FormatDoubleDecimal(this Vector2 value)
        {
            return $"({value.X.FormatDoubleDecimal()}, {value.Y.FormatDoubleDecimal()})";
        }

        public static string FormatZeroDecimal(this Vector2 value)
        {
            return $"({value.X.FormatZeroDecimal()}, {value.Y.FormatZeroDecimal()})";
        }

        public static string Format(this Vector2 value, int decimalCount)
        {
            return $"({value.X.Format(decimalCount)}, {value.Y.Format(decimalCount)})";
        }

        /// <summary>
        /// Capitalises the first letter (invariant) and forces the rest to lower case (invariant).
        /// </summary>
        public static string CapitaliseFirstInvariant(this string s)
        {
            if (string.IsNullOrEmpty(s)) { return string.Empty; }
            return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1, s.Length - 1).ToLowerInvariant();
        }

        /// <summary>
        /// Adds spaces into a CamelCase string.
        /// </summary>
        public static string FormatCamelCaseWithSpaces(this string str)
        {
            return new string(InsertSpacesBeforeCaps(str).ToArray());
            IEnumerable<char> InsertSpacesBeforeCaps(IEnumerable<char> input)
            {
                int i = 0;
                int lastChar = input.Count() - 1;
                foreach (char c in input)
                {
                    if (char.IsUpper(c) && i > 0)
                    {
                        yield return ' ';
                    }

                    yield return c;
                    i++;
                }
            }
        }

        public static ICollection<string> ParseCommaSeparatedStringToCollection(string input, ICollection<string> texts = null, bool convertToLowerInvariant = true)
        {
            if (texts == null)
            {
                texts = new HashSet<string>();
            }
            else
            {
                texts.Clear();
            }
            if (!string.IsNullOrWhiteSpace(input))
            {
                foreach (string value in input.Split(','))
                {
                    if (string.IsNullOrWhiteSpace(value)) { continue; }
                    if (convertToLowerInvariant)
                    {
                        texts.Add(value.ToLowerInvariant());
                    }
                    else
                    {
                        texts.Add(value);
                    }
                }
            }
            return texts;
        }

        public static ICollection<string> ParseSeparatedStringToCollection(string input, string[] separators, ICollection<string> texts = null, bool convertToLowerInvariant = true)
        {
            if (texts == null)
            {
                texts = new HashSet<string>();
            }
            else
            {
                texts.Clear();
            }
            if (!string.IsNullOrWhiteSpace(input))
            {
                foreach (string value in input.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (convertToLowerInvariant)
                    {
                        texts.Add(value.ToLowerInvariant());
                    }
                    else
                    {
                        texts.Add(value);
                    }
                }
            }
            return texts;
        }
    }
}
