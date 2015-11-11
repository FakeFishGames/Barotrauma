using FarseerPhysics;
using Microsoft.Xna.Framework;
using Barotrauma.Lights;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class Explosion
    {
        private Vector2 position;

        private Attack attack;
        
        private float force;

        private LightSource light;

        public float CameraShake;

        private bool sparks, shockwave, flames;

        public Explosion(XElement element)
        {
            attack = new Attack(element);

            force = ToolBox.GetAttributeFloat(element, "force", 0.0f);

            sparks      = ToolBox.GetAttributeBool(element, "sparks", true);
            shockwave   = ToolBox.GetAttributeBool(element, "shockwave", true);
            flames      = ToolBox.GetAttributeBool(element, "flames", true); 

            CameraShake = ToolBox.GetAttributeFloat(element, "camerashake", attack.Range*10.0f);
        }

        public void Explode()
        {
            Explode(position);
        }

        public void Explode(Vector2 simPosition)
        {
            Vector2 displayPosition = ConvertUnits.ToDisplayUnits(simPosition);

            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", displayPosition,
                    Vector2.Zero, 0.0f);
            }


            for (int i = 0; i < attack.Range * 10; i++)
            {
                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", displayPosition,
                        Rand.Vector(Rand.Range(500.0f, 800.0f)), 0.0f);
                }
                if (flames)
                {
                    GameMain.ParticleManager.CreateParticle("explosionfire", displayPosition + Rand.Vector(50f),
                        Rand.Vector(Rand.Range(50f, 100.0f)), 0.0f);
                }
            }

            float displayRange = ConvertUnits.ToDisplayUnits(attack.Range);

            light = new LightSource(displayPosition, displayRange, Color.LightYellow);
            CoroutineManager.StartCoroutine(DimLight());

            float cameraDist = Vector2.Distance(GameMain.GameScreen.Cam.Position, displayPosition)/2.0f;
            GameMain.GameScreen.Cam.Shake = CameraShake * Math.Max((displayRange - cameraDist)/displayRange, 0.0f);
            
            if (attack.GetStructureDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(displayPosition, displayRange, attack.GetStructureDamage(1.0f));
            }

            if (force == 0.0f && attack.Stun == 0.0f && attack.GetDamage(1.0f) == 0.0f) return;

            Hull hull = Hull.FindHull(displayPosition);

            foreach (Item item in Item.ItemList)
            {
                if (item.body == null || item.CurrentHull != hull) continue;

                Vector2 dir = (item.SimPosition == simPosition) ? Rand.Vector(1.0f) : Vector2.Normalize(item.SimPosition - simPosition);
                float distFactor = 1.0f - Vector2.Distance(item.SimPosition, simPosition) / attack.Range;

                item.body.ApplyLinearImpulse(dir * distFactor * force);
            }

            foreach (Character c in Character.CharacterList)
            {
                float dist = Vector2.Distance(c.SimPosition, simPosition);

                if (dist > attack.Range) continue;

                float distFactor = 1.0f - dist / attack.Range;
                                
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.SimPosition == simPosition) continue;
                    distFactor = 1.0f - Vector2.Distance(limb.SimPosition, simPosition)/attack.Range;
                    
                    c.AddDamage(limb.SimPosition, DamageType.None, 
                        attack.GetDamage(1.0f) / c.AnimController.Limbs.Length * distFactor, 0.0f, attack.Stun * distFactor, false);
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

        public static void RangedStructureDamage(Vector2 displayPosition, float displayRange, float damage)
        {
            List<Structure> structureList = new List<Structure>();


            float dist = 600.0f;
            foreach (MapEntity entity in MapEntity.mapEntityList)
            {
                Structure structure = entity as Structure;
                if (structure == null) continue;

                if (structure.HasBody &&
                    !structure.IsPlatform &&
                    Vector2.Distance(structure.Position, displayPosition) < dist * 3.0f)
                {
                    structureList.Add(structure);
                }
            }

            foreach (Structure structure in structureList)
            {
                for (int i = 0; i < structure.SectionCount; i++)
                {
                    float distFactor = 1.0f - (Vector2.Distance(structure.SectionPosition(i), displayPosition) / displayRange);
                    if (distFactor > 0.0f) structure.AddDamage(i, damage * distFactor);
                }
            }
        }
    }
}
