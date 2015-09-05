using FarseerPhysics;
using Microsoft.Xna.Framework;
using Subsurface.Lights;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class Explosion
    {
        private Vector2 position;

        private float range;
        private float damage;
        private float structureDamage;
        private float stun;

        private float force;

        private LightSource light;

        public float CameraShake;

        public Explosion(Vector2 position, float range, float damage, float structureDamage, float stun = 0.0f, float force = 0.0f)
        {
            this.position = position;
            this.range = Math.Max(range, 1.0f);
            this.damage = damage;
            this.structureDamage = structureDamage;
            this.stun = stun;
            this.force = force;

            CameraShake = range*10.0f;
        }

        public Explosion(XElement element)
        {
            range = Math.Max(ToolBox.GetAttributeFloat(element, "range", 1.0f), 1.0f);
            damage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            structureDamage = ToolBox.GetAttributeFloat(element, "structuredamage", 0.0f);
            stun = ToolBox.GetAttributeFloat(element, "stun", 0.0f);

            force = ToolBox.GetAttributeFloat(element, "force", 0.0f);
        }

        public void Explode()
        {
            Explode(position);
        }

        public void Explode(Vector2 simPosition)
        {
            Vector2 displayPosition = ConvertUnits.ToDisplayUnits(simPosition);

            Game1.ParticleManager.CreateParticle("shockwave", displayPosition,
                Vector2.Zero, 0.0f);

            for (int i = 0; i < range * 10; i++)
            {
                Game1.ParticleManager.CreateParticle("spark", displayPosition,
                    Rand.Vector(Rand.Range(500.0f, 800.0f)), 0.0f);

                Game1.ParticleManager.CreateParticle("explosionfire", displayPosition + Rand.Vector(50f),
                    Rand.Vector(Rand.Range(50f, 100.0f)), 0.0f);
            }



            float displayRange = ConvertUnits.ToDisplayUnits(range);

            light = new LightSource(displayPosition, displayRange, Color.LightYellow);
            CoroutineManager.StartCoroutine(DimLight());

            float cameraDist = Vector2.Distance(Game1.GameScreen.Cam.Position, displayPosition)/2.0f;
            Game1.GameScreen.Cam.Shake = CameraShake * Math.Max((displayRange - cameraDist)/displayRange, 0.0f);
            
            if (structureDamage > 0.0f)
            {
                List<Structure> structureList = new List<Structure>();
            
                float dist = 600.0f;
                foreach (MapEntity entity in MapEntity.mapEntityList)
                {
                    Structure structure = entity as Structure;
                    if (structure == null) continue;

                    if (structure.HasBody && 
                        !structure.IsPlatform &&
                        Vector2.Distance(structure.Position, displayPosition) < dist*3.0f)
                    {
                        structureList.Add(structure);
                    }
                }

                foreach (Structure structure in structureList)
                {
                    for (int i = 0; i < structure.SectionCount; i++)
                    {
                        float distFactor = 1.0f - (Vector2.Distance(structure.SectionPosition(i), displayPosition) / displayRange);
                        if (distFactor > 0.0f) structure.AddDamage(i, structureDamage*distFactor);
                    }
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                float dist = Vector2.Distance(c.SimPosition, simPosition);

                if (dist > range) continue;

                float distFactor = 1.0f - dist / range;
                                
                foreach (Limb limb in c.AnimController.limbs)
                {
                    distFactor = 1.0f - Vector2.Distance(limb.SimPosition, simPosition)/range;

                    c.AddDamage(limb.SimPosition, DamageType.None, damage / c.AnimController.limbs.Length * distFactor, 0.0f, stun * distFactor);
                    
                    if (force>0.0f)
                    {
                        limb.body.ApplyLinearImpulse(Vector2.Normalize(limb.SimPosition - simPosition) * distFactor * force);
                    }
                }
            }
        }

        private IEnumerable<object> DimLight()
        {
            float currBrightness= 1.0f;
            float startRange = light.Range;

            while (light.Color.A > 0.0f)
            {
                light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, currBrightness);
                light.Range = startRange * currBrightness;

                currBrightness -= 0.05f;

                yield return CoroutineStatus.Running;
            }

            light.Remove();
            
            yield return CoroutineStatus.Success;
        }
    }
}
