using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Extensions
{
    public static class ColorExtensions
    {
        /*
        public static Color Multiply(this Color color, float value, bool onlyAlpha = false)
        {
            return onlyAlpha ?
                new Color(color.R, color.G, color.B, MultiplyComponent(color.A, value)) :
                new Color(MultiplyComponent(color.R, value), MultiplyComponent(color.G, value), MultiplyComponent(color.B, value), MultiplyComponent(color.A, value));

            static byte MultiplyComponent(float colorComponent, float multiplier)
            {
                return (byte)MathHelper.Clamp(colorComponent * multiplier, 0.0f, 255.0f);
            }
        }
        */
        public static Color Multiply(this Color color, float value)
        {
            return new Color(
                MultiplyComponent(color.R, value),
                MultiplyComponent(color.G, value),
                MultiplyComponent(color.B, value),
                MultiplyComponent(color.A, value)
            );
        }

        // Use this for alpha-only multiplication
        public static Color MultiplyAlpha(this Color color, float value)
        {
            return new Color(color.R, color.G, color.B, MultiplyComponent(color.A, value));
        }
        private static byte MultiplyComponent(float component, float multiplier)
        {
            return (byte)MathHelper.Clamp(component * multiplier, 0.0f, 255.0f);
        }
        /// <summary>
        /// Multiplies the color components(RGBA) by the brightness factor, 
        /// treating the input as a normalized 0-1 scale.
        /// </summary>
        public static Color MultiplyNormalized(this Color color, float brightness)
        {
            //we can do this because we multiply and devide for naught
            //*255.f/255.f (redundant so we use byte cast)
            return new Color(
                MultiplyComponent(color.R, brightness),
                MultiplyComponent(color.G, brightness),
                MultiplyComponent(color.B, brightness),
                color.A
            );
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
