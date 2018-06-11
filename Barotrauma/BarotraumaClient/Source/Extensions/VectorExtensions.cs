using System;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class VectorExtensions
    {
        /// <summary>
        /// Unity's Angle implementation.
        /// Returns the angle in degrees.
        /// Min -180, max 180.
        /// </summary>
        public static float Angle(this Vector2 from, Vector2 to)
        {
            return (float)Math.Acos(MathHelper.Clamp(Vector2.Dot(Vector2.Normalize(from), Vector2.Normalize(to)), -1f, 1f)) * 57.29578f;
        }

        /// <summary>
        /// Creates a forward pointing vector based on the rotation (in radians).
        /// </summary>
        public static Vector2 Forward(float radians, float radius)
        {
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians)) * radius;
        }
    }
}
