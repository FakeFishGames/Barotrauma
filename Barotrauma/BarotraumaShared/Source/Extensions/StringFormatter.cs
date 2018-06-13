using System.Globalization;

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
    }
}
