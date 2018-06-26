using System;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public static class VectorExtensions
    {
        /// <summary>
        /// Unity's Angle implementation.
        /// Returns the angle in degrees.
        /// 0 - 180.
        /// </summary>
        public static float Angle(this Vector2 from, Vector2 to)
        {
            return (float)Math.Acos(MathHelper.Clamp(Vector2.Dot(Vector2.Normalize(from), Vector2.Normalize(to)), -1f, 1f)) * 57.29578f;
        }

        /// <summary>
        /// Creates a forward pointing vector based on the rotation (in radians).
        /// </summary>
        public static Vector2 Forward(float radians, float radius = 1)
        {
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians)) * radius;
        }

        /// <summary>
        /// Creates a normalized perpendicular vector to the right from a forward vector.
        /// </summary>
        public static Vector2 Right(this Vector2 forward)
        {
            var normV = Vector2.Normalize(forward);
            return new Vector2(normV.Y, -normV.X);
        }

        /// <summary>
        /// Transforms a vector relative to the given up vector.
        /// </summary>
        public static Vector2 TransformVector(this Vector2 v, Vector2 up)
        {
            return (up * v.Y) + (up.Right() * v.X);
        }

        /// <summary>
        /// Flips the x and y components.
        /// </summary>
        public static Vector2 Flip(this Vector2 v) => new Vector2(v.Y, v.X);

        /// <summary>
        /// Returns the sum of the x and y components.
        /// </summary>
        public static float Combine(this Vector2 v) => v.X + v.Y;

        public static Vector2 Clamp(this Vector2 v, Vector2 min, Vector2 max)
        {
            return new Vector2(MathHelper.Clamp(v.X, min.X, max.X), MathHelper.Clamp(v.Y, min.Y, max.Y));
        }
    }
}
