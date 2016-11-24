using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Particles
{
    enum ParticleBlendState
    {
        AlphaBlend, Additive, Distortion
    }

    class ParticleManager
    {
        public static int particleCount;

        private const int MaxOutOfViewDist = 500;
        
        private const int MaxParticles = 1500;
        private Particle[] particles;

        private Dictionary<string, ParticlePrefab> prefabs;

        Camera cam;
        
        public ParticleManager(string configFile, Camera cam)
        {
            this.cam = cam;

            particles = new Particle[MaxParticles];

            XDocument doc = ToolBox.TryLoadXml(configFile);
            if (doc == null || doc.Root == null) return;

            prefabs = new Dictionary<string, ParticlePrefab>();

            foreach (XElement element in doc.Root.Elements())
            {
                if (prefabs.ContainsKey(element.Name.ToString()))
                {
                    DebugConsole.ThrowError("Error in " + configFile + "! Each particle prefab must have a unique name.");
                    continue;
                }
                prefabs.Add(element.Name.ToString(), new ParticlePrefab(element));
            }
        }

        public Particle CreateParticle(string prefabName, Vector2 position, float angle, float speed, Hull hullGuess = null)
        {
            return CreateParticle(prefabName, position, new Vector2((float)Math.Cos(angle), (float)-Math.Sin(angle)) * speed, angle, hullGuess);
        }

        public Particle CreateParticle(string prefabName, Vector2 position, Vector2 speed, float rotation=0.0f, Hull hullGuess = null)
        {
            ParticlePrefab prefab = FindPrefab(prefabName);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Particle prefab \"" + prefabName+"\" not found!");
                return null;
            }

            return CreateParticle(prefab, position, speed, rotation, hullGuess);
        }

        public Particle CreateParticle(ParticlePrefab prefab, Vector2 position, Vector2 speed, float rotation = 0.0f, Hull hullGuess = null)
        {
            if (!Submarine.RectContains(MathUtils.ExpandRect(cam.WorldView, MaxOutOfViewDist), position)) return null;
            //if (!cam.WorldView.Contains(position)) return null;

            if (particleCount >= MaxParticles) return null;

            if (particles[particleCount] == null) particles[particleCount] = new Particle();

            particles[particleCount].Init(prefab, position, speed, rotation, hullGuess);

            particleCount++;

            return particles[particleCount-1];

        }

        public ParticlePrefab FindPrefab(string prefabName)
        {
            ParticlePrefab prefab;
            prefabs.TryGetValue(prefabName, out prefab);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Particle prefab " + prefabName + " not found!");
                return null;
            }

            return prefab;
        }

        private void RemoveParticle(int index)
        {
            particleCount--;

            Particle swap = particles[index];
            particles[index] = particles[particleCount];
            particles[particleCount] = swap;
        }

        public void Update(float deltaTime)
        {
            for (int i = 0; i < particleCount; i++)
            {
                bool remove = false;
                try
                {
                    remove = !particles[i].Update(deltaTime);
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

        public void Draw(SpriteBatch spriteBatch, bool inWater, ParticleBlendState blendState)
        {
            ParticlePrefab.DrawTargetType drawTarget = inWater ? ParticlePrefab.DrawTargetType.Water : ParticlePrefab.DrawTargetType.Air;

            for (int i = 0; i < particleCount; i++)
            {
                if (particles[i].BlendState != blendState) continue;
                if (!particles[i].DrawTarget.HasFlag(drawTarget)) continue;
                
                particles[i].Draw(spriteBatch);
            }
        }

    }
}
