using Barotrauma.Lights;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class FireSource
    {
        private LightSource lightSource;

        partial void UpdateProjSpecific(float growModifier)
        {
            EmitParticles(size, WorldPosition, hull, growModifier, OnChangeHull);
            
            lightSource.Color = new Color(1.0f, 0.45f, 0.3f) * Rand.Range(0.8f, 1.0f);
            if (Math.Abs((lightSource.Range * 0.2f) - Math.Max(size.X, size.Y)) > 1.0f) lightSource.Range = Math.Max(size.X, size.Y) * 5.0f;
            if (Vector2.DistanceSquared(lightSource.Position,position) > 5.0f) lightSource.Position = position + Vector2.UnitY * 30.0f;

            if (size.X > 256.0f)
            {
                if (burnDecals.Count == 0)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition + size/2);
                    if (newDecal != null) burnDecals.Add(newDecal);
                }
                else if (WorldPosition.X < burnDecals[0].WorldPosition.X - 256.0f)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition);
                    if (newDecal != null) burnDecals.Insert(0, newDecal);
                }
                else if (WorldPosition.X + size.X > burnDecals[burnDecals.Count-1].WorldPosition.X + 256.0f)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition + Vector2.UnitX * size.X);
                    if (newDecal != null) burnDecals.Add(newDecal);
                }
            }
            
            foreach (Decal d in burnDecals)
            {
                //prevent the decals from fading out as long as the firesource is alive
                d.FadeTimer = Math.Min(d.FadeTimer, d.FadeInTime);
            }
        }

        public static void EmitParticles(Vector2 size, Vector2 worldPosition, Hull hull, float growModifier, Particle.OnChangeHullHandler onChangeHull = null)
        {
            float particleCount = Rand.Range(0.0f, size.X / 50.0f);

            for (int i = 0; i < particleCount; i++)
            {
                Vector2 particlePos = new Vector2(
                    worldPosition.X + Rand.Range(0.0f, size.X),
                    Rand.Range(worldPosition.Y - size.Y, worldPosition.Y + 20.0f));

                Vector2 particleVel = new Vector2(
                    (particlePos.X - (worldPosition.X + size.X / 2.0f)),
                    (float)Math.Sqrt(size.X) * Rand.Range(0.0f, 15.0f) * growModifier);

                var particle = GameMain.ParticleManager.CreateParticle("flame",
                    particlePos, particleVel, 0.0f, hull);

                if (particle == null) continue;

                //make some of the particles create another firesource when they enter another hull
                if (Rand.Int(20) == 1) particle.OnChangeHull = onChangeHull;

                particle.Size *= MathHelper.Clamp(size.X / 60.0f * Math.Max(hull.Oxygen / hull.Volume, 0.4f), 0.5f, 1.0f);

                if (Rand.Int(5) == 1)
                {
                    var smokeParticle = GameMain.ParticleManager.CreateParticle("smoke",
                    particlePos, new Vector2(particleVel.X, particleVel.Y * 0.1f), 0.0f, hull);

                    if (smokeParticle != null)
                    {
                        smokeParticle.Size *= MathHelper.Clamp(size.X / 100.0f * Math.Max(hull.Oxygen / hull.Volume, 0.4f), 0.5f, 1.0f);
                    }
                }
            }
        }
    }
}
