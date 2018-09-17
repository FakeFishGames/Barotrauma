using System.Globalization;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class StringFormatter
    {
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
        public static string CapitaliseFirstInvariant(this string s) => s.Substring(0, 1).ToUpperInvariant() + s.Substring(1, s.Length - 1).ToLowerInvariant();
    }
}
