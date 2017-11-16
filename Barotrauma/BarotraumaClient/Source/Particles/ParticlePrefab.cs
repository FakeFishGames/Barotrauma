using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class ParticlePrefab
    {
        public enum DrawTargetType { Air = 1, Water = 2, Both = 3 }

        public readonly string Name;

        public readonly List<Sprite> Sprites;

        //movement -----------------------------------------

        private float angularVelocityMin;
        public float AngularVelocityMinRad { get; private set; }

        [Serialize(0.0f, false)]
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

        [Serialize(0.0f, false)]
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

        [Serialize(0.0f, false)]
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

        [Serialize(0.0f, false)]
        public float StartRotationMax
        {
            get { return startRotationMax; }
            private set
            {
                startRotationMax = value;
                StartRotationMaxRad = MathHelper.ToRadians(value);
            }
        }

        [Serialize(false, false)]
        public bool RotateToDirection { get; private set; }

        [Serialize(0.0f, false)]
        public float Drag { get; private set; }

        [Serialize(0.0f, false)]
        public float WaterDrag { get; private set; }

        private Vector2 velocityChange;
        public Vector2 VelocityChangeDisplay { get; private set; }

        [Serialize("0.0,0.0", false)]
        public Vector2 VelocityChange
        {
            get { return velocityChange; }
            private set
            {
                velocityChange = value;
                VelocityChangeDisplay = ConvertUnits.ToDisplayUnits(value);
            }
        }

        [Serialize(0.0f, false)]
        public float CollisionRadius { get; private set; }

        [Serialize(false, false)]
        public bool DeleteOnCollision { get; private set; }

        [Serialize(false, false)]
        public bool CollidesWithWalls { get; private set; }

        [Serialize(0.5f, false)]
        public float Friction { get; private set; }

        [Serialize(0.5f, false)]
        public float Restitution { get; private set; }

        //size -----------------------------------------

        [Serialize("1.0,1.0", false)]
        public Vector2 StartSizeMin { get; private set; }

        [Serialize("1.0,1.0", false)]
        public Vector2 StartSizeMax { get; private set; }

        [Serialize("0.0,0.0", false)]
        public Vector2 SizeChangeMin { get; private set; }

        [Serialize("0.0,0.0", false)]
        public Vector2 SizeChangeMax { get; private set; }

        [Serialize(0.0f, false)]
        public float GrowTime { get; private set; }

        //rendering -----------------------------------------

        [Serialize("1.0,1.0,1.0,1.0", false)]
        public Color StartColor { get; private set; }

        [Serialize(1.0f, false)]
        public float StartAlpha { get; private set; }

        [Serialize("0.0,0.0,0.0,0.0", false)]
        public Vector4 ColorChange { get; private set; }

        [Serialize(DrawTargetType.Air, false)]
        public DrawTargetType DrawTarget { get; private set; }

        [Serialize(ParticleBlendState.AlphaBlend, false)]
        public ParticleBlendState BlendState { get; private set; }
        
        //animation -----------------------------------------

        [Serialize(1.0f, false)]
        public float AnimDuration { get; private set; }

        [Serialize(true, false)]
        public bool LoopAnim { get; private set; }

        //misc -----------------------------------------

        [Serialize(5.0f, false)]
        public float LifeTime { get; private set; }

        //----------------------------------------------------

        public ParticlePrefab(XElement element)
        {
            Name = element.Name.ToString();

            Sprites = new List<Sprite>();

            SerializableProperty.DeserializeProperties(this, element);

            foreach (XElement subElement in element.Elements())
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
                }
            }

            if (element.Attribute("angularvelocity") != null)
            {
                AngularVelocityMin = element.GetAttributeFloat("angularvelocity", 0.0f);
                AngularVelocityMax = AngularVelocityMin;
            }

            if (element.Attribute("startsize") != null)
            {
                StartSizeMin = element.GetAttributeVector2("startsize", Vector2.One);
                StartSizeMax = StartSizeMin;
            }

            if (element.Attribute("sizechange") != null)
            {
                SizeChangeMin = element.GetAttributeVector2("sizechange", Vector2.Zero);
                SizeChangeMax = SizeChangeMin;
            }

            if (element.Attribute("startrotation") != null)
            {
                StartRotationMin = element.GetAttributeFloat("startrotation", 0.0f);
                StartRotationMax = StartRotationMin;
            }
            
            if (CollisionRadius <= 0.0f) CollisionRadius = Sprites.Count > 0 ? 1 : Sprites[0].SourceRect.Width / 2.0f;
        }

        public Vector2 CalculateEndPosition(Vector2 startPosition, Vector2 velocity)
        {
            //endPos = x + vt + 1/2 * at^2
            return startPosition + velocity * LifeTime + 0.5f * VelocityChangeDisplay * LifeTime * LifeTime;
        }
    }
}
