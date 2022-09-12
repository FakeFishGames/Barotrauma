using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class ParticlePrefab : Prefab, ISerializableEntity
    {
        public static readonly PrefabCollection<ParticlePrefab> Prefabs = new PrefabCollection<ParticlePrefab>();

        public enum DrawTargetType { Air = 1, Water = 2, Both = 3 }

        public readonly List<Sprite> Sprites;

        public override void Dispose()
        {
            GameMain.ParticleManager?.RemoveByPrefab(this);
            foreach (Sprite spr in Sprites)
            {
                spr.Remove();
            }
            Sprites.Clear();
        }

        public string Name => Identifier.Value;
        
        [Editable(0.0f, float.MaxValue), Serialize(5.0f, IsPropertySaveable.No, description: "How many seconds the particle remains alive.")]
        public float LifeTime { get; private set; }

        [Editable(0.0f, float.MaxValue), Serialize(0.0f, IsPropertySaveable.No, description: "Will randomize lifetime value between lifetime and lifetimeMin. If left to 0 will use only lifetime value.")]
        public float LifeTimeMin { get; private set; }


        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How long it takes for the particle to appear after spawning it.")]
        public float StartDelayMin { get; private set; }
        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How long it takes for the particle to appear after spawning it.")]
        public float StartDelayMax { get; private set; }
        //movement -----------------------------------------

        private float angularVelocityMin;
        public float AngularVelocityMinRad { get; private set; }

        [Editable, Serialize(0.0f, IsPropertySaveable.No)]
        public float AngularVelocityMin
        {
            get { return angularVelocityMin; }
            private set
            {
                angularVelocityMin = value;
                AngularVelocityMinRad = MathHelper.ToRadians(value);
            }
        }

        private float angularVelocityMax;
        public float AngularVelocityMaxRad { get; private set; }

        [Editable, Serialize(0.0f, IsPropertySaveable.No)]
        public float AngularVelocityMax
        {
            get { return angularVelocityMax; }
            private set
            {
                angularVelocityMax = value;
                AngularVelocityMaxRad = MathHelper.ToRadians(value);
            }
        }

        private float startRotationMin;
        public float StartRotationMinRad { get; private set; }

        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "The minimum initial rotation of the particle (in degrees).")]
        public float StartRotationMin
        {
            get { return startRotationMin; }
            private set
            {
                startRotationMin = value;
                StartRotationMinRad = MathHelper.ToRadians(value);
            }
        }

        private float startRotationMax;
        public float StartRotationMaxRad { get; private set; }

        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "The maximum initial rotation of the particle (in degrees).")]
        public float StartRotationMax
        {
            get { return startRotationMax; }
            private set
            {
                startRotationMax = value;
                StartRotationMaxRad = MathHelper.ToRadians(value);
            }
        }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the particle face the direction it's moving towards.")]
        public bool RotateToDirection { get; private set; }

        [Editable(0.0f, float.MaxValue, DecimalCount = 3), Serialize(0.0f, IsPropertySaveable.No, description: "Drag applied to the particle when it's moving through air.")]
        public float Drag { get; private set; }

        [Editable(0.0f, float.MaxValue, DecimalCount = 3), Serialize(0.0f, IsPropertySaveable.No, description: "Drag applied to the particle when it's moving through water.")]
        public float WaterDrag { get; private set; }

        private Vector2 velocityChange;
        public Vector2 VelocityChangeDisplay { get; private set; }

        [Editable, Serialize("0.0,0.0", IsPropertySaveable.No, description: "How much the velocity of the particle changes per second.")]
        public Vector2 VelocityChange
        {
            get { return velocityChange; }
            private set
            {
                velocityChange = value;
                VelocityChangeDisplay = ConvertUnits.ToDisplayUnits(value);
            }
        }

        private Vector2 velocityChangeWater;
        public Vector2 VelocityChangeWaterDisplay { get; private set; }

        [Editable, Serialize("0.0,0.0", IsPropertySaveable.No, description: "How much the velocity of the particle changes per second when in water.")]
        public Vector2 VelocityChangeWater
        {
            get { return velocityChangeWater; }
            private set
            {
                velocityChangeWater = value;
                VelocityChangeWaterDisplay = ConvertUnits.ToDisplayUnits(value);
            }
        }

        [Editable(0.0f, 10000.0f), Serialize(0.0f, IsPropertySaveable.No, description: "Radius of the particle's collider. Only has an effect if UseCollision is set to true.")]
        public float CollisionRadius { get; private set; }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Does the particle collide with the walls of the submarine and the level.")]
        public bool UseCollision { get; private set; }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Does the particle disappear when it collides with something.")]
        public bool DeleteOnCollision { get; private set; }

        [Editable(0.0f, 1.0f), Serialize(0.5f, IsPropertySaveable.No, description: "The friction coefficient of the particle, i.e. how much it slows down when it's sliding against a surface.")]
        public float Friction { get; private set; }

        [Editable(0.0f, 1.0f)]
        [Serialize(0.5f, IsPropertySaveable.No, description: "How much of the particle's velocity is conserved when it collides with something, i.e. the \"bounciness\" of the particle. (1.0 = the particle stops completely).")]
        public float Restitution { get; private set; }

        //size -----------------------------------------

        [Editable, Serialize("1.0,1.0", IsPropertySaveable.No, description: "The minimum initial size of the particle.")]
        public Vector2 StartSizeMin { get; private set; }

        [Editable, Serialize("1.0,1.0", IsPropertySaveable.No, description: "The maximum initial size of the particle.")]
        public Vector2 StartSizeMax { get; private set; }
        
        [Editable, Serialize("0.0,0.0", IsPropertySaveable.No, description: "How much the size of the particle changes per second. The rate of growth for each particle is randomize between SizeChangeMin and SizeChangeMax.")]
        public Vector2 SizeChangeMin { get; private set; }

        [Editable, Serialize("0.0,0.0", IsPropertySaveable.No, description: "How much the size of the particle changes per second. The rate of growth for each particle is randomize between SizeChangeMin and SizeChangeMax.")]
        public Vector2 SizeChangeMax { get; private set; }

        [Editable, Serialize(0.0f, IsPropertySaveable.No, description: "How many seconds it takes for the particle to grow to it's initial size.")]
        public float GrowTime { get; private set; }

        //rendering -----------------------------------------

        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.No, description: "The initial color of the particle.")]
        public Color StartColor { get; private set; }

        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.No, description: "The initial color of the particle.")]
        public Color MiddleColor { get; private set; }

        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.No, description: "The color of the particle at the end of its lifetime.")]
        public Color EndColor { get; private set; }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "If true the color will go from StartColor to EndcColor and back to StartColor.")]
        public bool UseMiddleColor { get; private set; }
        
        [Editable, Serialize(DrawTargetType.Air, IsPropertySaveable.No, description: "Should the particle be rendered in air, water or both.")]
        public DrawTargetType DrawTarget { get; private set; }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the particle be always rendered on top of entities?")]
        public bool DrawOnTop { get; private set; }

        [Editable, Serialize(ParticleBlendState.AlphaBlend, IsPropertySaveable.No, description: "The type of blending to use when rendering the particle.")]
        public ParticleBlendState BlendState { get; private set; }

        [Editable, Serialize(0, IsPropertySaveable.No, description: "Particles with a higher priority can replace lower-priority ones if the maximum number of active particles has been reached.")]
        public int Priority { get; private set; }

        //animation -----------------------------------------

        [Editable(0.0f, float.MaxValue), Serialize(1.0f, IsPropertySaveable.No, description: "The duration of the particle's animation cycle (if it's animated).")]
        public float AnimDuration { get; private set; }

        [Editable, Serialize(true, IsPropertySaveable.No, description: "Should the sprite animation be looped, or stay at the last frame when the animation finishes.")]
        public bool LoopAnim { get; private set; }

        //----------------------------------------------------
        
        public readonly List<ParticleEmitterPrefab> SubEmitters = new List<ParticleEmitterPrefab>();

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        //----------------------------------------------------

        public ParticlePrefab(ContentXElement element, ContentFile file) : base(file, element)
        {
            Sprites = new List<Sprite>();

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprites.Add(new Sprite(subElement));
                        break;
                    case "spritesheet":
                    case "animatedsprite":
                        Sprites.Add(new SpriteSheet(subElement));
                        break;
                    case "particleemitter":
                    case "emitter":
                    case "subemitter":
                        SubEmitters.Add(new ParticleEmitterPrefab(subElement));
                        break;
                }
            }

            if (Sprites.Count == 0)
            {
                DebugConsole.ThrowError($"Particle prefab \"{Name}\" in the file \"{file}\" has no sprites defined!");
            }

            //if velocity change in water is not given, it defaults to the normal velocity change
            if (element.GetAttribute("velocitychangewater") == null)
            {
                VelocityChangeWater = VelocityChange;
            }

            if (element.GetAttribute("angularvelocity") != null)
            {
                AngularVelocityMin = element.GetAttributeFloat("angularvelocity", 0.0f);
                AngularVelocityMax = AngularVelocityMin;
            }

            if (element.GetAttribute("startsize") != null)
            {
                StartSizeMin = element.GetAttributeVector2("startsize", Vector2.One);
                StartSizeMax = StartSizeMin;
            }

            if (element.GetAttribute("sizechange") != null)
            {
                SizeChangeMin = element.GetAttributeVector2("sizechange", Vector2.Zero);
                SizeChangeMax = SizeChangeMin;
            }

            if (element.GetAttribute("startrotation") != null)
            {
                StartRotationMin = element.GetAttributeFloat("startrotation", 0.0f);
                StartRotationMax = StartRotationMin;
            }

            if (CollisionRadius <= 0.0f && UseCollision)
            {
                CollisionRadius = Sprites.Count > 0 ? Sprites[0].SourceRect.Width / 2.0f : 1;
            }
        }

		protected override Identifier DetermineIdentifier(XElement element)
		{
			return element.NameAsIdentifier();
		}

		public Vector2 CalculateEndPosition(Vector2 startPosition, Vector2 velocity)
        {
            //endPos = x + vt + 1/2 * at^2
            return startPosition + velocity * LifeTime + 0.5f * VelocityChangeDisplay * LifeTime * LifeTime;
        }

        public Vector2 CalculateEndSize()
        {
            //endPos = x + vt + 1/2 * at^2
            return StartSizeMax + 0.5f * SizeChangeMax * LifeTime * LifeTime;
        }
    }
}
