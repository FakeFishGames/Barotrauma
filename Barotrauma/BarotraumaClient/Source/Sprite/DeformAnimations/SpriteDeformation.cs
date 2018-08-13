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

        protected Point resolution;

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
            string blendModeStr = element.GetAttributeString("blendmode", "override");
            if (!Enum.TryParse(blendModeStr, true, out blendMode))
            {
                DebugConsole.ThrowError("Error in SpriteDeformation - \""+blendModeStr+"\" is not a valid blend mode");
                blendMode = BlendMode.Add;
            }

            resolution = element.GetAttributeVector2("resolution", Vector2.One * 2).ToPoint();
            resolution = resolution.Clamp(new Point(1, 1), shaderMaxResolution);
        }

        protected abstract void GetDeformation(out Vector2[,] deformation, out float multiplier);

        public abstract void Update(float deltaTime);

        public static Vector2[,] GetDeformation(IEnumerable<SpriteDeformation> animations, Vector2 scale)
        {
            Point resolution = animations.First().resolution;
            foreach (SpriteDeformation animation in animations)
            {
                if (animation.resolution != resolution)
                {
                    DebugConsole.ThrowError("All animations must have the same resolution! Using the resolution of the first animation.");
                    animation.resolution = resolution;
                }
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
