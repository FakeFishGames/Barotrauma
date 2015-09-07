using System.Xml.Linq;
using Microsoft.Xna.Framework;
using FarseerPhysics;
using System;

namespace Subsurface.Particles
{
    class ParticleEmitterPrefab
    {        
        public readonly string Name;

        public readonly ParticlePrefab particlePrefab;

        public readonly float AngleMin, AngleMax;

        public readonly float VelocityMin, VelocityMax;

        public readonly float ParticleAmount;

        public ParticleEmitterPrefab(XElement element)
        {
            Name = element.Name.ToString();

            particlePrefab = Game1.ParticleManager.FindPrefab(ToolBox.GetAttributeString(element, "particle", ""));

            if (element.Attribute("startrotation") == null)
            {
                AngleMin = ToolBox.GetAttributeFloat(element, "anglemin", 0.0f);
                AngleMax = ToolBox.GetAttributeFloat(element, "anglemax", 0.0f);
            }
            else
            {
                AngleMin = ToolBox.GetAttributeFloat(element, "angle", 0.0f);
                AngleMax = AngleMin;
            }

            AngleMin = MathHelper.ToRadians(AngleMin);
            AngleMin = MathHelper.ToRadians(AngleMax);

            if (element.Attribute("velocity") == null)
            {
                VelocityMin = ToolBox.GetAttributeFloat(element, "velocitymin", 0.0f);
                VelocityMax = ToolBox.GetAttributeFloat(element, "velocitymax", 0.0f);
            }
            else
            {
                VelocityMin = ToolBox.GetAttributeFloat(element, "velocity", 0.0f);
                VelocityMax = VelocityMin;
            }

            ParticleAmount = ToolBox.GetAttributeInt(element, "particleamount", 1);
        }

        public void Emit(Vector2 position)
        {
            for (int i = 0; i<ParticleAmount; i++)
            {
                float angle = Rand.Range(AngleMin, AngleMax);
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Rand.Range(VelocityMin, VelocityMax);

                Game1.ParticleManager.CreateParticle(particlePrefab, position, velocity);
            }
        }
    }
}
