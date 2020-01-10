using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    static class Timing
    {
        private static double alpha;

        public static double TotalTime;

        public static double Accumulator;
        public static double Step = 1.0 / 60.0;

        public static double Alpha
        {
            get { return alpha; }
            set { alpha = Math.Min(Math.Max(value, 0.0), 1.0); }
        }

        public static double Interpolate(double previous, double current)
        {
            return current * alpha + previous * (1.0 - alpha);
        }

        public static float Interpolate(float previous, float current)
        {
            return current * (float)alpha + previous * (1.0f - (float)alpha);
        }

        public static float InterpolateRotation(float previous, float current)
        {
            float angleDiff = MathUtils.GetShortestAngle(previous, current);

            return previous + angleDiff * (float)alpha;
        }

        public static Vector2 Interpolate(Vector2 previous, Vector2 current)
        {
            return new Vector2(
                Interpolate(previous.X, current.X),
                Interpolate(previous.Y, current.Y));
        }
    }
}
