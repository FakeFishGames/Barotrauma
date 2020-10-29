using Microsoft.Xna.Framework;
using System;
using System.Linq;
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

        public void Emit(float deltaTime, Vector2 position, Hull hullGuess = null, float angle = 0.0f, float particleRotation = 0.0f, float velocityMultiplier = 1.0f, float sizeMultiplier = 1.0f, float amountMultiplier = 1.0f, Color? colorMultiplier = null, ParticlePrefab overrideParticle = null)
        {
            emitTimer += deltaTime * amountMultiplier;
            burstEmitTimer -= deltaTime;

            if (Prefab.ParticlesPerSecond > 0)
            {
                float emitInterval = 1.0f / Prefab.ParticlesPerSecond;
                while (emitTimer > emitInterval)
                {
                    Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier, colorMultiplier, overrideParticle);
                    emitTimer -= emitInterval;
                }
            }

            if (burstEmitTimer > 0.0f) { return; }
            
            burstEmitTimer = Prefab.EmitInterval;
            for (int i = 0; i < Prefab.ParticleAmount * amountMultiplier; i++)
            {
                Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier, colorMultiplier, overrideParticle);
            }
        }

        private void Emit(Vector2 position, Hull hullGuess, float angle, float particleRotation, float velocityMultiplier, float sizeMultiplier, Color? colorMultiplier = null, ParticlePrefab overrideParticle = null)
        {
            angle += Rand.Range(Prefab.AngleMin, Prefab.AngleMax);

            Vector2 dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            Vector2 velocity = dir * Rand.Range(Prefab.VelocityMin, Prefab.VelocityMax) * velocityMultiplier;
            position += dir * Rand.Range(Prefab.DistanceMin, Prefab.DistanceMax);

            var particle = GameMain.ParticleManager.CreateParticle(overrideParticle ?? Prefab.ParticlePrefab, position, velocity, particleRotation, hullGuess, Prefab.DrawOnTop);

            if (particle != null)
            {
                particle.Size *= Rand.Range(Prefab.ScaleMin, Prefab.ScaleMax) * sizeMultiplier;
                particle.HighQualityCollisionDetection = Prefab.HighQualityCollisionDetection;
                if (colorMultiplier.HasValue) 
                { 
                    particle.ColorMultiplier = colorMultiplier.Value.ToVector4(); 
                }
                else if (Prefab.ColorMultiplier != Color.White)
                {
                    particle.ColorMultiplier = Prefab.ColorMultiplier.ToVector4();
                }
            }
        }

        public Rectangle CalculateParticleBounds(Vector2 startPosition)
        {
            Rectangle bounds = new Rectangle((int)startPosition.X, (int)startPosition.Y, (int)startPosition.X, (int)startPosition.Y);

            for (float angle = Prefab.AngleMin; angle <= Prefab.AngleMax; angle += 0.1f)
            {
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Prefab.VelocityMax;
                Vector2 endPosition = Prefab.ParticlePrefab.CalculateEndPosition(startPosition, velocity);

                Vector2 endSize = Prefab.ParticlePrefab.CalculateEndSize();
                float spriteExtent = 0.0f;
                foreach (Sprite sprite in Prefab.ParticlePrefab.Sprites)
                {
                    if (sprite is SpriteSheet spriteSheet)
                    {
                        spriteExtent = Math.Max(spriteExtent, Math.Max(spriteSheet.FrameSize.X * endSize.X, spriteSheet.FrameSize.Y * endSize.Y));
                    }
                    else
                    {
                        spriteExtent = Math.Max(spriteExtent, Math.Max(sprite.size.X * endSize.X, sprite.size.Y * endSize.Y)); 
                    }
                }

                bounds = new Rectangle(
                    (int)Math.Min(bounds.X, endPosition.X - Prefab.DistanceMax - spriteExtent / 2),
                    (int)Math.Min(bounds.Y, endPosition.Y - Prefab.DistanceMax - spriteExtent / 2),
                    (int)Math.Max(bounds.X, endPosition.X + Prefab.DistanceMax + spriteExtent / 2),
                    (int)Math.Max(bounds.Y, endPosition.Y + Prefab.DistanceMax + spriteExtent / 2));
            }

            bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width - bounds.X, bounds.Height - bounds.Y);

            return bounds;
        }
    }

    class ParticleEmitterPrefab
    {        
        public readonly string Name;

        private string particlePrefabName;

        private ParticlePrefab particlePrefab;
        public ParticlePrefab ParticlePrefab
        {
            get
            {
                if (particlePrefab == null && particlePrefabName != null)
                {
                    particlePrefab = GameMain.ParticleManager?.FindPrefab(particlePrefabName);
                    if (particlePrefab == null) { particlePrefabName = null; }
                }
                return particlePrefab;
            }
        }

        public readonly float AngleMin, AngleMax;

        public readonly float DistanceMin, DistanceMax;

        public readonly float VelocityMin, VelocityMax;

        public readonly float ScaleMin, ScaleMax;

        public readonly float EmitInterval;
        public readonly int ParticleAmount;

        public readonly float ParticlesPerSecond;

        public readonly bool HighQualityCollisionDetection;

        public readonly bool CopyEntityAngle;

        public readonly Color ColorMultiplier;

        public bool DrawOnTop => forceDrawOnTop || ParticlePrefab.DrawOnTop;
        private readonly bool forceDrawOnTop;

        public ParticleEmitterPrefab(XElement element)
        {
            Name = element.Name.ToString();
            particlePrefabName = element.GetAttributeString("particle", "");

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

            if (element.Attribute("distance") == null)
            {
                DistanceMin = element.GetAttributeFloat("distancemin", 0.0f);
                DistanceMax = element.GetAttributeFloat("distancemax", 0.0f);
            }
            else
            {
                DistanceMin = DistanceMax = element.GetAttributeFloat("distance", 0.0f);
            }
            if (DistanceMax < DistanceMin)
            {
                var temp = DistanceMin;
                DistanceMin = DistanceMax;
                DistanceMax = temp;
            }

            if (element.Attribute("velocity") == null)
            {
                VelocityMin = element.GetAttributeFloat("velocitymin", 0.0f);
                VelocityMax = element.GetAttributeFloat("velocitymax", 0.0f);
            }
            else
            {
                VelocityMin = VelocityMax = element.GetAttributeFloat("velocity", 0.0f);
            }
            if (VelocityMax < VelocityMin)
            {
                var temp = VelocityMin;
                VelocityMin = VelocityMax;
                VelocityMax = temp;
            }

            EmitInterval = element.GetAttributeFloat("emitinterval", 0.0f);
            ParticlesPerSecond = element.GetAttributeFloat("particlespersecond", 0);
            ParticleAmount = element.GetAttributeInt("particleamount", 0);
            HighQualityCollisionDetection = element.GetAttributeBool("highqualitycollisiondetection", false);
            CopyEntityAngle = element.GetAttributeBool("copyentityangle", false);
            forceDrawOnTop = element.GetAttributeBool("drawontop", false); 
            ColorMultiplier = element.GetAttributeColor("colormultiplier", Color.White);
        }
    }
}
