using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class DecorativeSprite : ISerializableEntity
    {
        public class State
        {
            public float RotationState;
            public float OffsetState;
            public bool IsActive = true;
        }

        public string Name => $"Decorative Sprite";
        public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

        public Sprite Sprite { get; private set; }

        public enum AnimationType
        {
            None,
            Sine,
            Noise
        }

        [Serialize("0,0", true), Editable]
        public Vector2 Offset { get; private set; }

        [Serialize(AnimationType.None, false), Editable]
        public AnimationType OffsetAnim { get; private set; }

        [Serialize(0.0f, true), Editable]
        public float OffsetAnimSpeed { get; private set; }

        private float rotationSpeedRadians;
        [Serialize(0.0f, true), Editable]
        public float RotationSpeed
        {
            get
            {
                return MathHelper.ToDegrees(rotationSpeedRadians);
            }
            private set
            {
                rotationSpeedRadians = MathHelper.ToRadians(value);
            }
        }

        private float rotationRadians;
        [Serialize(0.0f, true), Editable]
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

        private float scale;
        [Serialize(1.0f, true), Editable]
        public float Scale
        {
            get { return scale; }
            private set { scale = MathHelper.Clamp(value, 0.0f, 10.0f); }
        }

        [Serialize(AnimationType.None, false), Editable]
        public AnimationType RotationAnim { get; private set; }

        /// <summary>
        /// If > 0, only one sprite of the same group is used (chosen randomly)
        /// </summary>
        [Serialize(0, false, description: "If > 0, only one sprite of the same group is used (chosen randomly)"), Editable(ReadOnly = true)]
        public int RandomGroupID { get; private set; }

        [Serialize("1.0,1.0,1.0,1.0", true), Editable()]
        public Color Color { get; set; }

        /// <summary>
        /// The sprite is only drawn if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> IsActiveConditionals { get; private set; } = new List<PropertyConditional>();
        /// <summary>
        /// The sprite is only animated if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> AnimationConditionals { get; private set; } = new List<PropertyConditional>();

        public DecorativeSprite(XElement element, string path = "", string file = "", bool lazyLoad = false)
        {
            Sprite = new Sprite(element, path, file, lazyLoad: lazyLoad);
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            foreach (XElement subElement in element.Elements())
            {
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

        public Vector2 GetOffset(ref float offsetState)
        {
            if (OffsetAnimSpeed <= 0.0f)
            {
                return Offset;
            }
            switch (OffsetAnim)
            {
                case AnimationType.Sine:
                    offsetState %= (MathHelper.TwoPi / OffsetAnimSpeed);
                    return Offset * (float)Math.Sin(offsetState * OffsetAnimSpeed);
                case AnimationType.Noise:
                    offsetState %= (1.0f / (OffsetAnimSpeed * 0.1f));

                    float t = offsetState * 0.1f * OffsetAnimSpeed;
                    return new Vector2(
                        Offset.X * (PerlinNoise.GetPerlin(t, t) - 0.5f),
                        Offset.Y * (PerlinNoise.GetPerlin(t + 0.5f, t + 0.5f) - 0.5f));
                default:
                    return Offset;
            }
        }

        public float GetRotation(ref float rotationState)
        {
            if (rotationSpeedRadians <= 0.0f)
            {
                return rotationRadians;
            }
            switch (RotationAnim)
            {
                case AnimationType.Sine:
                    rotationState = rotationState % (MathHelper.TwoPi / rotationSpeedRadians);
                    return rotationRadians * (float)Math.Sin(rotationState * rotationSpeedRadians);
                case AnimationType.Noise:
                    rotationState = rotationState % (1.0f / rotationSpeedRadians);
                    return rotationRadians * (PerlinNoise.GetPerlin(rotationState * rotationSpeedRadians, rotationState * rotationSpeedRadians) - 0.5f);
                default:
                    return rotationState * rotationSpeedRadians;
            }
        }

        public static void UpdateSpriteStates(Dictionary<int, List<DecorativeSprite>> spriteGroups, Dictionary<DecorativeSprite, State> animStates, 
            int entityID, float deltaTime, Func<PropertyConditional,bool> checkConditional)
        {
            foreach (int spriteGroup in spriteGroups.Keys)
            {
                for (int i = 0; i < spriteGroups[spriteGroup].Count; i++)
                {
                    var decorativeSprite = spriteGroups[spriteGroup][i];
                    if (decorativeSprite == null) { continue; }
                    if (spriteGroup > 0)
                    {
                        int activeSpriteIndex = entityID % spriteGroups[spriteGroup].Count;
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
