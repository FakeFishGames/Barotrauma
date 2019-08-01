using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class ParticleEmitter
    {
        private float emitTimer;
        private float burstEmitTimer;

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

        public void Emit(float deltaTime, Vector2 position, Hull hullGuess = null, float angle = 0.0f, float particleRotation = 0.0f, float velocityMultiplier = 1.0f, float sizeMultiplier = 1.0f, float amountMultiplier = 1.0f)
        {
            emitTimer += deltaTime * amountMultiplier;
            burstEmitTimer += deltaTime;

            if (Prefab.ParticlesPerSecond > 0)
            {
                float emitInterval = 1.0f / Prefab.ParticlesPerSecond;
                while (emitTimer > emitInterval)
                {
                    Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier);
                    emitTimer -= emitInterval;
                }
            }

            if (burstEmitTimer < Prefab.EmitInterval) return;
            burstEmitTimer = 0.0f;

            for (int i = 0; i < Prefab.ParticleAmount * amountMultiplier; i++)
            {
                Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier);
            }
        }

        private void Emit(Vector2 position, Hull hullGuess, float angle, float particleRotation, float velocityMultiplier, float sizeMultiplier)
        {
            angle += Rand.Range(Prefab.AngleMin, Prefab.AngleMax);
            Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Rand.Range(Prefab.VelocityMin, Prefab.VelocityMax) * velocityMultiplier;

            var particle = GameMain.ParticleManager.CreateParticle(Prefab.ParticlePrefab, position, velocity, particleRotation, hullGuess);

            if (particle != null)
            {
                particle.Size *= Rand.Range(Prefab.ScaleMin, Prefab.ScaleMax);
                particle.HighQualityCollisionDetection = Prefab.HighQualityCollisionDetection;
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

        public readonly float EmitInterval;
        public readonly int ParticleAmount;

        public readonly float ParticlesPerSecond;

        public readonly bool HighQualityCollisionDetection;

        public readonly bool CopyEntityAngle;

        public ParticleEmitterPrefab(XElement element)
        {
            Name = element.Name.ToString();

            ParticlePrefab = GameMain.ParticleManager.FindPrefab(element.GetAttributeString("particle", ""));

            if (element.Attribute("startrotation") == null)
            {
                AngleMin = element.GetAttributeFloat("anglemin", 0.0f);
                AngleMax = element.GetAttributeFloat("anglemax", 0.0f);
            }
            else
            {
                AngleMin = element.GetAttributeFloat("angle", 0.0f);
                AngleMax = AngleMin;
            }

            AngleMin = MathHelper.ToRadians(MathHelper.Clamp(AngleMin, -360.0f, 360.0f));
            AngleMax = MathHelper.ToRadians(MathHelper.Clamp(AngleMax, -360.0f, 360.0f));

            if (element.Attribute("scalemin") == null)
            {
                ScaleMin = 1.0f;
                ScaleMax = 1.0f;
            }
            else
            {
                ScaleMin = element.GetAttributeFloat("scalemin", 1.0f);
                ScaleMax = Math.Max(ScaleMin, element.GetAttributeFloat("scalemax", 1.0f));
            }

            if (element.Attribute("velocity") == null)
            {
                VelocityMin = element.GetAttributeFloat("velocitymin", 0.0f);
                VelocityMax = element.GetAttributeFloat("velocitymax", 0.0f);
            }
            else
            {
                VelocityMin = element.GetAttributeFloat("velocity", 0.0f);
                VelocityMax = VelocityMin;
            }

            EmitInterval = element.GetAttributeFloat("emitinterval", 0.0f);
            ParticlesPerSecond = element.GetAttributeInt("particlespersecond", 0);
            ParticleAmount = element.GetAttributeInt("particleamount", 0);
            HighQualityCollisionDetection = element.GetAttributeBool("highqualitycollisiondetection", false);
            CopyEntityAngle = element.GetAttributeBool("copyentityangle", false);
        }
    }
}
