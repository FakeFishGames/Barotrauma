using System;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    static class Physics
    {
        private static double alpha;

        public const Category CollisionNone         = Category.None;
        public const Category CollisionAll          = Category.All;
        public const Category CollisionWall         = Category.Cat1;
        public const Category CollisionCharacter    = Category.Cat2;
        public const Category CollisionPlatform     = Category.Cat3;
        public const Category CollisionStairs       = Category.Cat4;
        public const Category CollisionMisc         = Category.Cat5;
        public const Category CollisionProjectile   = Category.Cat6;
        public const Category CollisionLevel        = Category.Cat7;

        public static double accumulator;
        public static double step = 1.0f/60.0f;

        public const float DisplayToSimRation = 100.0f;

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

        public static Vector2 Interpolate(Vector2 previous, Vector2 current)
        {
            return new Vector2(
                Interpolate(previous.X, current.X), 
                Interpolate(previous.Y, current.Y));
        }
    }

}
