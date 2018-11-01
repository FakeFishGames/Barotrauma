using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.SpriteDeformations
{
    abstract class SpriteDeformation : ISerializableEntity
    {
        public enum DeformationBlendMode
        {
            Add, 
            Multiply,
            Override
        }

        protected Vector2[,] Deformation { get; private set; }
        private Point _resolution;
        
        [Serialize("2,2", true), Editable]
        public Point Resolution
        {
            get { return _resolution;}
            set
            {
                if (_resolution == value) { return; }
                _resolution = value.Clamp(new Point(2, 2), shaderMaxResolution);
                Deformation = new Vector2[_resolution.X, _resolution.Y];
            }
        }

        public string Name => GetType().Name;

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        /// <summary>
        /// A negative value means that the deformation is used only by one sprite only (default). 
        /// A positive value means that this deformation is or could be used for multiple sprites.
        /// This behaviour is not automatic, and has to be implemented for any particular case separately (currently only used in Limbs).
        /// </summary>
        [Serialize(-1, true)]
        public int Sync
        {
            get;
            private set;
        }

        [Serialize("", true)]
        public string TypeName
        {
            get;
            private set;
        }

        [Serialize(DeformationBlendMode.Add, true), Editable]
        public DeformationBlendMode BlendMode
        {
            get;
            set;
        }

        /// <summary>
        /// Defined in the shader.
        /// </summary>
        public static readonly Point shaderMaxResolution = new Point(15, 15);

        private static readonly string[] deformationTypes = new string[] { "Inflate", "Custom", "Noise", "BendJoint", "ReactToTriggerers" };
        public static IEnumerable<string> DeformationTypes
        {
            get { return deformationTypes; }
        }

        public static SpriteDeformation Load(string deformationType)
        {
            return Load(null, deformationType);
        }
        public static SpriteDeformation Load(XElement element)
        {
            return Load(element, null);
        }

        private static SpriteDeformation Load(XElement element, string deformationType)
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
                        DebugConsole.ThrowError("Could not load sprite deformation animation - \"" + typeName + "\" is not a valid deformation type.");
                    }
                    break;
            }

            if (newDeformation != null)
            {
                newDeformation.TypeName = typeName;
            }
            return newDeformation;
        }

        protected SpriteDeformation(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            TypeName = element.GetAttributeString("type", "").ToLowerInvariant();
            if (element == null)
            {
                Resolution = new Point(2, 2);
            }
            else
            {
                Resolution = element.GetAttributeVector2("resolution", Vector2.One * 2).ToPoint();
            }

        }

        protected abstract void GetDeformation(out Vector2[,] deformation, out float multiplier);

        public abstract void Update(float deltaTime);

        public static Vector2[,] GetDeformation(IEnumerable<SpriteDeformation> animations, Vector2 scale)
        {
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
                animation.GetDeformation(out Vector2[,] animDeformation, out float multiplier);

                for (int x = 0; x < resolution.X; x++)
                {
                    for (int y = 0; y < resolution.Y; y++)
                    {
                        switch (animation.BlendMode)
                        {
                            case DeformationBlendMode.Override:
                                deformation[x,y] = animDeformation[x,y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Add:
                                deformation[x, y] += animDeformation[x, y] * scale * multiplier;
                                break;
                            case DeformationBlendMode.Multiply:
                                deformation[x, y] *= animDeformation[x, y] * multiplier;
                                break;
                        }
                    }
                }
            }
            return deformation;
        }

        public virtual void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
        }
    }
}
