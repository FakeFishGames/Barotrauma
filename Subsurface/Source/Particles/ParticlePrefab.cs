using System.Xml.Linq;
using Microsoft.Xna.Framework;
using FarseerPhysics;
using System.Collections.Generic;

namespace Barotrauma.Particles
{
    class ParticlePrefab
    {
        public enum DrawTargetType { Air = 1, Water = 2, Both = 3 }

        public readonly string Name;

        public readonly List<Sprite> Sprites;

        public readonly float AngularVelocityMin, AngularVelocityMax;

        public readonly float StartRotationMin, StartRotationMax;

        public readonly Vector2 StartSizeMin, StartSizeMax;
        public readonly Vector2 SizeChangeMin, SizeChangeMax;

        public readonly Color StartColor;
        public readonly float StartAlpha;

        public readonly Vector4 ColorChange;
              
        public readonly float LifeTime;

        public readonly float GrowTime;

        public readonly float CollisionRadius;
        public readonly bool DeleteOnCollision;
        public readonly bool CollidesWithWalls;

        public readonly float Restitution;

        public readonly Vector2 VelocityChange;

        public readonly DrawTargetType DrawTarget;

        public readonly ParticleBlendState BlendState;

        public readonly bool RotateToDirection;

        public ParticlePrefab(XElement element)
        {
            Name = element.Name.ToString();

            Sprites = new List<Sprite>();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;

                Sprites.Add(new Sprite(subElement));
            }

            if (element.Attribute("angularvelocity") == null)
            {
                AngularVelocityMin = ToolBox.GetAttributeFloat(element, "angularvelocitymin", 0.0f);
                AngularVelocityMax = ToolBox.GetAttributeFloat(element, "angularvelocitymax", 0.0f);
            }
            else
            {
                AngularVelocityMin = ToolBox.GetAttributeFloat(element, "angularvelocity", 0.0f);
                AngularVelocityMax = AngularVelocityMin;
            }

            if (element.Attribute("startsize") == null)
            {
                StartSizeMin = ToolBox.GetAttributeVector2(element, "startsizemin", Vector2.One);
                StartSizeMax = ToolBox.GetAttributeVector2(element, "startsizemax", Vector2.One);
            }
            else
            {
                StartSizeMin = ToolBox.GetAttributeVector2(element, "startsize", Vector2.One);
                StartSizeMax = StartSizeMin;
            }

            if (element.Attribute("sizechange") == null)
            {
                SizeChangeMin = ToolBox.GetAttributeVector2(element, "sizechangemin", Vector2.Zero);
                SizeChangeMax = ToolBox.GetAttributeVector2(element, "sizechangemax", Vector2.Zero);
            }
            else
            {
                SizeChangeMin = ToolBox.GetAttributeVector2(element, "sizechange", Vector2.Zero);
                SizeChangeMax = SizeChangeMin;
            }

            Restitution = ToolBox.GetAttributeFloat(element, "restitution", 0.5f);

            switch (ToolBox.GetAttributeString(element, "blendstate", "alphablend"))
            {
                case "alpha":
                case "alphablend":
                    BlendState = ParticleBlendState.AlphaBlend;
                    break;
                case "add":
                case "additive":
                    BlendState = ParticleBlendState.Additive;
                    break;
                case "distort":
                case "distortion":
                    BlendState = ParticleBlendState.Distortion;
                    break;
            }

            GrowTime = ToolBox.GetAttributeFloat(element, "growtime", 0.0f);

            if (element.Attribute("startrotation") == null)
            {
                StartRotationMin = ToolBox.GetAttributeFloat(element, "startrotationmin", 0.0f);
                StartRotationMax = ToolBox.GetAttributeFloat(element, "startrotationmax", 0.0f);
            }
            else
            {
                StartRotationMin = ToolBox.GetAttributeFloat(element, "startrotation", 0.0f);
                StartRotationMax = StartRotationMin;
            }

            StartRotationMin = MathHelper.ToRadians(StartRotationMin);
            StartRotationMax = MathHelper.ToRadians(StartRotationMax);

            StartColor = new Color(ToolBox.GetAttributeVector4(element, "startcolor", Vector4.One));
            StartAlpha = ToolBox.GetAttributeFloat(element, "startalpha", 1.0f);

            DeleteOnCollision = ToolBox.GetAttributeBool(element, "deleteoncollision", false);
            CollidesWithWalls = ToolBox.GetAttributeBool(element, "collideswithwalls", false);

            CollisionRadius = ToolBox.GetAttributeFloat(element, "collisionradius", Sprites[0].SourceRect.Width/2.0f);

            ColorChange = ToolBox.GetAttributeVector4(element, "colorchange", Vector4.Zero);

            LifeTime = ToolBox.GetAttributeFloat(element, "lifetime", 5.0f);

            VelocityChange = ToolBox.GetAttributeVector2(element, "velocitychange", Vector2.Zero);
            VelocityChange = ConvertUnits.ToDisplayUnits(VelocityChange);

            RotateToDirection = ToolBox.GetAttributeBool(element, "rotatetodirection", false);

            switch (ToolBox.GetAttributeString(element, "drawtarget", "air").ToLowerInvariant())
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
