#nullable enable

namespace Barotrauma
{
    internal readonly struct NetLimitedString
    {
        public readonly string Value;
        public const int MaxLength = 255;

        public static readonly NetLimitedString Empty = new(string.Empty);

        public NetLimitedString(string value)
        {
            Value = value.Length > MaxLength
                ? value[..MaxLength]
                : value;
        }

        public override string ToString()
            => Value;
    }
}