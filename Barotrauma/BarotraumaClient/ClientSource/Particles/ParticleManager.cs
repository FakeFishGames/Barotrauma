using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    enum ParticleBlendState
    {
        AlphaBlend, Additive//, Distortion
    }

    class ParticleManager
    {
        private const int MaxOutOfViewDist = 500;

        private int particleCount;
        public int ParticleCount
        {
            get { return particleCount; }
        }

        private int maxParticles;
        public int MaxParticles
        {
            get { return maxParticles; }
            set
            {
                if (maxParticles == value || value < 4) return;

                Particle[] newParticles = new Particle[value];

                for (int i = 0; i < Math.Min(maxParticles, value); i++)
                {
                    newParticles[i] = particles[i];
                }

                particleCount = Math.Min(particleCount, value);
                particles = newParticles;
                maxParticles = value;
            }
        }
        private Particle[] particles;

        private Camera cam;

        public Camera Camera
        {
            get { return cam; }
            set { cam = value; }
        }
        
        public ParticleManager(Camera cam)
        {
            this.cam = cam;

            MaxParticles = GameSettings.CurrentConfig.Graphics.ParticleLimit;
        }

        public Particle CreateParticle(string prefabName, Vector2 position, float angle, float speed, Hull hullGuess = null, float collisionIgnoreTimer = 0f, Tuple<Vector2, Vector2> tracerPoints = null)
        {
            return CreateParticle(prefabName, position, new Vector2((float)Math.Cos(angle), (float)-Math.Sin(angle)) * speed, angle, hullGuess, collisionIgnoreTimer, tracerPoints: tracerPoints);
        }

        public Particle CreateParticle(string prefabName, Vector2 position, Vector2 velocity, float rotation = 0.0f, Hull hullGuess = null, float collisionIgnoreTimer = 0f, Tuple<Vector2, Vector2> tracerPoints = null)
        {
            ParticlePrefab prefab = FindPrefab(prefabName);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Particle prefab \"" + prefabName + "\" not found!");
                return null;
            }
            return CreateParticle(prefab, position, velocity, rotation, hullGuess, collisionIgnoreTimer: collisionIgnoreTimer, tracerPoints:tracerPoints);
        }

        public Particle CreateParticle(ParticlePrefab prefab, Vector2 position, Vector2 velocity, float rotation = 0.0f, Hull hullGuess = null, bool drawOnTop = false, float collisionIgnoreTimer = 0f, Tuple<Vector2, Vector2> tracerPoints = null)
        {
            if (prefab == null || prefab.Sprites.Count == 0) { return null; }

            if (particleCount >= MaxParticles)
            {
                for (int i = 0; i < particleCount; i++)
                {
                    if (particles[i].Prefab.Priority < prefab.Priority)
                    {
                        RemoveParticle(i);
                        break;
                    }
                }
                if (particleCount >= MaxParticles) { return null; }
            }

            Vector2 particleEndPos = prefab.CalculateEndPosition(position, velocity);

            Vector2 minPos = new Vector2(Math.Min(position.X, particleEndPos.X), Math.Min(position.Y, particleEndPos.Y));
            Vector2 maxPos = new Vector2(Math.Max(position.X, particleEndPos.X), Math.Max(position.Y, particleEndPos.Y));

            if (tracerPoints != null)
            {
                minPos = new Vector2(
                    Math.Min(Math.Min(minPos.X, tracerPoints.Item1.X), tracerPoints.Item2.X),
                    Math.Min(Math.Min(minPos.Y, tracerPoints.Item1.Y), tracerPoints.Item2.Y));
                maxPos = new Vector2(
                    Math.Max(Math.Max(maxPos.X, tracerPoints.Item1.X), tracerPoints.Item2.X),
                    Math.Max(Math.Max(maxPos.Y, tracerPoints.Item1.Y), tracerPoints.Item2.Y));
            }

            Rectangle expandedViewRect = MathUtils.ExpandRect(cam.WorldView, MaxOutOfViewDist);

            if (minPos.X > expandedViewRect.Right || maxPos.X < expandedViewRect.X) { return null; }
            if (minPos.Y > expandedViewRect.Y || maxPos.Y < expandedViewRect.Y - expandedViewRect.Height) { return null; }

            if (particles[particleCount] == null) { particles[particleCount] = new Particle(); }

            particles[particleCount].Init(prefab, position, velocity, rotation, hullGuess, drawOnTop, collisionIgnoreTimer, tracerPoints: tracerPoints);

            particleCount++;

            return particles[particleCount - 1];
        }

        public List<ParticlePrefab> GetPrefabList()
        {
            return ParticlePrefab.Prefabs.ToList();
        }

        public ParticlePrefab FindPrefab(string prefabName)
        {
            return ParticlePrefab.Prefabs.Find(p => p.Identifier == prefabName);
        }

        private void RemoveParticle(int index)
        {
            particleCount--;

            Particle swap = particles[index];
            particles[index] = particles[particleCount];
            particles[particleCount] = swap;
        }


        public void RemoveParticle(Particle particle)
        {
            for (int i = 0; i < particleCount; i++)
            {
                if (particles[i] == particle)
                {
                    RemoveParticle(i);
                    return;
                }
            }
        }

        public void Update(float deltaTime)
        {
            MaxParticles = GameSettings.CurrentConfig.Graphics.ParticleLimit;

            for (int i = 0; i < particleCount; i++)
            {
                bool remove;
                try
                {
                    remove = particles[i].Update(deltaTime) == Particle.UpdateResult.Delete;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Particle update failed", e);
                    remove = true;
                }

                if (remove) RemoveParticle(i);
            }
        }

        public void UpdateTransforms()
        {
            for (int i = 0; i < particleCount; i++)
            {
                particles[i].UpdateDrawPos();
            }
        }

        public Dictionary<ParticlePrefab, int> CountActiveParticles()
        {
            Dictionary<ParticlePrefab, int> activeParticles = new Dictionary<ParticlePrefab, int>();
            for (int i = 0; i < particleCount; i++)
            {
                if (!activeParticles.ContainsKey(particles[i].Prefab)) activeParticles[particles[i].Prefab] = 0;
                activeParticles[particles[i].Prefab]++;
            }
            return activeParticles;
        }

        public void Draw(SpriteBatch spriteBatch, bool inWater, bool? inSub, ParticleBlendState blendState)
        {
            ParticlePrefab.DrawTargetType drawTarget = inWater ? ParticlePrefab.DrawTargetType.Water : ParticlePrefab.DrawTargetType.Air;

            for (int i = 0; i < particleCount; i++)
            {
                var particle = particles[i];
                if (particle.BlendState != blendState) { continue; }
                //equivalent to !particles[i].DrawTarget.HasFlag(drawTarget) but garbage free and faster
                if ((particle.DrawTarget & drawTarget) == 0) { continue; } 
                if (inSub.HasValue)
                {
                    bool isOutside = particle.CurrentHull == null;
                    if (!particle.DrawOnTop && isOutside == inSub.Value)
                    {
                        continue;
                    }
                }
                
                particles[i].Draw(spriteBatch);
            }
        }

        public void RemoveByPrefab(ParticlePrefab prefab)
        {
            if (particles == null) { return; }
            for (int i = particles.Length - 1; i >= 0; i--)
            {
                if (particles[i]?.Prefab == prefab)
                {
                    if (i < particleCount) { particleCount--; }

                    Particle swap = particles[particleCount];
                    particles[particleCount] = null;
                    particles[i] = swap;
                }
            }
        }

    }
}
