#nullable enable

using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal readonly struct CircuitBoxLabel
    {
        public LocalizedString Value { get; }

        public Vector2 Size { get; }

        public GUIFont Font { get; }

        public CircuitBoxLabel(LocalizedString value, GUIFont font)
        {
            Value = value;
            Font = font;
            Size = font.MeasureString(font.ForceUpperCase ? value.Value.ToUpperInvariant() : value.Value);
        }
    }
}