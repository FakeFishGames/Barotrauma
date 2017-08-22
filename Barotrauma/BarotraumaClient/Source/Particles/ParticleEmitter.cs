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
            System.Diagnostics.Debug.Assert(prefab != null, "The prefab of a particle emitter cannot be null");
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

        public Rectangle CalculateParticleBounds(Vector2 startPosition)
        {
            Rectangle bounds = new Rectangle((int)startPosition.X, (int)startPosition.Y, (int)startPosition.X, (int)startPosition.Y);

            for (float angle = Prefab.AngleMin; angle <= Prefab.AngleMax; angle += 0.1f)
            {
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Prefab.VelocityMax;
                Vector2 endPosition = Prefab.ParticlePrefab.CalculateEndPosition(startPosition, velocity);

                bounds = new Rectangle(
                    (int)Math.Min(bounds.X, endPosition.X),
                    (int)Math.Min(bounds.Y, endPosition.Y),
                    (int)Math.Max(bounds.X, endPosition.X),
                    (int)Math.Max(bounds.Y, endPosition.Y));
            }

            bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width - bounds.X, bounds.Height - bounds.Y);

            return bounds;
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

            AngleMin = MathHelper.ToRadians(MathHelper.Clamp(AngleMin, -360.0f, 360.0f));
            AngleMax = MathHelper.ToRadians(MathHelper.Clamp(AngleMax, -360.0f, 360.0f));

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
