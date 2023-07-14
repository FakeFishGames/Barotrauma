using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class DestructibleLevelWall : LevelWall, IDamageable 
    {

        public override float Alpha
        {
            get
            {
                if (FadeOutDuration <= 0.0f || FadeOutTimer < FadeOutDuration - 1.0f) { return 1.0f; }
                return MathHelper.Clamp(FadeOutDuration - FadeOutTimer, 0.0f, 1.0f);
            }
        }

        partial void AddDamageProjSpecific(float damage, Vector2 worldPosition)
        {
            if (damage <= 0.0f) { return; }
            Vector2 particlePos = worldPosition;
            if (!Cells.Any(c => c.IsPointInside(particlePos)))
            {
                bool intersectionFound = false;
                foreach (var cell in Cells)
                {
                    foreach (var edge in cell.Edges)
                    {
                        if (MathUtils.GetLineSegmentIntersection(worldPosition, cell.Center, edge.Point1 + cell.Translation, edge.Point2 + cell.Translation, out Vector2 intersection))
                        {
                            intersectionFound = true;
                            particlePos = intersection;
                            break;
                        }
                    }
                    if (intersectionFound) { break; }
                }
            }

            Vector2 particleDir = particlePos - WorldPosition;
            if (particleDir.LengthSquared() > 0.0001f) { particleDir = Vector2.Normalize(particleDir); }
            int particleAmount = MathHelper.Clamp((int)damage, 1, 10);
            for (int i = 0; i < particleAmount; i++)
            {
                var particle = GameMain.ParticleManager.CreateParticle("iceshards",
                    particlePos + Rand.Vector(5.0f),
                    particleDir * Rand.Range(200.0f, 500.0f) + Rand.Vector(100.0f));
            }
        }

        public void SetDamage(float damage)
        {
            Damage = damage;
            if (Damage >= MaxHealth && !Destroyed)
            {
                CreateFragments();
                Destroy();
            }
        }
    }
}
