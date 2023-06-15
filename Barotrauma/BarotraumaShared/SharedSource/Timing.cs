﻿using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    static class Timing
    {
        private static double alpha;

        /// <summary>
        /// Total amount of the time the game has run.
        /// </summary>
        public static double TotalTime;
        /// <summary>
        /// Total amount of time the game has run, excluding when the game is paused.
        /// </summary>
        public static double TotalTimeUnpaused;

        public static double Accumulator;
        public const int FixedUpdateRate = 60;
        public const double Step = 1.0 / FixedUpdateRate;
        public const double AccumulatorMax = 0.25f;

        /// <summary>
        /// Maximum FPS (0 = unlimited).
        /// </summary>
        public static int FrameLimit => GameSettings.CurrentConfig.Graphics.FrameLimit;

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
            //use a somewhat high epsilon - very small differences aren't visible
            if (MathUtils.NearlyEqual(previous, current, epsilon: 0.02f)) { return current; }
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
