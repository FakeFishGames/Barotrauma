using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class ParticleEmitterProperties : ISerializableEntity
    {
        private const float MinValue = int.MinValue,
                            MaxValue = int.MaxValue;

        public string Name => nameof(ParticleEmitter);

        private float angleMin, angleMax;

        public float AngleMinRad { get; private set; }
        public float AngleMaxRad { get; private set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 360, MinValueFloat = -360f), Serialize(0f, IsPropertySaveable.Yes)]
        public float AngleMin
        {
            get => angleMin;
            set
            {
                angleMin = value;
                AngleMinRad = MathHelper.ToRadians(MathHelper.Clamp(value, -360.0f, 360.0f));
            }
        }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 360, MinValueFloat = -360f), Serialize(0f, IsPropertySaveable.Yes)]
        public float AngleMax
        {
            get => angleMax;
            set
            {
                angleMax = value;
                AngleMaxRad = MathHelper.ToRadians(MathHelper.Clamp(value, -360.0f, 360.0f));
            }
        }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = MaxValue, MinValueFloat = MinValue), Serialize(0f, IsPropertySaveable.Yes)]
        public float DistanceMin { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = MaxValue, MinValueFloat = MinValue), Serialize(0f, IsPropertySaveable.Yes)]
        public float DistanceMax { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = MaxValue, MinValueFloat = MinValue), Serialize(0f, IsPropertySaveable.Yes)]
        public float VelocityMin { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = MaxValue, MinValueFloat = MinValue), Serialize(0f, IsPropertySaveable.Yes)]
        public float VelocityMax { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 100.0f, MinValueFloat = 0.0f), Serialize(1f, IsPropertySaveable.Yes)]
        public float ScaleMin { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 100.0f, MinValueFloat = 0.0f), Serialize(1f, IsPropertySaveable.Yes)]
        public float ScaleMax { get; set; }


        [Editable(), Serialize("1,1", IsPropertySaveable.Yes)]
        public Vector2 ScaleMultiplier { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 100.0f, MinValueFloat = 0.0f), Serialize(0f, IsPropertySaveable.Yes)]
        public float EmitInterval { get; set; }

        [Editable(ValueStep = 1, MinValueInt = 0, MaxValueInt = 1000), Serialize(0, IsPropertySaveable.Yes, description: "The number of particles to spawn per frame, or every x seconds if EmitInterval is set.")]
        public int ParticleAmount { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 1000.0f, MinValueFloat = 0.0f), Serialize(0f, IsPropertySaveable.Yes)]
        public float ParticlesPerSecond { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 10.0f, MinValueFloat = 0.0f), Serialize(0f, IsPropertySaveable.Yes, description: "If larger than 0, a particle is spawned every x pixels across the ray cast by a hitscan weapon.")]
        public float EmitAcrossRayInterval { get; set; }

        [Editable(ValueStep = 1, DecimalCount = 2, MaxValueFloat = 100.0f, MinValueFloat = 0.0f), Serialize(0f, IsPropertySaveable.Yes, description: "Delay before the emitter becomes active after being created.")]
        public float InitialDelay { get; set; }

        [Editable, Serialize(false, IsPropertySaveable.Yes)]
        public bool HighQualityCollisionDetection { get; set; }

        [Editable, Serialize(false, IsPropertySaveable.Yes)]
        public bool CopyEntityAngle { get; set; }

        [Editable, Serialize("1,1,1,1", IsPropertySaveable.Yes)]
        public Color ColorMultiplier { get; set; }

        [Editable, Serialize(false, IsPropertySaveable.Yes)]
        public bool DrawOnTop { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float Angle
        {
            get => AngleMin;
            set => AngleMin = AngleMax = value;
        }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float Distance
        {
            get => DistanceMin;
            set => DistanceMin = DistanceMax = value;
        }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float Velocity
        {
            get => VelocityMin;
            set => VelocityMin = VelocityMax = value;
        }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public ParticleEmitterProperties(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }

    class ParticleEmitter
    {
        private float emitTimer;
        private float burstEmitTimer;
        private float initialDelay;

        public readonly ParticleEmitterPrefab Prefab;

        public ParticleEmitter(ContentXElement element)
        {
            Prefab = new ParticleEmitterPrefab(element);
        }

        public ParticleEmitter(ParticleEmitterPrefab prefab)
        {
            System.Diagnostics.Debug.Assert(prefab != null, "The prefab of a particle emitter cannot be null");
            Prefab = prefab;
        }

        public void Emit(float deltaTime, Vector2 position, Hull hullGuess = null, float angle = 0.0f, float particleRotation = 0.0f, float velocityMultiplier = 1.0f, float sizeMultiplier = 1.0f, float amountMultiplier = 1.0f, Color? colorMultiplier = null, ParticlePrefab overrideParticle = null, bool mirrorAngle = false, Tuple<Vector2, Vector2> tracerPoints = null)
        {
            if (GameMain.Client?.MidRoundSyncing ?? false) { return; }

            if (initialDelay < Prefab.Properties.InitialDelay)
            {
                initialDelay += deltaTime;
                return;
            }

            emitTimer += deltaTime * amountMultiplier;
            burstEmitTimer -= deltaTime;

            if (Prefab.Properties.EmitAcrossRayInterval > 0.0f && tracerPoints != null)
            {
                Vector2 dir = tracerPoints.Item2 - tracerPoints.Item1;
                if (dir.LengthSquared() > 0.001f)
                {
                    float dist = dir.Length();
                    dir /= dist;
                    for (float z = 0.0f; z < dist; z += Prefab.Properties.EmitAcrossRayInterval)
                    {
                        Vector2 pos = tracerPoints.Item1 + dir * z;
                        Emit(pos, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier, colorMultiplier, overrideParticle, mirrorAngle, tracerPoints: null);
                    }
                }
            }

            if (Prefab.Properties.ParticlesPerSecond > 0)
            {
                float emitInterval = 1.0f / Prefab.Properties.ParticlesPerSecond;
                while (emitTimer > emitInterval)
                {
                    Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier, colorMultiplier, overrideParticle, mirrorAngle, tracerPoints: tracerPoints);
                    emitTimer -= emitInterval;
                }
            }

            if (burstEmitTimer > 0.0f) { return; }

            burstEmitTimer = Prefab.Properties.EmitInterval;
            for (int i = 0; i < Prefab.Properties.ParticleAmount * amountMultiplier; i++)
            {
                Emit(position, hullGuess, angle, particleRotation, velocityMultiplier, sizeMultiplier, colorMultiplier, overrideParticle, tracerPoints: tracerPoints);
            }
        }

        private void Emit(Vector2 position, Hull hullGuess, float angle, float particleRotation, float velocityMultiplier, float sizeMultiplier, Color? colorMultiplier = null, ParticlePrefab overrideParticle = null, bool mirrorAngle = false, Tuple<Vector2, Vector2> tracerPoints = null)
        {
            var particlePrefab = overrideParticle ?? Prefab.ParticlePrefab;
            if (particlePrefab == null) { return; }

            Vector2 velocity = Vector2.Zero;
            if (!MathUtils.NearlyEqual(Prefab.Properties.VelocityMax * velocityMultiplier, 0.0f) || !MathUtils.NearlyEqual(Prefab.Properties.DistanceMax, 0.0f))
            {
                angle += Rand.Range(Prefab.Properties.AngleMinRad, Prefab.Properties.AngleMaxRad) * (mirrorAngle ? -1 : 1);
                Vector2 dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                velocity = dir * Rand.Range(Prefab.Properties.VelocityMin, Prefab.Properties.VelocityMax) * velocityMultiplier;
                position += dir * Rand.Range(Prefab.Properties.DistanceMin, Prefab.Properties.DistanceMax);
            }

            var particle = GameMain.ParticleManager.CreateParticle(particlePrefab, position, velocity, particleRotation, hullGuess, Prefab.DrawOnTop, tracerPoints: tracerPoints);

            if (particle != null)
            {
                particle.Size *= Rand.Range(Prefab.Properties.ScaleMin, Prefab.Properties.ScaleMax) * sizeMultiplier;
                particle.Size *= Prefab.Properties.ScaleMultiplier;
                particle.HighQualityCollisionDetection = Prefab.Properties.HighQualityCollisionDetection;
                if (colorMultiplier.HasValue)
                {
                    particle.ColorMultiplier = colorMultiplier.Value.ToVector4();
                }
                else if (Prefab.Properties.ColorMultiplier != Color.White)
                {
                    particle.ColorMultiplier = Prefab.Properties.ColorMultiplier.ToVector4();
                }
            }
        }

        public Rectangle CalculateParticleBounds(Vector2 startPosition)
        {
            Rectangle bounds = new Rectangle((int)startPosition.X, (int)startPosition.Y, (int)startPosition.X, (int)startPosition.Y);

            for (float angle = Prefab.Properties.AngleMinRad; angle <= Prefab.Properties.AngleMaxRad; angle += 0.1f)
            {
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Prefab.Properties.VelocityMax;
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
                    (int)Math.Min(bounds.X, endPosition.X - Prefab.Properties.DistanceMax - spriteExtent / 2),
                    (int)Math.Min(bounds.Y, endPosition.Y - Prefab.Properties.DistanceMax - spriteExtent / 2),
                    (int)Math.Max(bounds.X, endPosition.X + Prefab.Properties.DistanceMax + spriteExtent / 2),
                    (int)Math.Max(bounds.Y, endPosition.Y + Prefab.Properties.DistanceMax + spriteExtent / 2));
            }

            bounds = new Rectangle(bounds.X, bounds.Y, bounds.Width - bounds.X, bounds.Height - bounds.Y);

            return bounds;
        }
    }

    class ParticleEmitterPrefab
    {
        private readonly Identifier particlePrefabName;

        public ParticlePrefab ParticlePrefab => ParticlePrefab.Prefabs[particlePrefabName];

        public readonly ParticleEmitterProperties Properties;

        public bool DrawOnTop => Properties.DrawOnTop || ParticlePrefab.DrawOnTop;

        public ParticleEmitterPrefab(ContentXElement element)
        {
            Properties = new ParticleEmitterProperties(element);
            particlePrefabName = element.GetAttributeIdentifier("particle", "");
        }

        public ParticleEmitterPrefab(ParticlePrefab prefab, ParticleEmitterProperties properties)
        {
            Properties = properties;
            particlePrefabName = prefab.Identifier;
        }
    }
}
