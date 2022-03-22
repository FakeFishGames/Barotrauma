using Microsoft.Xna.Framework;

namespace Barotrauma.Extensions
{
    public static class ColorExtensions
    {
        public static Color Multiply(this Color color, float value, bool onlyAlpha = false)
        {
            return onlyAlpha ?
                new Color(color.R, color.G,  color.B, (byte)(color.A * value)) :            
                new Color((byte)(color.R * value), (byte)(color.G * value), (byte)(color.B * value), (byte)(color.A * value));
        }

        public static Color Multiply(this Color thisColor, Color color)
        {
            return new Color((byte)(thisColor.R * color.R / 255f), (byte)(thisColor.G * color.G / 255f), (byte)(thisColor.B * color.B / 255f), (byte)(thisColor.A * color.A / 255f));
        }

        public static Color Opaque(this Color color)
        {
            return new Color(color.R, color.G, color.B, (byte)255);
        }
    }
}
