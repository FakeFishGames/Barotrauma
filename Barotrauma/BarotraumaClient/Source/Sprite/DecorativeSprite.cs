using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class DecorativeSprite
    {
        public Sprite Sprite { get; private set; }

        public enum AnimationType
        {
            None,
            Sine,
            Noise
        }

        [Serialize("0,0", false)]
        public Vector2 Offset { get; private set; }

        [Serialize(AnimationType.None, false)]
        public AnimationType OffsetAnim { get; private set; }

        [Serialize(0.0f, false)]
        public float OffsetAnimSpeed { get; private set; }

        private float rotationSpeedRadians;
        [Serialize(0.0f, false)]
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

        [Serialize(0.0f, false)]
        public float Rotation { get; private set; }

        [Serialize(AnimationType.None, false)]
        public AnimationType RotationAnim { get; private set; }

        /// <summary>
        /// If > 0, only one sprite of the same group is used (chosen randomly)
        /// </summary>
        [Serialize(0, false)]
        public int RandomGroupID { get; private set; }

        /// <summary>
        /// The sprite is only drawn if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> IsActiveConditionals { get; private set; } = new List<PropertyConditional>();
        /// <summary>
        /// The sprite is only animated if these conditions are fulfilled
        /// </summary>
        internal List<PropertyConditional> AnimationConditionals { get; private set; } = new List<PropertyConditional>();

        public DecorativeSprite(XElement element, string path = "", bool lazyLoad = false)
        {
            Sprite = new Sprite(element, path, lazyLoad: lazyLoad);
            SerializableProperty.DeserializeProperties(this, element);

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
                    if (attribute.Name.ToString().ToLowerInvariant() == "targetitemcomponent") { continue; }
                    conditionalList.Add(new PropertyConditional(attribute));
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
                    offsetState = offsetState % (MathHelper.TwoPi / OffsetAnimSpeed);
                    return Offset * (float)Math.Sin(offsetState * OffsetAnimSpeed);
                case AnimationType.Noise:
                    offsetState = offsetState % (1.0f / (OffsetAnimSpeed * 0.1f));

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
                return Rotation;
            }
            switch (OffsetAnim)
            {
                case AnimationType.Sine:
                    rotationState = rotationState % (MathHelper.TwoPi / rotationSpeedRadians);
                    return Rotation * (float)Math.Sin(rotationState * rotationSpeedRadians);
                case AnimationType.Noise:
                    rotationState = rotationState % (1.0f / rotationSpeedRadians);
                    return Rotation * PerlinNoise.GetPerlin(rotationState * rotationSpeedRadians, rotationState * rotationSpeedRadians);
                default:
                    return rotationState * rotationSpeedRadians;
            }
        }

        public void Remove()
        {
            Sprite?.Remove();
            Sprite = null;
        }
    }
}
