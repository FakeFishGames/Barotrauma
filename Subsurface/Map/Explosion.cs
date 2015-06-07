using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class Explosion
    {
        Vector2 position;


        float range;
        float damage;
        float structureDamage;
        float stun;

        float force;

        public Explosion(Vector2 position, float range, float damage, float structureDamage, float stun=0.0f, float force=0.0f)
        {
            this.position = position;
            this.range = Math.Max(range,1.0f);
            this.damage = damage;
            this.structureDamage = structureDamage;
            this.stun = stun;
            this.force = force;
        }

        public Explosion(XElement element)
        {
            range = Math.Max(ToolBox.GetAttributeFloat(element, "range", 1.0f),1.0f);
            damage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            structureDamage = ToolBox.GetAttributeFloat(element, "structuredamage", 0.0f);
            stun = ToolBox.GetAttributeFloat(element, "stun", 0.0f);

            force = ToolBox.GetAttributeFloat(element, "force", 0.0f);
        }

        public void Explode()
        {
            Explode(position);
        }

        public void Explode(Vector2 position)
        {
            for (int i = 0; i<range*10; i++)
            {
                Game1.particleManager.CreateParticle("explosionfire", position,
                    Vector2.Normalize(new Vector2(ToolBox.RandomFloat(-1.0f, 1.0f), ToolBox.RandomFloat(-1.0f, 1.0f))) * ToolBox.RandomFloat(3.0f, 4.0f),
                    0.0f);
            }

            Vector2 displayPosition = ConvertUnits.ToDisplayUnits(position);
            float displayRange = ConvertUnits.ToDisplayUnits(range);

            if (structureDamage>0.0f)
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

            foreach (Character c in Character.characterList)
            {
                float dist = Vector2.Distance(c.SimPosition, position);

                if (dist > range) continue;

                float distFactor = 1.0f - dist / range;
                                
                foreach (Limb limb in c.animController.limbs)
                {
                    distFactor = 1.0f - Vector2.Distance(limb.SimPosition, position)/range;

                    c.AddDamage(limb.SimPosition, DamageType.None, damage * distFactor, 0.0f, stun * distFactor);
                    
                    if (force>0.0f)
                    {
                        limb.body.ApplyLinearImpulse(Vector2.Normalize(limb.SimPosition-position)*distFactor*force);
                    }
                }
            }
        }
    }
}
