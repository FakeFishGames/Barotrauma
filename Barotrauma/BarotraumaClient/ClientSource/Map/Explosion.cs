using Barotrauma.Lights;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Explosion
    {
        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull)
        {
            if (GameMain.Client?.MidRoundSyncing ?? false) { return; }

            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", worldPosition,
                    Vector2.Zero, 0.0f, hull);
            }

            hull ??= Hull.FindHull(worldPosition, useWorldCoordinates: true);
            bool underwater = hull == null || worldPosition.Y < hull.WorldSurface;

            if (underwater && underwaterBubble)
            {
                var underwaterExplosion = GameMain.ParticleManager.CreateParticle("underwaterexplosion", worldPosition, Vector2.Zero, 0.0f, hull);
                if (underwaterExplosion != null)
                {
                    underwaterExplosion.Size *= MathHelper.Clamp(Attack.Range / 150.0f, 0.5f, 10.0f);
                    underwaterExplosion.StartDelay = 0.0f;
                }
            }
            if (!underwater && (flames || smoke))
            {
                for (int i = 0; i < Attack.Range * 0.025f; i++)
                {
                    float distFactor = 0.0f;

                    if (i > 0 && Attack.Range > 100.0f)
                    {
                        distFactor = Rand.Range(0.0f, 1.0f);
                        //sqrt to make larger values more common (= more particles spawn further away from the origin)
                        distFactor = MathF.Sqrt(distFactor);
                    }
                    float sizeFactor = MathHelper.Clamp(Attack.Range / 1000.0f, 0.0f, 1.0f);
                    float minScale = MathHelper.Lerp(0.2f, 1.0f, sizeFactor);
                    float maxScale = MathUtils.InverseLerp(2.0f, 3.0f, sizeFactor);
                    //larger particles closer to the origin
                    float particleScale = MathHelper.Clamp(1.0f - distFactor, minScale, maxScale);

                    var particlePrefab = ParticleManager.FindPrefab("explosionfire");
                    Vector2 pos = worldPosition;
                    if (i > 0)
                    {
                        pos = ClampParticlePos(worldPosition + Rand.Vector(Attack.Range * distFactor * 0.3f), hull, particlePrefab);
                    }

                    if (flames)
                    {
                        var flameParticle = GameMain.ParticleManager.CreateParticle(particlePrefab,
                            pos,
                            velocity: Vector2.Zero, hullGuess: hull);
                        if (flameParticle != null)
                        {
                            //brief delay to particles futher from origin
                            flameParticle.StartDelay = distFactor * sizeFactor;
                            flameParticle.Size *= particleScale;
                        }
                    }
                    if (smoke)
                    {
                        GameMain.ParticleManager.CreateParticle(
                            ParticleManager.FindPrefab(Rand.Range(0.0f, 1.0f) < 0.5f ? "explosionsmoke" : "smoke"), 
                            pos, velocity: Vector2.Zero, hullGuess: hull);
                    }
                }
            }

            for (int i = 0; i < Attack.Range * 0.1f; i++)
            {
                if (underwater && underwaterBubble)
                {
                    Vector2 bubblePos = Rand.Vector(Rand.Range(0.0f, Attack.Range * 0.5f));

                    GameMain.ParticleManager.CreateParticle("risingbubbles", worldPosition + bubblePos,
                        velocity: Vector2.Zero, hullGuess: hull);
                    if (i < Attack.Range * 0.02f)
                    {
                        var underwaterExplosion = GameMain.ParticleManager.CreateParticle("underwaterexplosion", worldPosition + bubblePos,
                            Vector2.Zero, 0.0f, hull);
                        if (underwaterExplosion != null)
                        {
                            underwaterExplosion.Size *= MathHelper.Clamp(Attack.Range / 300.0f, 0.5f, 2.0f) * Rand.Range(0.8f, 1.2f);
                        }
                    }                    
                }
                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", worldPosition,
                        Rand.Vector(Rand.Range(800.0f, 1500.0f)), 0.0f, hull);
                }
                if (debris)
                {
                    GameMain.ParticleManager.CreateParticle("explosiondebris", worldPosition,
                        Rand.Vector(Rand.Range(800.0f, 2000.0f)), 0.0f, hull);
                }
            }

            if (flash)
            {
                float displayRange = flashRange ?? (Attack.Range * 2);
                if (displayRange < 0.1f) { return; }
                var light = new LightSource(worldPosition, displayRange, flashColor, null);
                CoroutineManager.StartCoroutine(DimLight(light));
            }
        }

        private static Vector2 ClampParticlePos(Vector2 particlePos, Hull hull, ParticlePrefab particlePrefab)
        {
            float minX = hull.WorldRect.X;
            float maxX = hull.WorldRect.Right;
            float minY = hull.WorldRect.Y - hull.WorldRect.Height;
            float maxY = hull.WorldRect.Y;
            if (particlePrefab != null)
            {
                minX = Math.Min(minX + particlePrefab.CollisionRadius, hull.WorldRect.Center.X);
                maxX = Math.Max(maxX - particlePrefab.CollisionRadius, hull.WorldRect.Center.X);
                minY = Math.Min(minY + particlePrefab.CollisionRadius, hull.WorldRect.Y - hull.WorldRect.Height / 2);
                maxY = Math.Max(maxY - particlePrefab.CollisionRadius, hull.WorldRect.Y - hull.WorldRect.Height / 2);
            }

            return new Vector2(
                MathHelper.Clamp(particlePos.X, minX, maxX),
                MathHelper.Clamp(particlePos.Y, minY, maxY));
        }

        private IEnumerable<CoroutineStatus> DimLight(LightSource light)
        {
            float currBrightness = 1.0f;
            while (light.Color.A > 0.0f && flashDuration > 0.0f && currBrightness > 0.0f)
            {
                if (!CoroutineManager.Paused)
                {
                    light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, (byte)(currBrightness * 255));
                    currBrightness -= 1.0f / flashDuration * CoroutineManager.DeltaTime;
                }
                yield return CoroutineStatus.Running;
            }

            light.Remove();

            yield return CoroutineStatus.Success;
        }

        static partial void PlayTinnitusProjSpecific(float volume) => SoundPlayer.PlaySound("tinnitus", volume: volume);
    }
}
