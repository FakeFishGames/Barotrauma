using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Networking
{
    static class NetConfig
    {
        public const int DefaultPort = 27015;
        public const int DefaultQueryPort = 27016;

        public const int MaxPlayers = 16;

        public const int ServerNameMaxLength = 60;

        public static string MasterServerUrl = GameMain.Config.MasterServerUrl;

        //if a Character is further than this from the sub and the players, the server will disable it
        //(in display units)
        public const float DisableCharacterDist = 22000.0f;
        public const float DisableCharacterDistSqr = DisableCharacterDist * DisableCharacterDist;

        //the character needs to get this close to be re-enabled
        public const float EnableCharacterDist = 20000.0f;
        public const float EnableCharacterDistSqr = EnableCharacterDist * EnableCharacterDist;

        public const float MaxPhysicsBodyVelocity = 64.0f;
        public const float MaxPhysicsBodyAngularVelocity = 16.0f;

        public const float MaxHealthUpdateInterval = 2.0f;
        public const float MaxHealthUpdateIntervalDead = 10.0f;

        public const float HighPrioCharacterPositionUpdateDistance = 1000.0f;
        public const float LowPrioCharacterPositionUpdateDistance = 10000.0f;
        public const float HighPrioCharacterPositionUpdateInterval = 0.0f;
        public const float LowPrioCharacterPositionUpdateInterval = 1.0f;

        public const float DeleteDisconnectedTime = 20.0f;

        public const float ItemConditionUpdateInterval = 0.15f;
        public const float LevelObjectUpdateInterval = 0.5f;
        public const float HullUpdateInterval = 0.5f;
        public const float HullUpdateDistance = 20000.0f;

        public const int MaxEventPacketsPerUpdate = 4;

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

        public static Vector2 Quantize(Vector2 value, float min, float max, int numberOfBits)
        {
            return new Vector2(
                Quantize(value.X, min, max, numberOfBits), 
                Quantize(value.Y, min, max, numberOfBits));
        }

        public static float Quantize(float value, float min, float max, int numberOfBits)
        {
            float step = (max - min) / (1 << (numberOfBits + 1));
            if (Math.Abs(value) < step + 0.00001f)
            {
                return 0.0f;
            }

            return MathUtils.RoundTowardsClosest(MathHelper.Clamp(value, min, max), step);
        }
    }
}
