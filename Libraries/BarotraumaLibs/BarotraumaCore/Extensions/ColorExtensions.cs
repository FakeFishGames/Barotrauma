using Microsoft.Xna.Framework;

namespace Barotrauma.Extensions
{
    public static class ColorExtensions
    {
        public static Color Multiply(this Color color, float value, bool onlyAlpha = false)
        {
            return onlyAlpha ?
                new Color(color.R, color.G,  color.B, MultiplyComponent(color.A, value)) :            
                new Color(MultiplyComponent(color.R, value), MultiplyComponent(color.G, value), MultiplyComponent(color.B, value), MultiplyComponent(color.A, value));

            static byte MultiplyComponent(float colorComponent, float multiplier)
            {
                return (byte)MathHelper.Clamp(colorComponent * multiplier, 0.0f, 255.0f);
            }
        }

        public static Color Multiply(this Color thisColor, Color color)
        {
            return new Color((byte)(thisColor.R * color.R / 255f), (byte)(thisColor.G * color.G / 255f), (byte)(thisColor.B * color.B / 255f), (byte)(thisColor.A * color.A / 255f));
        }

        public static Color Opaque(this Color color)
        {
            return new Color(color.R, color.G, color.B, (byte)255);
        }

        private static bool IsFirstColorChannelDominant(byte first, byte second, byte third, float minimumRatio = 2)
            => first > second * minimumRatio && first > third * minimumRatio;

        /// <summary>
        /// Is the value of the red channel at least 'minimumRatio' larger than the blue and green
        /// </summary>
        public static bool IsRedDominant(Color color, float minimumRatio = 2, byte minimumAlpha = 0)
            => color.A > minimumAlpha &&
                IsFirstColorChannelDominant(
                    first: color.R,
                    color.G, color.B, minimumRatio);

        /// <summary>
        /// Is the value of the green channel at least 'minimumRatio' larger than the red and blue
        /// </summary>
        public static bool IsGreenDominant(Color color, float minimumRatio = 2, byte minimumAlpha = 0)
            => color.A > minimumAlpha &&
                IsFirstColorChannelDominant(
                    first: color.G,
                    color.R, color.B, minimumRatio);

        /// <summary>
        /// Is the value of the blue channel at least 'minimumRatio' larger than the red and green
        /// </summary>
        public static bool IsBlueDominant(Color color, float minimumRatio = 2, byte minimumAlpha = 0)
            => color.A > minimumAlpha &&
                 IsFirstColorChannelDominant(
                    first: color.B,
                    color.G, color.R, minimumRatio);
    }
}
