using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.SpriteDeformations
{
    abstract class SpriteDeformationParams : ISerializableEntity
    {
        /// <summary>
        /// A negative value means that the deformation is used only by one sprite only (default). 
        /// A positive value means that this deformation is or could be used for multiple sprites.
        /// This behaviour is not automatic, and has to be implemented for any particular case separately (currently only used in Limbs).
        /// </summary>
        [Serialize(-1, IsPropertySaveable.Yes), Editable(minValue: -1, maxValue: 100)]
        public int Sync
        {
            get;
            private set;
        }

        [Serialize("", IsPropertySaveable.Yes)]
        public string TypeName
        {
            get;
            set;
        }

        [Serialize(SpriteDeformation.DeformationBlendMode.Add, IsPropertySaveable.Yes), Editable]
        public SpriteDeformation.DeformationBlendMode BlendMode
        {
            get;
            set;
        }

        public string Name => $"Deformation ({TypeName})";

        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2, ValueStep = 0.01f)]
        public float Strength { get; private set; }

        [Serialize(90f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0, MaxValueFloat = 90)]
        public float MaxRotation { get; private set; }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool UseMovementSine { get; set; }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool StopWhenHostIsDead { get; set; }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool OnlyInWater { get; set; }

        /// <summary>
        /// Only used if UseMovementSine is enabled. Multiplier for Pi.
        /// </summary>
        [Serialize(0f, IsPropertySaveable.Yes), Editable]
        public float SineOffset { get; set; }

        public virtual float Frequency { get; set; } = 1;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        /// <summary>
        /// Defined in the shader.
        /// </summary>
        public static readonly Point ShaderMaxResolution = new Point(15, 15);

        private Point _resolution;
        [Serialize("2,2", IsPropertySaveable.Yes)]
        public Point Resolution
        {
            get { return _resolution; }
            set
            {
                if (_resolution == value) { return; }
                _resolution = value.Clamp(new Point(2, 2), ShaderMaxResolution);
            }
        }

        public SpriteDeformationParams(XElement element)
        {
            if (element != null)
            {
                TypeName = element.GetAttributeString("type", "").ToLowerInvariant();
            }
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }

    abstract class SpriteDeformation
    {
        public enum DeformationBlendMode
        {
            Add, 
            Multiply,
            Override
        }

        public virtual float Phase { get; set; }

        protected Vector2[,] Deformation { get; private set; }

        public SpriteDeformationParams Params { get; set; }

        private static readonly string[] deformationTypes = new string[] { "Inflate", "Custom", "Noise", "BendJoint", "ReactToTriggerers" };
        public static IEnumerable<string> DeformationTypes
        {
            get { return deformationTypes; }
        }

        public Point Resolution
        {
            get { return Params.Resolution; }
            set { SetResolution(value); }
        }

        public string TypeName => Params.TypeName;

        public int Sync => Params.Sync;

        public static SpriteDeformation Load(string deformationType, string parentDebugName)
        {
            return Load(null, deformationType, parentDebugName);
        }
        public static SpriteDeformation Load(XElement element, string parentDebugName)
        {
            return Load(element, null, parentDebugName);
        }

        private static SpriteDeformation Load(XElement element, string deformationType, string parentDebugName)
        {
            string typeName = deformationType;

            if (element != null)
            {
                typeName = element.GetAttributeString("typename", null) ?? element.GetAttributeString("type", "");
            }
            
            SpriteDeformation newDeformation = null;
            switch (typeName.ToLowerInvariant())
            {
                case "inflate":
                    newDeformation = new Inflate(element);
                    break;
                case "custom":
                    newDeformation = new CustomDeformation(element);
                    break;
                case "noise":
                    newDeformation = new NoiseDeformation(element);
                    break;
                case "jointbend":
                case "bendjoint":
                    newDeformation = new JointBendDeformation(element);
                    break;
                case "reacttotriggerers":
                    return new PositionalDeformation(element);
                default:
                    if (Enum.TryParse(typeName, out PositionalDeformation.ReactionType reactionType))
                    {
                        newDeformation = new PositionalDeformation(element)
                        {
                            Type = reactionType
                        };
                    }
                    else
                    {
                        DebugConsole.ThrowError("Could not load sprite deformation animation in " + parentDebugName + " - \"" + typeName + "\" is not a valid deformation type.");
                    }
                    break;
            }

            if (newDeformation != null)
            {
                newDeformation.Params.TypeName = typeName;
            }
            return newDeformation;
        }

        protected SpriteDeformation(XElement element, SpriteDeformationParams deformationParams)
        {
            this.Params = deformationParams;
            SerializableProperty.DeserializeProperties(deformationParams, element);
            Deformation = new Vector2[deformationParams.Resolution.X, deformationParams.Resolution.Y];
        }

        public void SetResolution(Point resolution)
        {
            Params.Resolution = resolution;
            Deformation = new Vector2[Params.Resolution.X, Params.Resolution.Y];
        }

        protected abstract void GetDeformation(out Vector2[,] deformation, out float multiplier, bool inverse);

        public abstract void Update(float deltaTime);

        private static readonly List<int> yValues = new List<int>();
        public static Vector2[,] GetDeformation(IEnumerable<SpriteDeformation> animations, Vector2 scale, bool inverseY = false)
        {
            foreach (SpriteDeformation animation in animations)
            {
                if (animation.Params.Resolution.X != animation.Deformation.GetLength(0) ||
                    animation.Params.Resolution.Y != animation.Deformation.GetLength(1))
                {
                    animation.Deformation = new Vector2[animation.Params.Resolution.X, animation.Params.Resolution.Y];
                }
            }

            Point resolution = animations.First().Resolution;
            if (animations.Any(a => a.Resolution != resolution))
            {
                DebugConsole.ThrowError("All animations must have the same resolution! Using the lowest resolution.");
                resolution = animations.OrderBy(anim => anim.Resolution.X + anim.Resolution.Y).First().Resolution;
                animations.ForEach(a => a.Resolution = resolution);
            }

            Vector2[,] deformation = new Vector2[resolution.X, resolution.Y];
            foreach (SpriteDeformation animation in animations)
            {
                yValues.Clear();
                for (int y = 0; y < resolution.Y; y++)
                {
                    yValues.Add(y);
                }
                if (inverseY && animation is CustomDeformation)
                {
                    yValues.Reverse();
                }
                animation.GetDeformation(out Vector2[,] animDeformation, out float multiplier, inverseY);
                for (int x = 0; x < resolution.X; x++)
                {
                    for (int y = 0; y < resolution.Y; y++)
                    {
                        switch (animation.Params.BlendMode)
                        {
                            case DeformationBlendMode.Override:
                                deformation[x, yValues[y]] = animDeformation[x, y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Add:
                                deformation[x, yValues[y]] += animDeformation[x, y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Multiply:
                                deformation[x, yValues[y]] *= animDeformation[x, y] * multiplier;
                                break;
                        }
                    }
                }
            }
            return deformation;
        }

        public virtual void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(Params, element);
        }
    }
}
