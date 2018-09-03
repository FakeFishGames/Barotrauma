using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.SpriteDeformations
{
    abstract class SpriteDeformation
    {
        public enum BlendMode
        {
            Add, 
            Multiply,
            Override
        }

        protected Vector2[,] Deformation { get; private set; }
        private Point _resolution;
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
        /// <summary>
        /// A negative value means that the deformation is used only by one sprite only (default). 
        /// A positive value means that this deformation is or could be used for multiple sprites.
        /// This behaviour is not automatic, and has to be implemented for any particular case separately (currently only used in Limbs).
        /// </summary>
        public readonly int sync;
        public readonly string typeName;
        protected BlendMode blendMode;

        /// <summary>
        /// Defined in the shader.
        /// </summary>
        public static readonly Point shaderMaxResolution = new Point(15, 15);

        public static SpriteDeformation Load(XElement element)
        {
            string typeName = element.GetAttributeString("type", "");
            switch (typeName.ToLowerInvariant())
            {
                case "inflate":
                    return new Inflate(element);
                case "custom":
                    return new CustomDeformation(element);
                case "noise":
                    return new NoiseDeformation(element);
                case "bezier":
                    return new BezierDeformation(element);
                case "reacttotriggerers":
                default:
                    if (Enum.TryParse(typeName, out PositionalDeformation.ReactionType reactionType))
                    {
                        return new PositionalDeformation(element)
                        {
                            Type = reactionType
                        };
                    }
                    else
                    {
                        DebugConsole.ThrowError("Could not load sprite deformation animation - \"" + typeName + "\" is not a valid deformation type.");
                    }
                    return null;
            }
        }

        protected SpriteDeformation(XElement element)
        {
            typeName = element.GetAttributeString("type", "").ToLowerInvariant();
            sync = element.GetAttributeInt("sync", -1);
            string blendModeStr = element.GetAttributeString("blendmode", "override");
            if (!Enum.TryParse(blendModeStr, true, out blendMode))
            {
                DebugConsole.ThrowError("Error in SpriteDeformation - \""+blendModeStr+"\" is not a valid blend mode");
                blendMode = BlendMode.Add;
            }
            Resolution = element.GetAttributeVector2("resolution", Vector2.One * 2).ToPoint();
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
                        switch (animation.blendMode)
                        {
                            case BlendMode.Override:
                                deformation[x,y] = animDeformation[x,y] * scale * multiplier;
                                break;
                            case BlendMode.Add:
                                deformation[x, y] += animDeformation[x, y] * scale * multiplier;
                                break;
                            case BlendMode.Multiply:
                                deformation[x, y] *= animDeformation[x, y] * multiplier;
                                break;
                        }
                    }
                }
            }
            return deformation;
        }
    }
}
