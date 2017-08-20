using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class ParticleEmitter
    {
        private float emitTimer;

        public readonly ParticleEmitterPrefab Prefab;

        public ParticleEmitter(XElement element)
        {
            Prefab = new ParticleEmitterPrefab(element);
        }

        public ParticleEmitter(ParticleEmitterPrefab prefab)
        {
            Prefab = prefab;
        }

        public void Emit(float deltaTime, Vector2 position, Hull hullGuess = null)
        {
            emitTimer += deltaTime;

            if (Prefab.ParticlesPerSecond > 0)
            {
                float emitInterval = 1.0f / Prefab.ParticlesPerSecond;
                while (emitTimer > emitInterval)
                {
                    Emit(position, hullGuess);
                    emitTimer -= emitInterval;
                }
            }

            for (int i = 0; i < Prefab.ParticleAmount; i++)
            {
                Emit(position, hullGuess);
            }
        }

        private void Emit(Vector2 position, Hull hullGuess = null)
        {
            float angle = Rand.Range(Prefab.AngleMin, Prefab.AngleMax);
            Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Rand.Range(Prefab.VelocityMin, Prefab.VelocityMax);

            var particle = GameMain.ParticleManager.CreateParticle(Prefab.ParticlePrefab, position, velocity, 0.0f, hullGuess);

            if (particle != null)
            {
                particle.Size *= Rand.Range(Prefab.ScaleMin, Prefab.ScaleMax);
            }
        }
    }

    class ParticleEmitterPrefab
    {        
        public readonly string Name;

        public readonly ParticlePrefab ParticlePrefab;

        public readonly float AngleMin, AngleMax;

        public readonly float VelocityMin, VelocityMax;

        public readonly float ScaleMin, ScaleMax;
        
        public readonly int ParticleAmount;
        public readonly float ParticlesPerSecond;        

        public ParticleEmitterPrefab(XElement element)
        {
            Name = element.Name.ToString();

            ParticlePrefab = GameMain.ParticleManager.FindPrefab(ToolBox.GetAttributeString(element, "particle", ""));

            if (element.Attribute("startrotation") == null)
            {
                AngleMin = ToolBox.GetAttributeFloat(element, "anglemin", 0.0f);
                AngleMax = ToolBox.GetAttributeFloat(element, "anglemax", 0.0f);
            }
            else
            {
                AngleMin = ToolBox.GetAttributeFloat(element, "angle", 0.0f);
                AngleMax = AngleMin;
            }

            AngleMin = MathHelper.ToRadians(AngleMin);
            AngleMax = MathHelper.ToRadians(AngleMax);

            if (element.Attribute("scalemin")==null)
            {
                ScaleMin = 1.0f;
                ScaleMax = 1.0f;
            }
            else
            {
                ScaleMin = ToolBox.GetAttributeFloat(element,"scalemin",1.0f);
                ScaleMax = Math.Max(ScaleMin, ToolBox.GetAttributeFloat(element, "scalemax", 1.0f));
            }

            if (element.Attribute("velocity") == null)
            {
                VelocityMin = ToolBox.GetAttributeFloat(element, "velocitymin", 0.0f);
                VelocityMax = ToolBox.GetAttributeFloat(element, "velocitymax", 0.0f);
            }
            else
            {
                VelocityMin = ToolBox.GetAttributeFloat(element, "velocity", 0.0f);
                VelocityMax = VelocityMin;
            }

            ParticlesPerSecond = ToolBox.GetAttributeInt(element, "particlespersecond", 0);
            ParticleAmount = ToolBox.GetAttributeInt(element, "particleamount", 0);
        }
    }
}
