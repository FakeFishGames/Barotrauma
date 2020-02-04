using Microsoft.Xna.Framework;
using System;

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

        public static Color Opaque(this Color color)
        {
            return new Color(color.R, color.G, color.B, (byte)255);
        }
    }
}
