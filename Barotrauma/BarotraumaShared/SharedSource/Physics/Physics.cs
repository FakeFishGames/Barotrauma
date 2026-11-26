using Barotrauma.Extensions;
using FarseerPhysics.Collision;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

#nullable enable

namespace Barotrauma
{
    /// <summary>Hit information for a raycast.</summary>
    /// <param name="Fixture">The fixture that was hit.</param>
    /// <param name="Point">The location of the hit.</param>
    /// <param name="Normal">The normal of the edge that was hit.</param>
    /// <param name="Distance">The distance between the start of the ray and the hit point.</param>
    public readonly record struct RaycastHit(Fixture Fixture, Vector2 Point, Vector2 Normal, float Distance);

    public static class Physics
    {
        public const Category CollisionNone         = Category.None;
        public const Category CollisionAll          = Category.All;
        public const Category CollisionWall         = Category.Cat1;
        public const Category CollisionCharacter    = Category.Cat2;
        public const Category CollisionPlatform     = Category.Cat3;
        public const Category CollisionStairs       = Category.Cat4;
        public const Category CollisionItem         = Category.Cat5;
        public const Category CollisionItemBlocking = Category.Cat6;
        public const Category CollisionProjectile   = Category.Cat7;
        public const Category CollisionLevel        = Category.Cat8;
        public const Category CollisionRepairableWall       = Category.Cat9;
        public const Category CollisionLagCompensationBody  = Category.Cat10;

        public const Category DefaultItemCollidesWith = CollisionWall | CollisionLevel | CollisionPlatform | CollisionRepairableWall;

        public static float DisplayToRealWorldRatio = 1.0f / 100.0f;

        public const float DisplayToSimRation = 100.0f;

        public const float NeutralDensity = 10.0f;

        public static bool TryParseCollisionCategory(string categoryName, out Category category)
        {
            category = Category.None;
            if (string.IsNullOrEmpty(categoryName))
            {
                return false;
            }
            switch (categoryName.ToLowerInvariant())
            {
                case "all":
                    category = CollisionAll;
                    return true;
                case "wall":
                case "structure":
                    category = CollisionWall;
                    return true;
                case "character":
                    category = CollisionCharacter;
                    return true;
                case "platform":
                    category = CollisionPlatform;
                    return true;
                case "stairs":
                    category = CollisionStairs;
                    return true;
                case "item":
                    category = CollisionItem;
                    return true;
                case "itemblocking":
                    category = CollisionItemBlocking;
                    return true;
                case "projectile":
                    category = CollisionProjectile;
                    return true;
                case "level":
                    category = CollisionLevel;
                    return true;
                case "repair":
                    category = CollisionRepairableWall;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Casts a ray against every <see cref="Fixture"/> and returns the closest hit.
        /// </summary>
        /// <param name="start">The starting point of the ray.</param>
        /// <param name="rotationRad">The direction of the ray.</param>
        /// <param name="length">The maximum distance the ray can check for hits.</param>
        /// <param name="hit">The details of the hit closest to <paramref name="start"/>.</param>
        /// <param name="collidesWith">Filters which fixtures the ray can hit.</param>
        /// <param name="hitSensors">Specifies whether the ray can hit sensors.</param>
        /// <param name="allowInsideStart">Specifies whether the ray can hit fixtures overlapping <paramref name="start"/>.</param>
        /// <param name="ignoredFixtures">Additional fixtures for the ray to ignore.</param>
        /// <returns><see langword="true"/> if the ray intersects a <see cref="Fixture"/>, otherwise <see langword="false"/>.</returns>
        public static bool Raycast(Vector2 start, float rotationRad, float length, [NotNullWhen(true)] out RaycastHit? hit, Category collidesWith = CollisionAll, bool hitSensors = false, bool allowInsideStart = true, IEnumerable<Fixture>? ignoredFixtures = null)
        {
            if (Submarine.PickBody(start, start + MathUtils.RotatedUnitXRadians(rotationRad) * length, null, collidesWith, !hitSensors, fixture => ignoredFixtures == null || !ignoredFixtures.Contains(fixture), allowInsideStart) != null)
            {
                hit = new RaycastHit(Submarine.LastPickedFixture, Submarine.LastPickedPosition, Submarine.LastPickedNormal, length * Submarine.LastPickedFraction);
                return true;
            }
            hit = null;
            return false;
        }

        /// <summary>
        /// Tests a point against every <see cref="Fixture"/> and returns the first one overlapping it.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="hit">The first <see cref="Fixture"/> overlapping the <paramref name="point"/>.</param>
        /// <param name="collidesWith">Filters which fixtures the ray can hit.</param>
        /// <param name="hitSensors">Specifies whether to test against sensors.</param>
        /// <param name="ignoredFixtures">Additional fixtures for the test to ignore.</param>
        /// <returns><see langword="true"/> if a <see cref="Fixture"/> overlaps <paramref name="point"/>, otherwise <see langword="false"/>.</returns>
        public static bool TestPoint(Vector2 point, [NotNullWhen(true)] out Fixture? hit, Category collidesWith = CollisionAll, bool hitSensors = false, IEnumerable<Fixture>? ignoredFixtures = null)
        {
            AABB aabb = new(point, Settings.Epsilon, Settings.Epsilon);

            Fixture? firstHit = null;
            GameMain.World.QueryAABB(fixture =>
            {
                if (fixture.IsSensor && !hitSensors) { return true; }
                if (!fixture.CollisionCategories.HasAnyFlag(collidesWith)) { return true; }
                if (ignoredFixtures != null && ignoredFixtures.Contains(fixture)) { return true; }
                if (!fixture.TestPoint(ref point)) { return true; }
                firstHit = fixture;
                return false;
            }, ref aabb);

            hit = firstHit;
            return firstHit != null;
        }

        public static Vector2 DrawPosToSimPos(Vector2 drawPos) => ConvertUnits.ToSimUnits(drawPos).FlipY();
        public static Vector2 SimPosToDrawPos(Vector2 simPos) => ConvertUnits.ToDisplayUnits(simPos).FlipY();
    }
}
