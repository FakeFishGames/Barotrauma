using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Barotrauma
{
    class DecorativeSprite : ISerializableEntity
    {
        public class State
        {
            public float RotationState;
            public float OffsetState;
            public Vector2 RandomOffsetMultiplier = new Vector2(Rand.Range(-1.0f, 1.0f), Rand.Range(-1.0f, 1.0f));
            public float RandomRotationFactor = Rand.Range(0.0f, 1.0f);
            public float RandomScaleFactor = Rand.Range(0.0f, 1.0f);
            public bool IsActive = true;
        }

        public string Name => $"Decorative Sprite";
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

        public Sprite Sprite { get; private set; }

        public enum AnimationType
        {
            None,
            Sine,
            Noise
        }

        [Serialize("0,0", IsPropertySaveable.Yes), Editable]
        public Vector2 Offset { get; private set; }

        [Serialize("0,0", IsPropertySaveable.Yes), Editable]
        public Vector2 RandomOffset { get; private set; }

        [Serialize(AnimationType.None, IsPropertySaveable.No), Editable]
        public AnimationType OffsetAnim { get; private set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float OffsetAnimSpeed { get; private set; }

        private float rotationSpeedRadians;
        private float absRotationSpeedRadians;

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float RotationSpeed
        {
            get
            {
                return MathHelper.ToDegrees(rotationSpeedRadians);
            }
            private set
            {
                rotationSpeedRadians = MathHelper.ToRadians(value);
                absRotationSpeedRadians = Math.Abs(rotationSpeedRadians);
            }
        }

        private float rotationRadians;
        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float Rotation
        {
            get
            {
                return MathHelper.ToDegrees(rotationRadians);
            }
            private set
            {
                rotationRadians = MathHelper.ToRadians(value);
            }
        }

        private Vector2 randomRotationRadians;
        [Serialize("0,0", IsPropertySaveable.Yes), Editable]
        public Vector2 RandomRotation
        {
            get
            {
                return new Vector2(MathHelper.ToDegrees(randomRotationRadians.X), MathHelper.ToDegrees(randomRotationRadians.Y));
            }
            private set
            {
                randomRotationRadians = new Vector2(MathHelper.ToRadians(value.X), MathHelper.ToRadians(value.Y));
            }
        }

        private float scale;
        [Serialize(1.0f, IsPropertySaveable.Yes), Editable]
        public float Scale
        {
            get { return scale; }
            private set { scale = MathHelper.Clamp(value, 0.0f, 10.0f); }
        }

        [Serialize("0,0", IsPropertySaveable.Yes), Editable]
        public Vector2 RandomScale
        {
            get;
            private set;
        }

        [Serialize(AnimationType.None, IsPropertySaveable.No), Editable]
        public AnimationType RotationAnim { get; private set; }

        /// <summary>
        /// If > 0, only one sprite of the same group is used (chosen randomly)
        /// </summary>
        [Serialize(0, IsPropertySaveable.No, description: "If > 0, only one sprite of the same group is used (chosen randomly)"), Editable(ReadOnly = true)]
        public int RandomGroupID { get; private set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes), Editable()]
        public Color Color { get; set; }

        /// <summary>
        /// The sprite is only drawn if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> IsActiveConditionals { get; private set; } = new List<PropertyConditional>();
        /// <summary>
        /// The sprite is only animated if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> AnimationConditionals { get; private set; } = new List<PropertyConditional>();

        public DecorativeSprite(ContentXElement element, string path = "", string file = "", bool lazyLoad = false)
        {
            Sprite = new Sprite(element, path, file, lazyLoad: lazyLoad);
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            // load property conditionals
            foreach (var subElement in element.Elements())
            {
                //choose which list the new conditional should be placed to
                List<PropertyConditional> conditionalList = null;
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional":
                    case "isactiveconditional":
                        conditionalList = IsActiveConditionals;
                        break;
                    case "animationconditional":
                        conditionalList = AnimationConditionals;
                        break;
                    default:
                        continue;
                }
                foreach (XAttribute attribute in subElement.Attributes())
                {
                    if (PropertyConditional.IsValid(attribute))
                    {
                        conditionalList.Add(new PropertyConditional(attribute));
                    }
                }
            }
        }

        public Vector2 GetOffset(ref float offsetState, Vector2 randomOffsetMultiplier, float rotation = 0.0f)
        {
            Vector2 offset = Offset;
            if (OffsetAnimSpeed > 0.0f)
            {
                switch (OffsetAnim)
                {
                    case AnimationType.Sine:
                        offsetState %= (MathHelper.TwoPi / OffsetAnimSpeed);
                        offset *= (float)Math.Sin(offsetState * OffsetAnimSpeed);
                        break;
                    case AnimationType.Noise:
                        offsetState %= 1.0f / (OffsetAnimSpeed * 0.1f);

                        float t = offsetState * 0.1f * OffsetAnimSpeed;
                        offset = new Vector2(
                            offset.X * (PerlinNoise.GetPerlin(t, t) - 0.5f),
                            offset.Y * (PerlinNoise.GetPerlin(t + 0.5f, t + 0.5f) - 0.5f));
                        break;
                }
            }
            offset += new Vector2(
                RandomOffset.X * randomOffsetMultiplier.X, 
                RandomOffset.Y * randomOffsetMultiplier.Y);
            if (Math.Abs(rotation) > 0.01f)
            {
                Matrix transform = Matrix.CreateRotationZ(rotation);
                offset = Vector2.Transform(offset, transform);
            }
            return offset;
        }

        public float GetRotation(ref float rotationState, float randomRotationFactor)
        {
            RotationSpeed = -Math.Abs(RotationSpeed);
            switch (RotationAnim)
            {
                case AnimationType.Sine:
                    rotationState %= MathHelper.TwoPi / absRotationSpeedRadians;
                    return 
                        rotationRadians * (float)Math.Sin(rotationState * rotationSpeedRadians)
                        + MathHelper.Lerp(randomRotationRadians.X, randomRotationRadians.Y, randomRotationFactor);
                case AnimationType.Noise:
                    rotationState %= 1.0f / absRotationSpeedRadians;
                    return 
                        rotationRadians * (PerlinNoise.GetPerlin(rotationState * absRotationSpeedRadians, rotationState * absRotationSpeedRadians) - 0.5f)
                        + MathHelper.Lerp(randomRotationRadians.X, randomRotationRadians.Y, randomRotationFactor);
                default:
                    return  
                        rotationRadians + 
                        rotationState * rotationSpeedRadians
                        + MathHelper.Lerp(randomRotationRadians.X, randomRotationRadians.Y, randomRotationFactor);
            }
        }

        public float GetScale(float randomScaleModifier)
        {
            if (RandomScale == Vector2.Zero)
            { 
                return scale;
            }
            return MathHelper.Lerp(RandomScale.X, RandomScale.Y, randomScaleModifier);
        }

        public static void UpdateSpriteStates(ImmutableDictionary<int, ImmutableArray<DecorativeSprite>> spriteGroups, Dictionary<DecorativeSprite, State> animStates,
            int entityID, float deltaTime, Func<PropertyConditional, bool> checkConditional)
        {
            foreach (int spriteGroup in spriteGroups.Keys)
            {
                for (int i = 0; i < spriteGroups[spriteGroup].Length; i++)
                {
                    var decorativeSprite = spriteGroups[spriteGroup][i];
                    if (decorativeSprite == null) { continue; }
                    if (spriteGroup > 0)
                    {
                        int activeSpriteIndex = entityID % spriteGroups[spriteGroup].Length;
                        if (i != activeSpriteIndex)
                        {
                            animStates[decorativeSprite].IsActive = false;
                            continue;
                        }
                    }

                    //check if the sprite is active (whether it should be drawn or not)
                    var spriteState = animStates[decorativeSprite];
                    spriteState.IsActive = true;
                    foreach (PropertyConditional conditional in decorativeSprite.IsActiveConditionals)
                    {
                        if (!checkConditional(conditional))
                        {
                            spriteState.IsActive = false;
                            break;
                        }
                    }
                    if (!spriteState.IsActive) { continue; }

                    //check if the sprite should be animated
                    bool animate = true;
                    foreach (PropertyConditional conditional in decorativeSprite.AnimationConditionals)
                    {
                        if (!checkConditional(conditional)) { animate = false; break; }
                    }
                    if (!animate) { continue; }
                    spriteState.OffsetState += deltaTime;
                    spriteState.RotationState += deltaTime;
                }
            }
        }

        public void Remove()
        {
            Sprite?.Remove();
            Sprite = null;
        }
    }
}
