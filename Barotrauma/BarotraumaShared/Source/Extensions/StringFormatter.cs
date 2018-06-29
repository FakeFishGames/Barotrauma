using System.Globalization;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class StringFormatter
    {
        /// <summary>
        /// Formats the value with one decimal.
        /// </summary>
        public static string FormatAsSingleDecimal(this float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats the value with two decimals.
        /// </summary>
        public static string FormatAsDoubleDecimal(this float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts first to int, then to string.
        /// </summary>
        public static string FormatAsInt(this float value)
        {
            int v = (int)value;
            return v.ToString();
        }

        /// <summary>
        /// Formats the value with one decimal.
        /// </summary>
        public static string FormatAsSingleDecimal(this Vector2 value)
        {
            return $"({value.X.FormatAsSingleDecimal()}, {value.Y.FormatAsSingleDecimal()})";
        }

        /// <summary>
        /// Formats the value with two decimals.
        /// </summary>
        public static string FormatAsDoubleDecimal(this Vector2 value)
        {
            return $"({value.X.FormatAsDoubleDecimal()}, {value.Y.FormatAsDoubleDecimal()})";
        }

        /// <summary>
        /// Formats the value with no decimals.
        /// </summary>
        public static string FormatAsZeroDecimal(this Vector2 value)
        {
            return $"({value.X.FormatAsInt()}, {value.Y.FormatAsInt()})";
        }
    }
}
