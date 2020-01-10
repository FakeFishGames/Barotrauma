using System;
using Microsoft.Xna.Framework;

namespace Barotrauma.Extensions
{
    public static class VectorExtensions
    {
        /// <summary>
        /// Unity's Angle implementation without the conversion to degrees.
        /// Returns the angle in radians between two vectors.
        /// 0 - Pi.
        /// </summary>
        public static float Angle(this Vector2 from, Vector2 to)
        {
            return (float)Math.Acos(MathHelper.Clamp(Vector2.Dot(Vector2.Normalize(from), Vector2.Normalize(to)), -1f, 1f));
        }

        /// <summary>
        /// Creates a forward pointing vector based on the rotation (in radians).
        /// </summary>
        public static Vector2 Forward(float radians, float length = 1)
        {
            return new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians)) * length;
        }

        /// <summary>
        /// Creates a backward pointing vector based on the rotation (in radians).
        /// </summary>
        public static Vector2 Backward(float radians, float length = 1)
        {
            return -Forward(radians, length);
        }

        /// <summary>
        /// Creates a forward pointing vector based on the rotation (in radians). TODO: remove when the implications have been neutralized
        /// </summary>
        public static Vector2 ForwardFlipped(float radians, float length = 1)
        {
            return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians)) * length;
        }

        /// <summary>
        /// Creates a backward pointing vector based on the rotation (in radians). TODO: remove when the implications have been neutralized
        /// </summary>
        public static Vector2 BackwardFlipped(float radians, float length = 1)
        {
            return -ForwardFlipped(radians, length);
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
        /// Creates a normalized perpendicular vector to the left from a forward vector.
        /// </summary>
        public static Vector2 Left(this Vector2 forward)
        {
            return -forward.Right();
        }

        /// <summary>
        /// Transforms a vector relative to the given up vector.
        /// </summary>
        public static Vector2 TransformVector(this Vector2 v, Vector2 up)
        {
            up = Vector2.Normalize(up);
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
            return Vector2.Clamp(v, min, max);
        }

        public static bool NearlyEquals(this Vector2 v, Vector2 other)
        {
            return MathUtils.NearlyEqual(v.X, other.X) && MathUtils.NearlyEqual(v.Y, other.Y);
        }
    }
}
