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

        public readonly float AnimDuration;
        public readonly bool LoopAnim;

        public readonly float AngularVelocityMin, AngularVelocityMax;

        public readonly float StartRotationMin, StartRotationMax;

        public readonly Vector2 StartSizeMin, StartSizeMax;
        public readonly Vector2 SizeChangeMin, SizeChangeMax;

        public readonly float Drag, WaterDrag;

        public readonly Color StartColor;
        public readonly float StartAlpha;

        public readonly Vector4 ColorChange;
              
        public readonly float LifeTime;

        public readonly float GrowTime;

        public readonly float CollisionRadius;
        public readonly bool DeleteOnCollision;
        public readonly bool CollidesWithWalls;

        public readonly float Friction;
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

            AnimDuration = element.GetAttributeFloat("animduration", 1.0f);
            LoopAnim = element.GetAttributeBool("loopanim", true);

            if (element.Attribute("angularvelocity") == null)
            {
                AngularVelocityMin = element.GetAttributeFloat("angularvelocitymin", 0.0f);
                AngularVelocityMax = element.GetAttributeFloat("angularvelocitymax", 0.0f);
            }
            else
            {
                AngularVelocityMin = element.GetAttributeFloat("angularvelocity", 0.0f);
                AngularVelocityMax = AngularVelocityMin;
            }

            AngularVelocityMin = MathHelper.ToRadians(AngularVelocityMin);
            AngularVelocityMax = MathHelper.ToRadians(AngularVelocityMax);

            if (element.Attribute("startsize") == null)
            {
                StartSizeMin = element.GetAttributeVector2("startsizemin", Vector2.One);
                StartSizeMax = element.GetAttributeVector2("startsizemax", Vector2.One);
            }
            else
            {
                StartSizeMin = element.GetAttributeVector2("startsize", Vector2.One);
                StartSizeMax = StartSizeMin;
            }

            if (element.Attribute("sizechange") == null)
            {
                SizeChangeMin = element.GetAttributeVector2("sizechangemin", Vector2.Zero);
                SizeChangeMax = element.GetAttributeVector2("sizechangemax", Vector2.Zero);
            }
            else
            {
                SizeChangeMin = element.GetAttributeVector2("sizechange", Vector2.Zero);
                SizeChangeMax = SizeChangeMin;
            }

            Drag = element.GetAttributeFloat("drag", 0.0f);
            WaterDrag = element.GetAttributeFloat("waterdrag", 0.0f);
            
            Friction = element.GetAttributeFloat("friction", 0.5f);
            Restitution = element.GetAttributeFloat("restitution", 0.5f);

            switch (element.GetAttributeString("blendstate", "alphablend"))
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

            GrowTime = element.GetAttributeFloat("growtime", 0.0f);

            if (element.Attribute("startrotation") == null)
            {
                StartRotationMin = element.GetAttributeFloat("startrotationmin", 0.0f);
                StartRotationMax = element.GetAttributeFloat("startrotationmax", 0.0f);
            }
            else
            {
                StartRotationMin = element.GetAttributeFloat("startrotation", 0.0f);
                StartRotationMax = StartRotationMin;
            }

            StartRotationMin = MathHelper.ToRadians(StartRotationMin);
            StartRotationMax = MathHelper.ToRadians(StartRotationMax);

            StartColor = new Color(element.GetAttributeVector4("startcolor", Vector4.One));
            StartAlpha = element.GetAttributeFloat("startalpha", 1.0f);

            DeleteOnCollision = element.GetAttributeBool("deleteoncollision", false);
            CollidesWithWalls = element.GetAttributeBool("collideswithwalls", false);

            CollisionRadius = element.GetAttributeFloat("collisionradius",
                Sprites.Count > 0 ? 1 : Sprites[0].SourceRect.Width / 2.0f);

            ColorChange = element.GetAttributeVector4("colorchange", Vector4.Zero);

            LifeTime = element.GetAttributeFloat("lifetime", 5.0f);

            VelocityChange = element.GetAttributeVector2("velocitychange", Vector2.Zero);
            VelocityChange = ConvertUnits.ToDisplayUnits(VelocityChange);

            RotateToDirection = element.GetAttributeBool("rotatetodirection", false);

            switch (element.GetAttributeString("drawtarget", "air").ToLowerInvariant())
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

        public Vector2 CalculateEndPosition(Vector2 startPosition, Vector2 velocity)
        {
            //endPos = x + vt + 1/2 * at^2
            return startPosition + velocity * LifeTime + 0.5f * VelocityChange * LifeTime * LifeTime;
        }
    }
}
