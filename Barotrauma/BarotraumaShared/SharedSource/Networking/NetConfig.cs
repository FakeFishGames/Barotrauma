using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Networking
{
    public static class NetConfig
    {
        public const int DefaultPort = 27015;
        public const int DefaultQueryPort = 27016;

        public static int MaxPlayers = 16;

        public const int ServerNameMaxLength = 60;
        public const int ServerMessageMaxLength = 2000;

        public const float MaxPhysicsBodyVelocity = 64.0f;
        public const float MaxPhysicsBodyAngularVelocity = 16.0f;

        public static float MaxHealthUpdateInterval = 2.0f;
        public static float MaxHealthUpdateIntervalDead = 10.0f;

        public static float HighPrioCharacterPositionUpdateDistance = 1000.0f;
        public static float LowPrioCharacterPositionUpdateDistance = 10000.0f;
        public static float HighPrioCharacterPositionUpdateInterval = 0.0f;
        public static float LowPrioCharacterPositionUpdateInterval = 1.0f;

        //this should be higher than LowPrioCharacterPositionUpdateInterval,
        //otherwise the clients may freeze characters even though the server hasn't actually stopped sending position updates
        public static float FreezeCharacterIfPositionDataMissingDelay = 2.0f;
        public static float DisableCharacterIfPositionDataMissingDelay = 3.5f;

        public static float DeleteDisconnectedTime = 20.0f;

        public static float ItemConditionUpdateInterval = 0.15f;
        public static float LevelObjectUpdateInterval = 0.5f;
        public static float HullUpdateInterval = 0.5f;
        public static float SparseHullUpdateInterval = 5.0f;
        public static float HullUpdateDistance = 20000.0f;

        public static int MaxEventPacketsPerUpdate = 4;

        /// <summary>
        /// When enabled, uses more lenient Lidgren handshake timeouts (longer connection timeout, more retry attempts).
        /// Useful for local testing when running multiple instances on the same machine under heavy load.
        /// </summary>
        public static bool UseLenientHandshake;

        /// <summary>
        /// Interpolates the positional error of a physics body towards zero.
        /// </summary>
        public static Vector2 InterpolateSimPositionError(Vector2 simPositionError, float? smoothingFactor = null)
        {
            float lengthSqr = simPositionError.LengthSquared();
            //correct immediately if the error is very large
            if (lengthSqr > 100.0f) { return Vector2.Zero; }
            float positionSmoothingFactor = smoothingFactor ?? MathHelper.Lerp(0.95f, 0.8f, MathHelper.Clamp(lengthSqr, 0.0f, 1.0f));
            return simPositionError *= positionSmoothingFactor;
        }

        /// <summary>
        /// Interpolates the rotational error of a physics body towards zero.
        /// </summary>
        public static float InterpolateRotationError(float rotationError)
        {
            //correct immediately if the error is very large
            if (rotationError > MathHelper.TwoPi) { return 0.0f; }
            float rotationSmoothingFactor = MathHelper.Lerp(0.95f, 0.8f, Math.Min(Math.Abs(rotationError), 1.0f));
            return rotationError *= rotationSmoothingFactor;
        }

        /// <summary>
        /// Interpolates the cursor position error towards zero.
        /// </summary>
        public static Vector2 InterpolateCursorPositionError(Vector2 cursorPositionError)
        {
            float lengthSqr = cursorPositionError.LengthSquared();
            //correct immediately if the error is very large
            if (lengthSqr > 1000.0f) { return Vector2.Zero; }
            return cursorPositionError *= 0.7f;
        }

        /// <summary>
        /// Quantizes the value so it's "as accurate as it can be" when the value is represented using the specified number of bits.
        /// Relevant e.g. when writing float values into network messages using some specific number of bits.
        /// </summary>
        public static Vector2 Quantize(Vector2 value, float min, float max, int numberOfBits)
        {
            return new Vector2(
                Quantize(value.X, min, max, numberOfBits), 
                Quantize(value.Y, min, max, numberOfBits));
        }

        /// <summary>
        /// Quantizes the value so it's "as accurate as it can be" when the value is represented using the specified number of bits.
        /// Relevant e.g. when writing float values into network messages using some specific number of bits.
        /// </summary>
        public static float Quantize(float value, float min, float max, int numberOfBits)
        {
            value = MathHelper.Clamp(value, min, max);

            float step = (max - min) / ((1 << numberOfBits) - 1);
            if (Math.Abs(value) < step + 0.00001f)
            {
                return 0.0f;
            }

            return MathUtils.RoundTowardsClosest(value - min, step) + min;
        }
    }
}
