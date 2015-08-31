using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Subsurface.Particles
{
    class ParticlePrefab
    {
        public enum DrawTargetType { Air = 1, Water = 2, Both = 3 }

        public readonly string name;

        public readonly Sprite sprite;

        public readonly float angularVelocityMin, angularVelocityMax;

        public readonly float startRotationMin, startRotationMax;

        public readonly Vector2 startSizeMin, startSizeMax;
        public readonly Vector2 sizeChangeMin, sizeChangeMax;

        public readonly Color startColor;
        public readonly float startAlpha;

        public readonly Vector4 colorChange;
              
        public readonly float lifeTime;

        public readonly bool deleteOnHit;

        public readonly Vector2 velocityChange;

        public readonly DrawTargetType DrawTarget;

        public readonly bool rotateToDirection;

        public ParticlePrefab(XElement element)
        {
            name = element.Name.ToString();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "sprite") continue;

                sprite = new Sprite(subElement);
            }

            angularVelocityMin = ToolBox.GetAttributeFloat(element, "angularvelocitymin", 0.0f);
            angularVelocityMax = ToolBox.GetAttributeFloat(element, "angularvelocitymax", 0.0f);

            startSizeMin = ToolBox.GetAttributeVector2(element, "startsizemin", Vector2.One);
            startSizeMax = ToolBox.GetAttributeVector2(element, "startsizemax", Vector2.One);

            sizeChangeMin = ToolBox.GetAttributeVector2(element, "sizechangemin", Vector2.Zero);
            sizeChangeMax = ToolBox.GetAttributeVector2(element, "sizechangemax", Vector2.Zero);

            startRotationMin = ToolBox.GetAttributeFloat(element, "startrotationmin", 0.0f);
            startRotationMax = ToolBox.GetAttributeFloat(element, "startrotationmax", 0.0f);

            startColor = new Color(ToolBox.GetAttributeVector4(element, "startcolor", Vector4.One));
            startAlpha = ToolBox.GetAttributeFloat(element, "startalpha", 1.0f);

            deleteOnHit = ToolBox.GetAttributeBool(element, "deleteonhit", false);

            colorChange = ToolBox.GetAttributeVector4(element, "colorchange", Vector4.Zero);

            lifeTime = ToolBox.GetAttributeFloat(element, "lifetime", 5.0f);

            velocityChange = ToolBox.GetAttributeVector2(element, "velocitychange", Vector2.Zero);

            rotateToDirection = ToolBox.GetAttributeBool(element, "rotatetodirection", false);

            switch (ToolBox.GetAttributeString(element, "drawtarget", "air").ToLower())
            {
                case "air":
                default:
                    DrawTarget = DrawTargetType.Air;
                    break;
                case "water":
                    DrawTarget = DrawTargetType.Water;
                    break;
                case "both":
                    DrawTarget = DrawTargetType.Both;
                    break;
                
            }
        }
    }
}
