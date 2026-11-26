using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class SubmarineBody
    {
        /// <summary>
        /// Extents of everything drawn as a part of the submarine, used for culling. Unlike <see cref="Borders"/>, includes structures without a collider
        /// and the parts of item sprites that extend beyond the items (e.g. turret barrels).
        /// </summary>
        public Rectangle VisibleBorders
        {
            get;
            private set;
        }

        partial void CalculateVisibleBorders(Vector2 minExtents, Vector2 maxExtents)
        {
            Vector2 min = minExtents;
            Vector2 max = maxExtents;
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != submarine || item.ParentInventory != null) { continue; }
                //range drawables (lights, editor range indicators) are ignored, they'd make the borders unnecessarily large
                Rectangle extents = item.CalculateVisibleExtents(ignoreRangedDrawables: true);
                min = Vector2.Min(min, item.Position + new Vector2(extents.X, extents.Y));
                max = Vector2.Max(max, item.Position + new Vector2(extents.Width, extents.Height));
            }
            VisibleBorders = new Rectangle((int)min.X, (int)max.Y, (int)(max.X - min.X), (int)(max.Y - min.Y));
        }

        partial void HandleLevelCollisionProjSpecific(Impact impact)
        {
            float wallImpact = Vector2.Dot(impact.Velocity, -impact.Normal);
            int particleAmount = (int)Math.Min(wallImpact, 10);

            const float BurstParticleThreshold = 5.0f;

            float velocityFactor = MathHelper.Clamp(wallImpact / 10.0f, 0.0f, 1.0f);
            for (int i = 0; i < particleAmount * 5; i++)
            {
                GameMain.ParticleManager.CreateParticle("iceshards",
                    ConvertUnits.ToDisplayUnits(impact.ImpactPos) + Rand.Vector(Rand.Range(1.0f, 50.0f)),
                    (Rand.Vector(0.9f) + impact.Normal) * Rand.Range(100.0f, 10000) * velocityFactor);
            }
            for (int i = 0; i < particleAmount; i++)
            {
                float particleVelocityMultiplier = Rand.Range(0.0f, 1);
                var p = GameMain.ParticleManager.CreateParticle("iceexplosion",
                     ConvertUnits.ToDisplayUnits(impact.ImpactPos) + Rand.Vector(Rand.Range(1.0f, 50.0f)),
                     (Rand.Vector(0.5f) + impact.Normal) * particleVelocityMultiplier * 500 * velocityFactor);
                if (p != null)
                {
                    p.VelocityChangeMultiplier = particleVelocityMultiplier * Rand.Range(0.0f, 1.0f);
                    p.Size *= Math.Max(particleVelocityMultiplier, 0);
                }
            }
            if (wallImpact > BurstParticleThreshold)
            {
                for (int i = 0; i < particleAmount; i++)
                {
                    GameMain.ParticleManager.CreateParticle("iceburst",
                         ConvertUnits.ToDisplayUnits(impact.ImpactPos) + Rand.Vector(Rand.Range(1.0f, 50.0f)),
                         angle: MathUtils.VectorToAngle(impact.Normal.FlipY() + Rand.Vector(0.25f)), speed: 0.0f);
                }
            }
        }

        partial void ClientUpdatePosition(float deltaTime)
        {
            if (GameMain.Client == null) { return; }

            Body.CorrectPosition(positionBuffer, out Vector2 newPosition, out Vector2 newVelocity, out _, out _);
            Vector2 moveAmount = ConvertUnits.ToDisplayUnits(newPosition - Body.SimPosition);
            newVelocity = newVelocity.ClampLength(100.0f);
            if (!MathUtils.IsValid(newVelocity) || moveAmount.LengthSquared() < 0.0001f)
            {
                return;
            }

            var subsToMove = submarine.GetConnectedSubs();
            foreach (Submarine dockedSub in subsToMove)
            {
                if (dockedSub == submarine) { continue; }
                //clear the position buffer of the docked subs to prevent unnecessary position corrections
                dockedSub.SubBody.positionBuffer.Clear();
            }

            Submarine closestSub;
            if (Character.Controlled == null)
            {
                closestSub = Submarine.FindClosest(GameMain.GameScreen.Cam.Position);
            }
            else
            {
                closestSub = Character.Controlled.Submarine;
            }

            bool displace = moveAmount.LengthSquared() > 100.0f * 100.0f;
            foreach (Submarine sub in subsToMove)
            {
                sub.PhysicsBody.LinearVelocity = newVelocity;

                if (displace)
                {
                    sub.PhysicsBody.SetTransform(sub.PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(moveAmount), 0.0f);
                    sub.SubBody.DisplaceCharacters(moveAmount);
                }
                else
                {
                    sub.PhysicsBody.SetTransformIgnoreContacts(sub.PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(moveAmount), 0.0f);
                }
            }
            if (closestSub != null && subsToMove.Contains(closestSub))
            {
                GameMain.GameScreen.Cam.Position += moveAmount;
                if (GameMain.GameScreen.Cam.TargetPos != Vector2.Zero) { GameMain.GameScreen.Cam.TargetPos += moveAmount; }

                if (Character.Controlled != null) { Character.Controlled.CursorPosition += moveAmount; }
            }
        }

        private void PlayDamageSounds(Dictionary<Structure, float> damagedStructures, Vector2 impactSimPos, float impact, string soundTag)
        {
            if (impact < MinCollisionImpact) { return; }

            //play a damage sound for the structure that took the most damage
            float maxDamage = 0.0f;
            Structure maxDamageStructure = null;
            foreach (KeyValuePair<Structure, float> structureDamage in damagedStructures)
            {
                if (maxDamageStructure == null || structureDamage.Value > maxDamage)
                {
                    maxDamage = structureDamage.Value;
                    maxDamageStructure = structureDamage.Key;
                }
            }

            if (maxDamageStructure != null)
            {
                PlayDamageSound(impactSimPos, impact, soundTag, maxDamageStructure);
            }
        }

        private void PlayDamageSound(Vector2 impactSimPos, float impact, string soundTag, Structure hitStructure = null)
        {
            if (impact < MinCollisionImpact) { return; }

            SoundPlayer.PlayDamageSound(
                soundTag,
                impact * 10.0f,
                ConvertUnits.ToDisplayUnits(impactSimPos),
                MathHelper.Lerp(2000.0f, 10000.0f, (impact - MinCollisionImpact) / 2.0f),
                hitStructure?.Tags);            
        }


    }
}
