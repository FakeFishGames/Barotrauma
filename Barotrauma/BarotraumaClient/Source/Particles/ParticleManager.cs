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
        AlphaBlend, Additive, Distortion
    }

    class ParticleManager
    {
        public readonly string ConfigFile;
        public static int particleCount;

        public int MaxOutOfViewDist = 500;
        
        public int MaxParticles = 1500;
        private Particle[] particles;

        private Dictionary<string, ParticlePrefab> prefabs;

        private Camera cam;
        
        public ParticleManager(string configFile, Camera cam)
        {
            ConfigFile = configFile;
            this.cam = cam;

            MaxParticles = GameMain.NilMod.MaxParticles;

            particles = new Particle[MaxParticles];

            LoadPrefabs(configFile);
        }

        public void LoadPrefabs(string file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            prefabs = new Dictionary<string, ParticlePrefab>();

            foreach (XElement element in doc.Root.Elements())
            {
                if (prefabs.ContainsKey(element.Name.ToString()))
                {
                    DebugConsole.ThrowError("Error in " + file + "! Each particle prefab must have a unique name.");
                    continue;
                }
                prefabs.Add(element.Name.ToString(), new ParticlePrefab(element));
            }
        }

        public Particle CreateParticle(string prefabName, Vector2 position, float angle, float speed, Hull hullGuess = null)
        {
            return CreateParticle(prefabName, position, new Vector2((float)Math.Cos(angle), (float)-Math.Sin(angle)) * speed, angle, hullGuess);
        }

        public Particle CreateParticle(string prefabName, Vector2 position, Vector2 velocity, float rotation=0.0f, Hull hullGuess = null)
        {
            ParticlePrefab prefab = FindPrefab(prefabName);

            if (prefab == null)
            {
                DebugConsole.ThrowError("Particle prefab \"" + prefabName+"\" not found!");
                return null;
            }

            return CreateParticle(prefab, position, velocity, rotation, hullGuess);
        }

        public Particle CreateParticle(ParticlePrefab prefab, Vector2 position, Vector2 velocity, float rotation = 0.0f, Hull hullGuess = null)
        {
            if (particleCount >= MaxParticles || prefab == null || GameMain.NilMod.DisableParticles) return null;

            if (((Rand.Range(1, 100) <= GameMain.NilMod.ParticleSpawnPercent) || GameMain.NilMod.ParticleWhitelist.Find(p => p == prefab.Name) != null))
            {
                Vector2 particleEndPos = prefab.CalculateEndPosition(position, velocity);

                Vector2 minPos = new Vector2(Math.Min(position.X, particleEndPos.X), Math.Min(position.Y, particleEndPos.Y));
                Vector2 maxPos = new Vector2(Math.Max(position.X, particleEndPos.X), Math.Max(position.Y, particleEndPos.Y));

                Rectangle expandedViewRect = MathUtils.ExpandRect(cam.WorldView, MaxOutOfViewDist);

                if (minPos.X > expandedViewRect.Right || maxPos.X < expandedViewRect.X) return null;
                if (minPos.Y > expandedViewRect.Y || maxPos.Y < expandedViewRect.Y - expandedViewRect.Height) return null;

                if (particles[particleCount] == null) particles[particleCount] = new Particle();

                particles[particleCount].Init(prefab, position, velocity, rotation, hullGuess);

                particleCount++;

                return particles[particleCount - 1];
            }
            else
            {
                return null;
            }
        }

        public List<ParticlePrefab> GetPrefabList()
        {
            return prefabs.Values.ToList();
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

        public void Draw(SpriteBatch spriteBatch, bool inWater, bool? inSub, ParticleBlendState blendState)
        {
            ParticlePrefab.DrawTargetType drawTarget = inWater ? ParticlePrefab.DrawTargetType.Water : ParticlePrefab.DrawTargetType.Air;

            for (int i = 0; i < particleCount; i++)
            {
                if (particles[i].BlendState != blendState) continue;
                if (!particles[i].DrawTarget.HasFlag(drawTarget)) continue;
                if (inSub.HasValue && (particles[i].CurrentHull == null) == inSub.Value) continue;
                
                particles[i].Draw(spriteBatch);
            }
        }

        //NilMod Reset Particles for changing their settings
        public void ResetParticleManager()
        {
            //Nullify the variables

            for(int i = MaxParticles - 1; i > 0; i--)
            {
                particles[i] = null;
            }
            particles = null;

            MaxParticles = GameMain.NilMod.MaxParticles;

            //Reset the entire componant

            particles = new Particle[MaxParticles];

            particleCount = 0;
        }
    }
}
